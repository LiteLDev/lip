using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Flurl;
using Flurl.Http;

namespace Lip.Core.Git;

/// A managed client for the git smart-HTTP protocol version 2 (read-only, upload-pack).
///
/// ListTree uses a shallow partial fetch (deepen 1, filter blob:none) so only commits and trees
/// are transferred; FetchBlobs then fetches individual blobs by object id. Servers that do not
/// support filtering get a full shallow fetch whose pack is kept to serve later FetchBlobs calls.
///
/// GitTreeEntry.Mode is the octal mode digits read as a decimal number, exactly as git prints
/// them: 100644 (regular file), 100755 (executable) or 120000 (symlink).
///
/// The stream passed to FetchBlobs' onBlob callback is only valid during the callback, which must
/// read it before returning. Callbacks are invoked sequentially, never concurrently.
public class GitRemoteClient(Url? githubProxy) : IGitRemoteClient, IDisposable {
  public const string UserAgent = "git/2.0 (lip)";

  private const int MaxWantsPerRequest = 256;
  private const int MaxRetainedPacks = 8;
  private const int MaxCachedTrees = 64;
  private const int MaxTransientRetries = 2;
  private const string GitProtocolHeader = "version=2";

  private readonly Url? _githubProxy = githubProxy;
  private readonly ConcurrentDictionary<string, RemoteInfo> _remotes = new();
  private readonly ConcurrentDictionary<string, byte> _proxyFailedRepos = new();
  private readonly Lock _treesLock = new();
  private readonly Dictionary<string, Lazy<Task<IReadOnlyList<GitTreeEntry>>>> _trees = [];
  private readonly Queue<string> _treeOrder = new();
  private readonly Lock _retainedPackLock = new();
  private readonly List<(string Key, GitPackFile Pack)> _retainedPacks = [];
  private long _bytesReceived;

  /// Total bytes of protocol responses received by this client.
  public long BytesReceived => Interlocked.Read(ref _bytesReceived);

  public async Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) {
    List<string> prefixes = [.. refPrefixes];

    PktLineWriter request = new PktLineWriter()
        .WriteLine("command=ls-refs")
        .WriteDelimiter()
        .WriteLine("peel");
    foreach (string prefix in prefixes) {
      request.WriteLine($"ref-prefix {prefix}");
    }
    request.WriteFlush();
    byte[] body = request.ToArray();

    List<GitRemoteRef> refs = await WithProxyFallback(repoUrl, async url => {
      RemoteInfo remote = await Discover(url);

      List<GitRemoteRef> result = [];
      await PostCommand(remote, body, async reader => {
        while (true) {
          PktLineType type = await reader.Read();
          if (type is PktLineType.Flush) {
            return;
          }
          if (type is not PktLineType.Data) {
            throw new GitProtocolException($"Unexpected {type} in ls-refs response.");
          }
          string line = reader.PayloadText;
          ThrowIfError(line);
          result.Add(ParseRef(line));
        }
      });
      return result;
    });

    // ref-prefix is only a hint to the server, so filter again.
    return prefixes.Count == 0
        ? refs
        : [.. refs.Where(r => prefixes.Any(p => r.Name.StartsWith(p, StringComparison.Ordinal)))];
  }

  public async Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) {
    string objectId = NormalizeObjectId(commitish);

    // Object ids are immutable, so a listed tree can be reused for the client's lifetime. Lazy
    // makes concurrent callers share one fetch; the oldest trees are evicted to bound memory
    // when many versions are inspected (e.g. during dependency solving).
    string key = $"{GetRepoKey(repoUrl)}@{objectId}";
    Lazy<Task<IReadOnlyList<GitTreeEntry>>> lazy;
    lock (_treesLock) {
      if (!_trees.TryGetValue(key, out lazy!)) {
        lazy = new(() => FetchTree(repoUrl, objectId));
        _trees[key] = lazy;
        _treeOrder.Enqueue(key);
        while (_treeOrder.Count > MaxCachedTrees) {
          _trees.Remove(_treeOrder.Dequeue());
        }
      }
    }

    try {
      return await lazy.Value;
    }
    catch {
      // Do not keep failures cached, so a later call can retry.
      lock (_treesLock) {
        if (_trees.TryGetValue(key, out Lazy<Task<IReadOnlyList<GitTreeEntry>>>? current) && current == lazy) {
          _trees.Remove(key);
        }
      }
      throw;
    }
  }

  public async Task FetchBlobs(
      Url repoUrl,
      IReadOnlyCollection<string> blobObjectIds,
      Func<string, Stream, Task> onBlob) {
    HashSet<string> remaining = [.. blobObjectIds.Select(NormalizeObjectId)];
    if (remaining.Count == 0) {
      return;
    }

    await FetchBlobsFromRetainedPack(GetRepoKey(repoUrl), remaining, onBlob);
    if (remaining.Count == 0) {
      return;
    }

    List<Exception> errors = [];
    foreach (string[] batch in remaining.ToList().Chunk(MaxWantsPerRequest)) {
      if (await FetchBlobBatch(repoUrl, batch, remaining, onBlob) is Exception error) {
        errors.Add(error);
      }
    }

    if (errors.Count == 1) {
      throw errors[0];
    }
    if (errors.Count > 1) {
      throw errors.All(e => e is GitProtocolException)
          ? new GitProtocolException(string.Join(" ", errors.Select(e => e.Message)))
          : new AggregateException($"Failed to fetch blobs from {repoUrl}.", errors);
    }
  }

  public void Dispose() {
    lock (_retainedPackLock) {
      foreach ((_, GitPackFile pack) in _retainedPacks) {
        pack.Dispose();
      }
      _retainedPacks.Clear();
    }
    GC.SuppressFinalize(this);
  }

  /// Fetches one batch of blobs, trying the GitHub proxy first (if any) and then the original
  /// URL. A pack is fully downloaded before any callback runs, and a later attempt only asks for
  /// the blobs still missing, so no callback is ever invoked twice for the same blob. Returns the
  /// error if some blobs could not be fetched, or null.
  private async Task<Exception?> FetchBlobBatch(
      Url repoUrl,
      string[] batch,
      HashSet<string> remaining,
      Func<string, Stream, Task> onBlob) {
    List<Exception> errors = [];

    foreach ((Url url, bool isProxy) in GetCandidateUrls(repoUrl)) {
      string[] wanted = [.. batch.Where(remaining.Contains)];

      GitPackFile pack;
      try {
        pack = await WithRetry(async () =>
            await FetchPack(await Discover(url), wanted, deepen: false, filterBlobs: false));
      }
      catch (Exception ex) {
        errors.Add(ex);
        MarkProxyFailed(repoUrl, isProxy);
        continue;
      }

      using (pack) {
        foreach (GitObject obj in pack.ReadObjects()) {
          if (!remaining.Remove(obj.ObjectId)) {
            continue;
          }
          if (obj.Type != GitObjectType.Blob) {
            throw new GitProtocolException($"Object {obj.ObjectId} is a {obj.Type}, not a blob.");
          }
          using MemoryStream stream = new(obj.Content, writable: false);
          await onBlob(obj.ObjectId, stream);
        }
      }

      string[] missing = [.. batch.Where(remaining.Contains)];
      if (missing.Length == 0) {
        return null;
      }
      errors.Add(new GitProtocolException(
          $"Remote {url} did not send {missing.Length} requested blob(s), e.g. {missing[0]}."));
      MarkProxyFailed(repoUrl, isProxy);
    }

    return errors.Count == 1
        ? errors[0]
        : new AggregateException($"Failed to fetch blobs from {repoUrl}.", errors);
  }

  private async Task<IReadOnlyList<GitTreeEntry>> FetchTree(Url repoUrl, string objectId) {
    return await WithProxyFallback(repoUrl, async url => {
      RemoteInfo remote = await Discover(url);
      GitPackFile? pack = await FetchPack(
          remote, [objectId], deepen: remote.SupportsShallow, filterBlobs: remote.SupportsFilter);
      try {
        Dictionary<string, GitObject> objects = [];
        foreach (GitObject obj in pack.ReadObjects()) {
          if (obj.Type != GitObjectType.Blob) {
            objects[obj.ObjectId] = obj;
          }
        }

        // Inside the fallback, so an incomplete pack from the proxy is retried directly.
        List<GitTreeEntry> entries = WalkTree(objects, objectId);

        if (!remote.SupportsFilter) {
          // The pack holds every blob of the tree; keep it for FetchBlobs.
          RetainPack(GetRepoKey(repoUrl), pack);
          pack = null;
        }
        return (IReadOnlyList<GitTreeEntry>)entries;
      }
      finally {
        pack?.Dispose();
      }
    });
  }

  /// Runs an operation against the GitHub proxy (if configured and applicable) and falls back to
  /// the original URL when the proxied attempt fails. Each attempt is retried on transient
  /// network errors. A repository whose proxied attempt failed goes directly to the original URL
  /// for the rest of the client's lifetime.
  private async Task<T> WithProxyFallback<T>(Url repoUrl, Func<Url, Task<T>> operation) {
    List<Exception> errors = [];

    foreach ((Url url, bool isProxy) in GetCandidateUrls(repoUrl)) {
      try {
        return await WithRetry(() => operation(url));
      }
      catch (Exception ex) {
        errors.Add(ex);
        MarkProxyFailed(repoUrl, isProxy);
      }
    }

    throw errors.Count == 1
        ? errors[0]
        : new AggregateException(
            $"Git request for {repoUrl} failed both via the GitHub proxy and directly.", errors);
  }

  /// The URLs to try for a repository: the GitHub proxy (unless not applicable or failed before),
  /// then the original URL.
  private IEnumerable<(Url Url, bool IsProxy)> GetCandidateUrls(Url repoUrl) {
    Url? proxied = RewriteUrl(repoUrl);
    if (proxied is not null && !_proxyFailedRepos.ContainsKey(GetRepoKey(repoUrl))) {
      yield return (proxied, true);
    }
    yield return (repoUrl, false);
  }

  private void MarkProxyFailed(Url repoUrl, bool isProxy) {
    if (isProxy) {
      _proxyFailedRepos.TryAdd(GetRepoKey(repoUrl), 0);
    }
  }

  private static async Task<T> WithRetry<T>(Func<Task<T>> operation) {
    for (int attempt = 0; ; attempt++) {
      try {
        return await operation();
      }
      catch (Exception ex) when (attempt < MaxTransientRetries && IsTransient(ex)) {
        await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
      }
    }
  }

  /// Network failures without a response, server errors and rate limiting are worth retrying.
  private static bool IsTransient(Exception ex) {
    return ex switch {
      FlurlHttpTimeoutException => true,
      FlurlHttpException flurl => flurl.StatusCode is null or >= 500 or 429,
      HttpRequestException http => http.StatusCode is null or >= System.Net.HttpStatusCode.InternalServerError
          or System.Net.HttpStatusCode.TooManyRequests,
      IOException => true,
      _ => false,
    };
  }

  /// Returns the proxied URL of a github.com repository, or null if no proxy applies.
  private Url? RewriteUrl(Url repoUrl) {
    if (_githubProxy is null
        || !string.Equals(repoUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    Url proxied = _githubProxy
        .Clone()
        .AppendPathSegments(repoUrl.PathSegments);
    return proxied.ToString() == repoUrl.ToString() ? null : proxied;
  }

  /// Identifies a repository for caches. GitHub resolves owner and repository names
  /// case-insensitively and the ".git" suffix is optional there, so github.com URLs are
  /// lower-cased and stripped of it; other URLs are kept as they are.
  public static string GetRepoKey(Url repoUrl) {
    string key = repoUrl.ToString().TrimEnd('/');
    if (!string.Equals(repoUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
      return key;
    }
    key = key.ToLowerInvariant();
    return key.EndsWith(".git", StringComparison.Ordinal) ? key[..^4] : key;
  }

  private async Task<RemoteInfo> Discover(Url repoUrl) {
    string key = GetRepoKey(repoUrl);
    if (_remotes.TryGetValue(key, out RemoteInfo? cached)) {
      return cached;
    }

    Url url = repoUrl.Clone()
        .AppendPathSegments("info", "refs")
        .SetQueryParam("service", "git-upload-pack");
    // Flurl drops request headers on redirect by default; without Git-Protocol the redirect
    // target answers with protocol version 0. Authorization is still never forwarded.
    using IFlurlResponse response = await url
        .WithHeader("Git-Protocol", GitProtocolHeader)
        .WithHeader("User-Agent", UserAgent)
        .WithSettings(settings => settings.Redirects.ForwardHeaders = true)
        .AllowAnyHttpStatus()
        .GetAsync(HttpCompletionOption.ResponseHeadersRead);
    EnsureSuccess(response, url);

    // Follow redirects (e.g. renamed repositories) for subsequent POSTs, like git does.
    Url endpoint = repoUrl;
    Uri? finalUri = response.ResponseMessage.RequestMessage?.RequestUri;
    if (finalUri is not null) {
      string path = finalUri.GetLeftPart(UriPartial.Path);
      if (path.EndsWith("/info/refs", StringComparison.Ordinal)) {
        endpoint = Url.Parse(path[..^"/info/refs".Length]);
      }
    }

    await using Stream body = await response.GetStreamAsync();
    PktLineReader reader = new(body);
    Dictionary<string, string?> capabilities;
    try {
      capabilities = await ReadCapabilities(reader, repoUrl);
    }
    finally {
      Interlocked.Add(ref _bytesReceived, reader.BytesRead);
    }

    if (!capabilities.ContainsKey("ls-refs") || !capabilities.TryGetValue("fetch", out string? fetch)) {
      throw new GitProtocolException($"Remote {repoUrl} does not support ls-refs and fetch.");
    }
    if (capabilities.TryGetValue("object-format", out string? format) && format != "sha1") {
      throw new GitProtocolException($"Remote {repoUrl} uses unsupported object format {format}.");
    }

    string[] fetchFeatures = fetch?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
    RemoteInfo info = new(
        endpoint,
        SupportsFilter: fetchFeatures.Contains("filter"),
        SupportsShallow: fetchFeatures.Contains("shallow"));
    _remotes[key] = info;
    return info;
  }

  private static async Task<Dictionary<string, string?>> ReadCapabilities(
      PktLineReader reader,
      Url repoUrl) {
    PktLineType type = await reader.Read();

    // Smart HTTP servers may prefix the advertisement with a service line and a flush.
    if (type == PktLineType.Data && reader.PayloadText.StartsWith("# service=", StringComparison.Ordinal)) {
      type = await reader.Read();
      if (type == PktLineType.Flush) {
        type = await reader.Read();
      }
    }

    if (type != PktLineType.Data || reader.PayloadText != "version 2") {
      throw new GitProtocolException(
          $"Remote {repoUrl} does not support git protocol version 2.");
    }

    Dictionary<string, string?> capabilities = [];
    while ((type = await reader.Read()) == PktLineType.Data) {
      string line = reader.PayloadText;
      int separator = line.IndexOf('=');
      if (separator < 0) {
        capabilities[line] = null;
      } else {
        capabilities[line[..separator]] = line[(separator + 1)..];
      }
    }
    if (type != PktLineType.Flush) {
      throw new GitProtocolException($"Malformed capability advertisement from {repoUrl}.");
    }
    return capabilities;
  }

  private async Task<GitPackFile> FetchPack(
      RemoteInfo remote,
      IEnumerable<string> wants,
      bool deepen,
      bool filterBlobs) {
    PktLineWriter request = new PktLineWriter()
        .WriteLine("command=fetch")
        .WriteDelimiter()
        .WriteLine("no-progress")
        .WriteLine("ofs-delta");
    if (deepen) {
      request.WriteLine("deepen 1");
    }
    if (filterBlobs) {
      request.WriteLine("filter blob:none");
    }
    foreach (string want in wants) {
      request.WriteLine($"want {want}");
    }
    request.WriteLine("done").WriteFlush();

    FileStream file = CreateTempFile();
    try {
      await PostCommand(remote, request.ToArray(), reader => ReadPackfile(reader, file));
      file.Position = 0;
      return GitPackFile.Open(file);
    }
    catch {
      await file.DisposeAsync();
      throw;
    }
  }

  /// Reads a fetch response up to and including the packfile section and writes the
  /// demultiplexed pack data to output.
  public static async Task ReadPackfile(PktLineReader reader, Stream output) {
    while (true) {
      PktLineType type = await reader.Read();
      if (type != PktLineType.Data) {
        throw new GitProtocolException($"Unexpected {type} in fetch response.");
      }
      string header = reader.PayloadText;
      ThrowIfError(header);
      if (header == "packfile") {
        break;
      }

      // Skip other sections (acknowledgments, shallow-info, wanted-refs, packfile-uris).
      while ((type = await reader.Read()) == PktLineType.Data) {
        ThrowIfError(reader.PayloadText);
      }
      if (type != PktLineType.Delimiter) {
        throw new GitProtocolException($"Fetch response ended without a packfile (section {header}).");
      }
    }

    while (true) {
      PktLineType type = await reader.Read();
      switch (type) {
        case PktLineType.Flush:
        case PktLineType.ResponseEnd:
          return;
        case PktLineType.Data:
          break;
        default:
          throw new GitProtocolException($"Unexpected {type} in packfile section.");
      }

      ReadOnlyMemory<byte> payload = reader.Payload;
      if (payload.Length == 0) {
        throw new GitProtocolException("Empty side-band packet.");
      }
      switch (payload.Span[0]) {
        case 1:
          await output.WriteAsync(payload[1..]);
          break;
        case 2:
          break;
        case 3:
          throw new GitProtocolException(
              $"Remote error: {System.Text.Encoding.UTF8.GetString(payload.Span[1..]).Trim()}");
        default:
          throw new GitProtocolException($"Invalid side-band channel {payload.Span[0]}.");
      }
    }
  }

  private async Task PostCommand(RemoteInfo remote, byte[] body, Func<PktLineReader, Task> handle) {
    Url url = remote.Endpoint.Clone().AppendPathSegment("git-upload-pack");
    ByteArrayContent content = new(body);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/x-git-upload-pack-request");

    using IFlurlResponse response = await url
        .WithHeader("Git-Protocol", GitProtocolHeader)
        .WithHeader("User-Agent", UserAgent)
        .WithHeader("Accept", "application/x-git-upload-pack-result")
        .WithTimeout(TimeSpan.FromMinutes(30))
        .AllowAnyHttpStatus()
        .PostAsync(content, HttpCompletionOption.ResponseHeadersRead);
    EnsureSuccess(response, url);

    await using Stream stream = await response.GetStreamAsync();
    PktLineReader reader = new(stream);
    try {
      await handle(reader);
    }
    finally {
      Interlocked.Add(ref _bytesReceived, reader.BytesRead);
    }
  }

  private async Task FetchBlobsFromRetainedPack(
      string key,
      HashSet<string> remaining,
      Func<string, Stream, Task> onBlob) {
    foreach (string objectId in remaining.ToList()) {
      GitObject? obj = null;
      lock (_retainedPackLock) {
        foreach ((string packKey, GitPackFile pack) in _retainedPacks) {
          if (packKey == key && (obj = pack.FindObject(objectId)) is not null) {
            break;
          }
        }
      }
      if (obj is null || obj.Type != GitObjectType.Blob) {
        continue;
      }
      using MemoryStream stream = new(obj.Content, writable: false);
      await onBlob(objectId, stream);
      remaining.Remove(objectId);
    }
  }

  private void RetainPack(string key, GitPackFile pack) {
    lock (_retainedPackLock) {
      if (_retainedPacks.Count >= MaxRetainedPacks) {
        _retainedPacks[0].Pack.Dispose();
        _retainedPacks.RemoveAt(0);
      }
      _retainedPacks.Add((key, pack));
    }
  }

  private static List<GitTreeEntry> WalkTree(Dictionary<string, GitObject> objects, string objectId) {
    GitObject Get(string id) {
      return objects.TryGetValue(id, out GitObject? obj)
          ? obj
          : throw new GitProtocolException($"Remote did not send object {id}.");
    }

    GitObject current = Get(objectId);
    while (current.Type == GitObjectType.Tag) {
      current = Get(GitObject.ParseTagTarget(current.Content));
    }
    if (current.Type != GitObjectType.Commit) {
      throw new GitProtocolException($"Object {objectId} does not point to a commit.");
    }

    List<GitTreeEntry> entries = [];
    void Walk(string prefix, string treeId) {
      GitObject tree = Get(treeId);
      if (tree.Type != GitObjectType.Tree) {
        throw new GitProtocolException($"Object {treeId} is not a tree.");
      }
      foreach ((int mode, string name, string entryId) in GitObject.ParseTree(tree.Content)) {
        if (!IsSafeEntryName(name)) {
          throw new GitProtocolException($"Unsafe tree entry name '{prefix}{name}'.");
        }
        string path = prefix + name;
        if (mode == 40000) {
          Walk(path + "/", entryId);
        } else if (mode != 160000) {
          entries.Add(new GitTreeEntry(path, mode, entryId));
        }
      }
    }
    Walk("", GitObject.ParseCommitTree(current.Content));
    return entries;
  }

  /// Rejects names git itself refuses to check out, so paths cannot escape the install root.
  private static bool IsSafeEntryName(string name) {
    return name is not ("" or "." or "..")
        && !name.Equals(".git", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(['/', '\\', '\0']) < 0;
  }

  private static GitRemoteRef ParseRef(string line) {
    string[] parts = line.Split(' ');
    if (parts.Length < 2 || !GitObject.IsObjectId(parts[0])) {
      throw new GitProtocolException($"Malformed ls-refs line '{line}'.");
    }
    string? peeled = parts
        .Skip(2)
        .FirstOrDefault(p => p.StartsWith("peeled:", StringComparison.Ordinal))?["peeled:".Length..];
    return new GitRemoteRef(parts[1], parts[0].ToLowerInvariant(), peeled?.ToLowerInvariant());
  }

  private static string NormalizeObjectId(string objectId) {
    return GitObject.IsObjectId(objectId)
        ? objectId.ToLowerInvariant()
        : throw new ArgumentException($"'{objectId}' is not a SHA-1 object id.", nameof(objectId));
  }

  private static void ThrowIfError(string line) {
    if (line.StartsWith("ERR ", StringComparison.Ordinal)) {
      throw new GitProtocolException($"Remote error: {line[4..]}");
    }
  }

  private static void EnsureSuccess(IFlurlResponse response, Url url) {
    if (response.StatusCode is < 200 or >= 300) {
      throw new HttpRequestException(
          $"Git request to {url} failed: HTTP {response.StatusCode}",
          inner: null,
          (System.Net.HttpStatusCode)response.StatusCode);
    }
  }

  private static FileStream CreateTempFile() {
    return new FileStream(
        Path.Combine(Path.GetTempPath(), $"lip-{Guid.NewGuid():N}.pack"),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        bufferSize: 81920,
        FileOptions.DeleteOnClose);
  }

  private record RemoteInfo(Url Endpoint, bool SupportsFilter, bool SupportsShallow);
}
