using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;

namespace Lip.Core.Sources;

/// A source backed by the tree of a remote git commit. The tree is listed lazily (once per
/// instance) without downloading any blob, and only the blobs of the keys read are downloaded,
/// never through the cache: OpenRead fetches its blob on every call unless Prefetch has
/// downloaded it into this instance's memory. Blob content is verified against its object id.
///
/// Keys are '/'-separated paths relative to the root tree. Only regular files (modes
/// 100644/100755) are exposed, as in a git archive (protocol/package.md §1).
public class GitTreeSource(IGitRemoteClient gitRemoteClient, Url repoUrl, string commitish) : ISource {
  private readonly IGitRemoteClient _gitRemoteClient = gitRemoteClient;
  private readonly Url _repoUrl = repoUrl;
  private readonly string _commitish = commitish;

  private readonly Lock _entriesLock = new();
  private Task<IReadOnlyDictionary<string, string>>? _entries;

  // Blobs downloaded by Prefetch, by object id.
  private readonly ConcurrentDictionary<string, byte[]> _prefetched = new();

  // Blocks on the first access unless the tree was listed already, e.g. via Create().
  public IEnumerable<string> Keys => Task.Run(GetEntries).GetAwaiter().GetResult().Keys;

  /// Creates a source and lists its tree up front, so later Keys accesses do not block.
  public static async Task<GitTreeSource> Create(
      IGitRemoteClient gitRemoteClient,
      Url repoUrl,
      string commitish) {
    GitTreeSource source = new(gitRemoteClient, repoUrl, commitish);

    await source.GetEntries();

    return source;
  }

  public async Task<Stream> OpenRead(string key) {
    string objectId = await GetObjectId(key);

    if (_prefetched.TryGetValue(objectId, out byte[]? prefetched)) {
      return new MemoryStream(prefetched, writable: false);
    }

    Dictionary<string, byte[]> blobs = await FetchBlobs([objectId]);
    return new MemoryStream(blobs[objectId], writable: false);
  }

  /// Downloads all blobs of the given keys that are not downloaded yet with a single request.
  public async Task Prefetch(IEnumerable<string> keys) {
    IReadOnlyDictionary<string, string> entries = await GetEntries();

    HashSet<string> objectIds = [.. keys
        .Select(k => entries.TryGetValue(k, out string? oid)
            ? oid
            : throw new ArgumentException($"Key not found: {k}", nameof(keys)))
        .Where(oid => !_prefetched.ContainsKey(oid))];

    if (objectIds.Count == 0) {
      return;
    }

    foreach ((string objectId, byte[] content) in await FetchBlobs(objectIds)) {
      _prefetched.TryAdd(objectId, content);
    }
  }

  // Fetches the given blobs with a single request and verifies each against its object id.
  private async Task<Dictionary<string, byte[]>> FetchBlobs(IReadOnlyCollection<string> objectIds) {
    Dictionary<string, byte[]> blobs = [];

    await _gitRemoteClient.FetchBlobs(_repoUrl, objectIds, async (oid, stream) => {
      if (!objectIds.Contains(oid) || blobs.ContainsKey(oid)) {
        await stream.CopyToAsync(Stream.Null);
        return;
      }

      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      byte[] content = memory.ToArray();
      VerifyBlob(oid, content);
      blobs[oid] = content;
    });

    string[] missing = [.. objectIds.Where(oid => !blobs.ContainsKey(oid))];
    if (missing.Length > 0) {
      throw new InvalidOperationException(
          $"Blobs {string.Join(", ", missing)} were not returned by {_repoUrl}.");
    }

    return blobs;
  }

  private async Task<string> GetObjectId(string key) {
    IReadOnlyDictionary<string, string> entries = await GetEntries();

    return entries.TryGetValue(key, out string? objectId)
        ? objectId
        : throw new ArgumentException($"Key not found: {key}", nameof(key));
  }

  // ListTree is called at most once per instance unless it fails, in which case the next access retries.
  private Task<IReadOnlyDictionary<string, string>> GetEntries() {
    lock (_entriesLock) {
      if (_entries is null || _entries.IsFaulted || _entries.IsCanceled) {
        _entries = ListEntries();
      }

      return _entries;
    }
  }

  private async Task<IReadOnlyDictionary<string, string>> ListEntries() {
    IReadOnlyList<GitTreeEntry> tree = await _gitRemoteClient.ListTree(_repoUrl, _commitish);

    return tree.Where(e => e.IsRegularFile).ToDictionary(e => e.Path, e => e.ObjectId);
  }

  // Checks that the git object hash of the blob ("blob <size>\0" + content) matches its SHA-1
  // object id.
  private static void VerifyBlob(string objectId, byte[] content) {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    hash.AppendData(Encoding.ASCII.GetBytes($"blob {content.Length}\0"));
    hash.AppendData(content);

    string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
    if (!string.Equals(actual, objectId, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidDataException($"Blob content hash {actual} does not match object id {objectId}.");
    }
  }
}
