using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Flurl;
using Flurl.Http;
using Lip.Core.Infrastructure;

namespace Lip.Core.Git;

/// A managed client for the git smart-HTTP protocol version 2 (read-only, upload-pack).
///
/// ListTree uses a shallow partial fetch (deepen 1, filter blob:none) so only commits and trees
/// are transferred; FetchBlobs then fetches individual blobs by object id. A remote that does not
/// support both is not used, so a repository is never cloned in full.
///
/// GitTreeEntry.Mode is the octal mode digits read as a decimal number, exactly as git prints
/// them: 100644 (regular file), 100755 (executable) or 120000 (symlink).
///
/// The stream passed to FetchBlobs' onBlob callback is only valid during the callback, which must
/// read it before returning. Callbacks are invoked sequentially, never concurrently.
///
/// Every failure names what the client was trying to do and with which server; the technical
/// detail (status line, malformed data, the server's own message) is kept as inner exception.
public class GitRemoteClient(Url? githubProxy) : IGitRemoteClient {
  public const string UserAgent = "git/2.0 (lip)";

  private const int MaxWantsPerRequest = 256;
  private const int MaxCachedTrees = 64;
  private const int MaxTracedRefs = 50;
  private const int MaxErrorBodyChars = 2000;
  private const string GitProtocolHeader = "version=2";

  private readonly Url? _githubProxy = githubProxy;
  private readonly ConcurrentDictionary<string, RemoteInfo> _remotes = new();
  private readonly Lock _treesLock = new();
  private readonly Dictionary<string, Lazy<Task<IReadOnlyList<GitTreeEntry>>>> _trees = [];
  private readonly Queue<string> _treeOrder = new();

  public async Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) {
    List<string> prefixes = [.. refPrefixes];
    RunTrace.Write("git", prefixes.Count == 0
        ? $"List all refs of {repoUrl}"
        : $"List the refs of {repoUrl} starting with {string.Join(", ", prefixes)}");

    PktLineWriter request = new PktLineWriter()
        .WriteLine("command=ls-refs")
        .WriteDelimiter()
        .WriteLine("peel");
    foreach (string prefix in prefixes) {
      request.WriteLine($"ref-prefix {prefix}");
    }
    request.WriteFlush();
    byte[] body = request.ToArray();

    List<GitRemoteRef> refs = await WithProxyFallback(
        repoUrl,
        $"list the tags and branches of {repoUrl}",
        notFound: null,
        async url => {
          RemoteInfo remote = await Discover(url);

          List<GitRemoteRef> result = [];
          await PostCommand(remote, body, async reader => {
            while (true) {
              PktLineType type = await reader.Read();
              if (type is PktLineType.Flush) {
                return;
              }
              if (type is not PktLineType.Data) {
                throw new GitProtocolException(
                    $"Unexpected {type} packet in the ls-refs response; expected a ref line or a flush.");
              }
              string line = reader.PayloadText;
              ThrowIfError(line);
              result.Add(ParseRef(line));
            }
          });
          return result;
        });

    // ref-prefix is only a hint to the server, so filter again.
    IReadOnlyList<GitRemoteRef> matching = prefixes.Count == 0
        ? refs
        : [.. refs.Where(r => prefixes.Any(p => r.Name.StartsWith(p, StringComparison.Ordinal)))];
    RunTrace.Write("git", $"{repoUrl} has {matching.Count} matching refs"
        + (matching.Count == 0 ? "" : ": " + string.Join(", ", matching.Take(MaxTracedRefs).Select(r => r.Name)))
        + (matching.Count > MaxTracedRefs ? ", ..." : ""));
    return matching;
  }

  public async Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) {
    string objectId = NormalizeObjectId(commitish);

    // Object ids are immutable, so a listed tree can be reused for the client's lifetime. Lazy
    // makes concurrent callers share one fetch; the oldest trees are evicted to bound memory
    // when many versions are inspected (e.g. during dependency solving).
    string key = $"{GetRepoKey(repoUrl)}@{objectId}";
    Lazy<Task<IReadOnlyList<GitTreeEntry>>> lazy;
    lock (_treesLock) {
      if (_trees.TryGetValue(key, out lazy!)) {
        RunTrace.Write("git", $"Reuse the file list of commit {objectId} in {repoUrl} fetched earlier");
      } else {
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
    int requested = remaining.Count;
    RunTrace.Write("git", $"Download {requested} blobs from {repoUrl}");

    List<Exception> errors = [];
    foreach (string[] batch in remaining.ToList().Chunk(MaxWantsPerRequest)) {
      if (await FetchBlobBatch(repoUrl, batch, remaining, onBlob) is Exception error) {
        errors.Add(error);
      }
    }

    if (errors.Count == 1) {
      ExceptionDispatchInfo.Throw(errors[0]);
    }
    if (errors.Count > 1) {
      string message = $"Could not download {remaining.Count} of the {requested} requested files"
          + $" from {repoUrl}: {errors.Count} of the download requests failed.";
      throw errors.All(e => e is GitProtocolException)
          ? new GitProtocolException(message, new AggregateException(
              $"The errors of the {errors.Count} failed download requests.", errors))
          : new AggregateException(message, errors);
    }
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
    string purpose = $"download {batch.Length} files from {repoUrl}";
    string notFound = $"The git repository {repoUrl} does not have some of the files lip asked"
        + " for; the commit may have been removed from it (for example by a force push). Try"
        + " again, and if this keeps happening, pick another version of the package.";
    List<Url> candidates = [.. GetCandidateUrls(repoUrl)];
    List<Exception> errors = [];

    foreach (Url url in candidates) {
      string[] wanted = [.. batch.Where(remaining.Contains)];
      RunTrace.Write("git", $"Fetch {wanted.Length} blobs of {repoUrl} via {url}");

      GitPackFile pack;
      try {
        pack = await FetchPack(await Discover(url), wanted, deepen: false, filterBlobs: false);
      }
      catch (Exception ex) {
        errors.Add(Explain(ex, url, repoUrl, purpose, notFound));
        continue;
      }

      using (pack) {
        try {
          foreach (GitObject obj in pack.ReadObjects()) {
            if (!remaining.Remove(obj.ObjectId)) {
              continue;
            }
            if (obj.Type != GitObjectType.Blob) {
              throw new UnusableRepositoryException(
                  $"the object {obj.ObjectId} was requested as a file (blob) but is a {GitObject.GetTypeName(obj.Type)}");
            }
            using MemoryStream stream = new(obj.Content, writable: false);
            await onBlob(obj.ObjectId, stream);
          }
        }
        catch (GitProtocolException ex) {
          // Callbacks may already have run, so this pack is not retried elsewhere.
          throw Explain(ex, url, repoUrl, purpose, notFound);
        }
      }

      string[] missing = [.. batch.Where(remaining.Contains)];
      if (missing.Length == 0) {
        return null;
      }
      errors.Add(Explain(
          new GitProtocolException($"The server did not send {missing.Length} of the"
              + $" {wanted.Length} requested blobs, for example {missing[0]}."),
          url, repoUrl, purpose, notFound));
    }

    return errors.Count == 1
        ? errors[0]
        : new AggregateException(FallbackSummary(purpose, candidates[0]), errors);
  }

  private async Task<IReadOnlyList<GitTreeEntry>> FetchTree(Url repoUrl, string objectId) {
    RunTrace.Write("git", $"Fetch commit {objectId} of {repoUrl} to list its files");
    string notFound = $"The git repository {repoUrl} has no commit {objectId}. The commit may"
        + " have been removed from the repository (for example by a force push), or the commit"
        + " id is wrong; check it, or pick another version of the package.";

    IReadOnlyList<GitTreeEntry> entries = await WithProxyFallback(
        repoUrl,
        $"list the files of commit {objectId} in {repoUrl}",
        notFound,
        async url => {
          RemoteInfo remote = await Discover(url);
          if (!remote.SupportsShallow || !remote.SupportsFilter) {
            throw new UnsupportedServerException(
                $"The server at {url} does not support shallow partial fetches (the 'shallow'"
                + " and 'filter' features of the fetch command).");
          }

          using GitPackFile pack = await FetchPack(remote, [objectId], deepen: true, filterBlobs: true);
          Dictionary<string, GitObject> objects = [];
          foreach (GitObject obj in pack.ReadObjects()) {
            if (obj.Type != GitObjectType.Blob) {
              objects[obj.ObjectId] = obj;
            }
          }

          // Inside the fallback, so an incomplete pack from the proxy is retried directly.
          return (IReadOnlyList<GitTreeEntry>)WalkTree(objects, objectId);
        });

    RunTrace.Write("git", $"Commit {objectId} of {repoUrl} has {entries.Count} files");
    return entries;
  }

  /// Runs an operation against the GitHub proxy (if configured and applicable) and falls back to
  /// the original URL when the proxied attempt fails. purpose says what the operation does, as
  /// in "Could not {purpose}"; notFound, if any, explains a server error about missing objects.
  private async Task<T> WithProxyFallback<T>(
      Url repoUrl,
      string purpose,
      string? notFound,
      Func<Url, Task<T>> operation) {
    List<Url> candidates = [.. GetCandidateUrls(repoUrl)];
    List<Exception> errors = [];

    foreach (Url url in candidates) {
      RunTrace.Write("git", errors.Count == 0
          ? $"Try to {purpose} via {url}"
          : $"Fall back to {url} to {purpose}");
      try {
        return await operation(url);
      }
      catch (Exception ex) {
        errors.Add(Explain(ex, url, repoUrl, purpose, notFound));
      }
    }

    if (errors.Count == 1) {
      ExceptionDispatchInfo.Throw(errors[0]);
    }
    throw new AggregateException(FallbackSummary(purpose, candidates[0]), errors);
  }

  private static string FallbackSummary(string purpose, Url proxied) =>
      $"Could not {purpose}: lip tried the GitHub proxy {proxied.Host} and then GitHub directly,"
      + " and both failed.";

  /// Turns a failure of one attempt against url into an exception that says, in plain words,
  /// what went wrong, keeping the failure as inner exception. Exceptions that are not about the
  /// server (cancellation, local file errors) are returned as they are.
  private static Exception Explain(Exception ex, Url url, Url repoUrl, string purpose, string? notFound) {
    RunTrace.Write("git", $"Could not {purpose} via {url}: {ex.GetType().Name}: {ex.Message}");

    bool viaProxy = url.ToString() != repoUrl.ToString();
    string server = viaProxy
        ? $"the GitHub proxy {url.Host}"
        : IsGithub(url) ? "GitHub" : $"the git server {url.Host}";
    string subject = char.ToUpperInvariant(server[0]) + server[1..];
    string proxyAdvice = viaProxy
        ? " If the GitHub proxy does not support git repositories, remove it with"
            + " 'lip config delete github_proxy'."
        : "";

    // An invalid github_proxy value is accepted by the configuration and fails only here.
    if (viaProxy && HasCause<UriFormatException>(ex)) {
      return new UriFormatException(
          $"The GitHub proxy set in github_proxy does not give a valid URL ({url}). Set a valid"
          + " URL with 'lip config set github_proxy <URL>', or remove it with"
          + " 'lip config delete github_proxy'.",
          ex);
    }

    return ex switch {
      UnusableRepositoryException unusable =>
          new GitProtocolException(
              $"The git repository {repoUrl} contains data lip cannot handle: {unusable.Message}."
              + " Pick another version of the package, or report the problem to the package author.",
              ex),
      UnsupportedObjectFormatException format =>
          new GitProtocolException(
              $"The git repository {repoUrl} uses {format.Format} object ids, which lip does not"
              + " support yet. Pick another version of the package, or ask the package author to"
              + " publish it from a repository that uses SHA-1 object ids.",
              ex),
      GitServerErrorException error
          when notFound is not null && error.ServerMessage.Contains("not our ref", StringComparison.Ordinal) =>
          new GitProtocolException(notFound, ex),
      GitServerErrorException =>
          new GitProtocolException(
              $"{subject} reported an error when lip tried to {purpose}. Try again later.{proxyAdvice}", ex),
      UnsupportedServerException =>
          new GitProtocolException(
              $"{subject} cannot be used to {purpose}: lip needs a git server that supports version 2"
              + $" of the git protocol with shallow and partial fetches, as GitHub does.{proxyAdvice}",
              ex),
      GitProtocolException =>
          new GitProtocolException(
              $"{subject} sent an unexpected response when lip tried to {purpose}. Try again later;"
              + $" if this keeps happening, it may not be a git server.{proxyAdvice}",
              ex),
      HttpRequestException { StatusCode: HttpStatusCode status } =>
          new HttpRequestException(
              DescribeStatus(server, subject, purpose, status, IsGithub(repoUrl)) + proxyAdvice, ex, status),
      FlurlHttpTimeoutException =>
          new HttpRequestException(
              $"{subject} did not answer in time when lip tried to {purpose}. Check your network"
              + $" connection and try again.{proxyAdvice}",
              ex),
      FlurlHttpException =>
          new HttpRequestException(
              $"Could not connect to {server} to {purpose}. Check your network connection and"
              + $" try again.{proxyAdvice}",
              ex),
      HttpIOException or IOException { InnerException: SocketException } =>
          new HttpRequestException(
              $"The connection to {server} broke off while lip tried to {purpose}. Check your network"
              + $" connection and try again.{proxyAdvice}",
              ex),
      TaskCanceledException { InnerException: TimeoutException } =>
          new HttpRequestException(
              $"{subject} did not answer in time when lip tried to {purpose}. Check your network"
              + $" connection and try again.{proxyAdvice}",
              ex),
      _ => ex,
    };
  }

  private static string DescribeStatus(
      string server,
      string subject,
      string purpose,
      HttpStatusCode status,
      bool isGithub) {
    string code = $"HTTP {(int)status}";
    return status switch {
      HttpStatusCode.Unauthorized =>
          $"{subject} refused to let lip {purpose} ({code}). "
          + (isGithub
              ? "GitHub answers this way when a repository does not exist or is private"
              : "The repository may not exist or may be private")
          + ": check the spelling of the package, and that its repository is public.",
      HttpStatusCode.Forbidden =>
          $"{subject} denied access when lip tried to {purpose} ({code}). The repository may be"
          + $" private, or {server} may be limiting how many requests lip can send; wait a while"
          + " and try again.",
      HttpStatusCode.NotFound =>
          $"{subject} found no such repository when lip tried to {purpose} ({code}). Check the"
          + " spelling of the package, and that its repository is public.",
      HttpStatusCode.TooManyRequests =>
          $"{subject} refused to let lip {purpose} because lip sent too many requests ({code})."
          + " Wait a while and try again.",
      >= HttpStatusCode.InternalServerError =>
          $"{subject} had a problem when lip tried to {purpose} ({code}). Try again later.",
      _ => $"{subject} answered {code}"
          + (Enum.IsDefined(status) ? $" ({status})" : "")
          + $" when lip tried to {purpose}. Try again later; if this keeps happening, check that"
          + " the URL points to a git repository.",
    };
  }

  private static bool IsGithub(Url url) =>
      string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase);

  private static bool HasCause<T>(Exception ex) where T : Exception {
    for (Exception? current = ex; current is not null; current = current.InnerException) {
      if (current is T) {
        return true;
      }
    }
    return false;
  }

  /// The URLs to try for a repository: the GitHub proxy (if applicable), then the original URL.
  private IEnumerable<Url> GetCandidateUrls(Url repoUrl) {
    Url? proxied = GithubProxy.Rewrite(_githubProxy, repoUrl);
    if (proxied is not null) {
      yield return proxied;
    }
    yield return repoUrl;
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
    await EnsureSuccess(response, "GET", url);

    // Follow redirects (e.g. renamed repositories) for subsequent POSTs, like git does.
    Url endpoint = repoUrl;
    Uri? finalUri = response.ResponseMessage.RequestMessage?.RequestUri;
    if (finalUri is not null) {
      string path = finalUri.GetLeftPart(UriPartial.Path);
      if (path.EndsWith("/info/refs", StringComparison.Ordinal)) {
        endpoint = Url.Parse(path[..^"/info/refs".Length]);
      }
    }
    if (endpoint.ToString() != repoUrl.ToString()) {
      RunTrace.Write("git", $"{repoUrl} redirects to {endpoint}");
    }

    await using Stream body = await response.GetStreamAsync();
    Dictionary<string, string?> capabilities = await ReadCapabilities(new PktLineReader(body), repoUrl);
    RunTrace.Write("git", $"{repoUrl} offers: " + string.Join(", ", capabilities.Select(
        c => c.Value is null ? c.Key : $"{c.Key}={c.Value}")));

    if (!capabilities.ContainsKey("ls-refs") || !capabilities.TryGetValue("fetch", out string? fetch)) {
      throw new UnsupportedServerException(
          $"The server at {repoUrl} does not offer the ls-refs and fetch commands of git protocol"
          + " version 2.");
    }
    if (capabilities.TryGetValue("object-format", out string? format) && format != "sha1") {
      throw new UnsupportedObjectFormatException(format ?? "(unnamed)");
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
      throw new UnsupportedServerException(
          $"The server at {repoUrl} does not support version 2 of the git protocol.");
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
      throw new GitProtocolException(
          $"Malformed capability advertisement from {repoUrl}: expected a flush packet after the"
          + $" capabilities, got {type}.");
    }
    return capabilities;
  }

  private static async Task<GitPackFile> FetchPack(
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
      GitPackFile pack = GitPackFile.Open(file);
      RunTrace.Write("git", $"Received a pack of {file.Length} bytes with {pack.Count} objects"
          + $" from {remote.Endpoint}");
      return pack;
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
        throw new GitProtocolException(
            $"Unexpected {type} packet in the fetch response; expected a section header.");
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
        throw new GitProtocolException(
            $"The fetch response ended after section '{header}' without a packfile section.");
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
          throw new GitProtocolException($"Unexpected {type} packet in the packfile section.");
      }

      ReadOnlyMemory<byte> payload = reader.Payload;
      if (payload.Length == 0) {
        throw new GitProtocolException("Empty side-band packet in the packfile section.");
      }
      switch (payload.Span[0]) {
        case 1:
          await output.WriteAsync(payload[1..]);
          break;
        case 2:
          break;
        case 3:
          throw new GitServerErrorException(
              System.Text.Encoding.UTF8.GetString(payload.Span[1..]).Trim());
        default:
          throw new GitProtocolException(
              $"Invalid side-band channel {payload.Span[0]} in the packfile section.");
      }
    }
  }

  private static async Task PostCommand(RemoteInfo remote, byte[] body, Func<PktLineReader, Task> handle) {
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
    await EnsureSuccess(response, "POST", url);

    await using Stream stream = await response.GetStreamAsync();
    await handle(new PktLineReader(stream));
  }

  private static List<GitTreeEntry> WalkTree(Dictionary<string, GitObject> objects, string objectId) {
    GitObject Get(string id) {
      return objects.TryGetValue(id, out GitObject? obj)
          ? obj
          : throw new GitProtocolException($"The server did not send object {id}.");
    }

    GitObject current = Get(objectId);
    while (current.Type == GitObjectType.Tag) {
      current = Get(GitObject.ParseTagTarget(current.Content));
    }
    if (current.Type != GitObjectType.Commit) {
      throw new UnusableRepositoryException(
          $"the object {objectId} is a {GitObject.GetTypeName(current.Type)}, not a commit or a tag of one");
    }

    List<GitTreeEntry> entries = [];
    void Walk(string prefix, string treeId) {
      GitObject tree = Get(treeId);
      if (tree.Type != GitObjectType.Tree) {
        throw new GitProtocolException(
            $"Object {treeId} at '{prefix}' is a {GitObject.GetTypeName(tree.Type)}, not a tree.");
      }
      foreach ((int mode, string name, string entryId) in GitObject.ParseTree(tree.Content)) {
        if (!IsSafeEntryName(name)) {
          throw new UnusableRepositoryException(
              $"it has an entry named '{prefix}{name}' (in tree {treeId}), which is not a valid file name");
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

  /// Rejects names a git archive cannot hold (protocol/package.md §1).
  private static bool IsSafeEntryName(string name) {
    return name is not ("" or "." or "..") && !name.Contains('/') && !name.Contains('\\');
  }

  private static GitRemoteRef ParseRef(string line) {
    string[] parts = line.Split(' ');
    if (parts.Length < 2 || !GitObject.IsObjectId(parts[0])) {
      throw new GitProtocolException(
          $"Malformed ls-refs line '{line}'; expected an object id followed by a ref name.");
    }
    string? peeled = parts
        .Skip(2)
        .FirstOrDefault(p => p.StartsWith("peeled:", StringComparison.Ordinal))?["peeled:".Length..];
    return new GitRemoteRef(parts[1], parts[0].ToLowerInvariant(), peeled?.ToLowerInvariant());
  }

  private static string NormalizeObjectId(string objectId) {
    return GitObject.IsObjectId(objectId)
        ? objectId.ToLowerInvariant()
        : throw new ArgumentException(
            $"'{objectId}' is not a git object id; an object id is 40 hexadecimal digits.");
  }

  private static void ThrowIfError(string line) {
    if (line.StartsWith("ERR ", StringComparison.Ordinal)) {
      throw new GitServerErrorException(line[4..]);
    }
  }

  /// Throws an HttpRequestException carrying the status code if the response is not a success.
  /// The start of the response body, which often says why, is kept in the exception's Data so
  /// that it reaches the failure report.
  private static async Task EnsureSuccess(IFlurlResponse response, string verb, Url url) {
    if (response.StatusCode is >= 200 and < 300) {
      return;
    }

    string? reason = response.ResponseMessage.ReasonPhrase;
    HttpRequestException exception = new(
        $"The server answered HTTP {response.StatusCode}"
            + (string.IsNullOrEmpty(reason) ? "" : $" ({reason})")
            + $" to {verb} {url}.",
        inner: null,
        (HttpStatusCode)response.StatusCode);
    try {
      // Only the start of the body is read, so a large or slow error page does not delay the error.
      await using Stream stream = await response.GetStreamAsync();
      using StreamReader reader = new(stream);
      char[] buffer = new char[MaxErrorBodyChars + 1];
      int length = await reader.ReadBlockAsync(buffer);
      exception.Data["Response body"] = length > MaxErrorBodyChars
          ? new string(buffer, 0, MaxErrorBodyChars) + "..."
          : new string(buffer, 0, length);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      exception.Data["Response body"] = $"(cannot read: {ex.Message})";
    }
    throw exception;
  }

  // A <N>.tmp file in the cache directory (protocol/userspace.md §3), since lip keeps nothing in
  // the system temp directory.
  private static FileStream CreateTempFile() {
    Directory.CreateDirectory(LipDirectories.Cache);
    return new FileStream(
        Path.Join(LipDirectories.Cache, $"{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        bufferSize: 81920,
        FileOptions.DeleteOnClose);
  }

  private record RemoteInfo(Url Endpoint, bool SupportsFilter, bool SupportsShallow);

  /// The server reported an error itself (an ERR line or side-band channel 3).
  private sealed class GitServerErrorException(string serverMessage)
      : GitProtocolException($"The git server reported: {serverMessage}") {
    public string ServerMessage { get; } = serverMessage;
  }

  /// The server works but lacks a feature lip needs.
  private sealed class UnsupportedServerException(string message) : GitProtocolException(message);

  /// The repository uses object ids other than SHA-1.
  private sealed class UnsupportedObjectFormatException(string format)
      : GitProtocolException($"The repository uses the {format} object format; lip supports only sha1.") {
    public string Format { get; } = format;
  }
}
