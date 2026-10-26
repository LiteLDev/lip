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
  private const string GitProtocolHeader = "version=2";

  private readonly Url? _githubProxy = githubProxy;
  private readonly ConcurrentDictionary<string, RemoteInfo> _remotes = new();
  private readonly Lock _retainedPackLock = new();
  private readonly List<(string Key, GitPackFile Pack)> _retainedPacks = [];
  private long _bytesReceived;

  /// Total bytes of protocol responses received by this client.
  public long BytesReceived => Interlocked.Read(ref _bytesReceived);

  public async Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) {
    List<string> prefixes = [.. refPrefixes];
    RemoteInfo remote = await Discover(RewriteUrl(repoUrl));

    PktLineWriter request = new PktLineWriter()
        .WriteLine("command=ls-refs")
        .WriteDelimiter()
        .WriteLine("peel");
    foreach (string prefix in prefixes) {
      request.WriteLine($"ref-prefix {prefix}");
    }
    request.WriteFlush();

    List<GitRemoteRef> refs = [];
    await PostCommand(remote, request.ToArray(), async reader => {
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
        refs.Add(ParseRef(line));
      }
    });

    // ref-prefix is only a hint to the server, so filter again.
    return prefixes.Count == 0
        ? refs
        : [.. refs.Where(r => prefixes.Any(p => r.Name.StartsWith(p, StringComparison.Ordinal)))];
  }

  public async Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) {
    string objectId = NormalizeObjectId(commitish);
    Url url = RewriteUrl(repoUrl);
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

      List<GitTreeEntry> entries = WalkTree(objects, objectId);

      if (!remote.SupportsFilter) {
        // The pack holds every blob of the tree; keep it for FetchBlobs.
        RetainPack(url.ToString(), pack);
        pack = null;
      }
      return entries;
    }
    finally {
      pack?.Dispose();
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

    Url url = RewriteUrl(repoUrl);
    await FetchBlobsFromRetainedPack(url.ToString(), remaining, onBlob);
    if (remaining.Count == 0) {
      return;
    }

    RemoteInfo remote = await Discover(url);
    foreach (string[] batch in remaining.ToList().Chunk(MaxWantsPerRequest)) {
      using GitPackFile pack = await FetchPack(remote, batch, deepen: false, filterBlobs: false);
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

    if (remaining.Count > 0) {
      throw new GitProtocolException(
          $"Remote {url} did not send {remaining.Count} requested blob(s), e.g. {remaining.First()}.");
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

  private Url RewriteUrl(Url repoUrl) {
    if (_githubProxy is not null
        && string.Equals(repoUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
      return _githubProxy
          .Clone()
          .AppendPathSegments(repoUrl.PathSegments);
    }
    return repoUrl;
  }

  private async Task<RemoteInfo> Discover(Url repoUrl) {
    string key = repoUrl.ToString();
    if (_remotes.TryGetValue(key, out RemoteInfo? cached)) {
      return cached;
    }

    Url url = repoUrl.Clone()
        .AppendPathSegments("info", "refs")
        .SetQueryParam("service", "git-upload-pack");
    using IFlurlResponse response = await url
        .WithHeader("Git-Protocol", GitProtocolHeader)
        .WithHeader("User-Agent", UserAgent)
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
      throw new HttpRequestException($"Git request to {url} failed: HTTP {response.StatusCode}");
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
