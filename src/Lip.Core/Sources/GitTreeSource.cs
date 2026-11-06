using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Services;

namespace Lip.Core.Sources;

/// A source backed by the tree of a remote git commit. The tree is listed lazily (once per
/// instance) without downloading any blob, and blobs are downloaded on demand into the
/// content-addressed cache under the key "git-{oid}" (CacheKeys.GitBlob). Blob content is verified against its
/// object id before it is cached, so the repository-independent cache key is safe to share.
/// ReadUncached fetches a blob into memory on every call, bypassing that cache.
///
/// Keys are '/'-separated paths relative to the root tree. Only regular files (modes
/// 100644/100755) are exposed, as in a git archive (protocol/package.md §1).
public class GitTreeSource(
    IGitRemoteClient gitRemoteClient,
    ICacheService cacheService,
    Url repoUrl,
    string commitish) : ISource {
  private readonly IGitRemoteClient _gitRemoteClient = gitRemoteClient;
  private readonly ICacheService _cacheService = cacheService;
  private readonly Url _repoUrl = repoUrl;
  private readonly string _commitish = commitish;

  private readonly Lock _entriesLock = new();
  private Task<IReadOnlyDictionary<string, string>>? _entries;

  // Object ids known to be present in the cache, so Prefetch need not probe them again.
  private readonly ConcurrentDictionary<string, byte> _cachedObjectIds = new();

  // Blocks on the first access unless the tree was listed already, e.g. via Create().
  public IEnumerable<string> Keys => Task.Run(GetEntries).GetAwaiter().GetResult().Keys;

  /// Creates a source and lists its tree up front, so later Keys accesses do not block.
  public static async Task<GitTreeSource> Create(
      IGitRemoteClient gitRemoteClient,
      ICacheService cacheService,
      Url repoUrl,
      string commitish) {
    GitTreeSource source = new(gitRemoteClient, cacheService, repoUrl, commitish);

    await source.GetEntries();

    return source;
  }

  public async Task<Stream> OpenRead(string key) {
    string objectId = await GetObjectId(key);

    IFileInfo file = await _cacheService.GetOrCreateFile(GetCacheKey(objectId), async file => {
      bool received = false;

      await _gitRemoteClient.FetchBlobs(_repoUrl, [objectId], async (oid, stream) => {
        if (oid != objectId || received) {
          await stream.CopyToAsync(Stream.Null);
          return;
        }

        await WriteVerifiedBlob(file, oid, stream);
        received = true;
      });

      if (!received) {
        throw new InvalidOperationException($"Blob {objectId} was not returned by {_repoUrl}.");
      }
    });

    _cachedObjectIds.TryAdd(objectId, 0);

    return file.OpenRead();
  }

  public async Task<byte[]> ReadUncached(string key) {
    string objectId = await GetObjectId(key);

    byte[]? content = null;
    await _gitRemoteClient.FetchBlobs(_repoUrl, [objectId], async (oid, stream) => {
      if (oid != objectId || content is not null) {
        await stream.CopyToAsync(Stream.Null);
        return;
      }

      using MemoryStream memory = new();
      await stream.CopyToAsync(memory);
      byte[] bytes = memory.ToArray();
      VerifyBlob(oid, bytes);
      content = bytes;
    });

    return content ?? throw new InvalidOperationException(
        $"Blob {objectId} was not returned by {_repoUrl}.");
  }

  /// Downloads all blobs of the given keys that are not cached yet with a single request.
  public async Task Prefetch(IEnumerable<string> keys) {
    IReadOnlyDictionary<string, string> entries = await GetEntries();

    HashSet<string> objectIds = [.. keys
        .Select(k => entries.TryGetValue(k, out string? oid)
            ? oid
            : throw new ArgumentException($"Key not found: {k}", nameof(keys)))
        .Where(oid => !_cachedObjectIds.ContainsKey(oid))];

    ConcurrentDictionary<string, byte> pending = new();
    foreach (string objectId in objectIds) {
      if (await IsCached(objectId)) {
        _cachedObjectIds.TryAdd(objectId, 0);
      } else {
        pending.TryAdd(objectId, 0);
      }
    }

    if (pending.IsEmpty) {
      return;
    }

    await _gitRemoteClient.FetchBlobs(_repoUrl, [.. pending.Keys], async (oid, stream) => {
      if (!pending.ContainsKey(oid)) {
        await stream.CopyToAsync(Stream.Null);
        return;
      }

      // The factory is skipped if another process cached the blob meanwhile, so drain the stream
      // in that case to keep the client's packfile reader in sync.
      bool written = false;
      await _cacheService.GetOrCreateFile(GetCacheKey(oid), async file => {
        await WriteVerifiedBlob(file, oid, stream);
        written = true;
      });
      if (!written) {
        await stream.CopyToAsync(Stream.Null);
      }

      _cachedObjectIds.TryAdd(oid, 0);
      pending.TryRemove(oid, out _);
    });

    if (!pending.IsEmpty) {
      throw new InvalidOperationException(
          $"Blobs {string.Join(", ", pending.Keys)} were not returned by {_repoUrl}.");
    }
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

  private async Task<bool> IsCached(string objectId) {
    return await _cacheService.TryGetFile(GetCacheKey(objectId)) is not null;
  }

  private static string GetCacheKey(string objectId) => CacheKeys.GitBlob(objectId);

  // Writes the blob to the file, then checks that its git object hash ("blob <size>\0" + content)
  // matches the SHA-1 object id.
  private static async Task WriteVerifiedBlob(IFileInfo file, string objectId, Stream content) {
    using (Stream fileStream = file.Create()) {
      await content.CopyToAsync(fileStream);
    }

    file.Refresh();

    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    hash.AppendData(Encoding.ASCII.GetBytes($"blob {file.Length}\0"));

    using (Stream fileStream = file.OpenRead()) {
      byte[] buffer = new byte[81920];
      int read;
      while ((read = await fileStream.ReadAsync(buffer)) > 0) {
        hash.AppendData(buffer, 0, read);
      }
    }

    VerifyHash(objectId, hash.GetHashAndReset());
  }

  private static void VerifyBlob(string objectId, byte[] content) {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    hash.AppendData(Encoding.ASCII.GetBytes($"blob {content.Length}\0"));
    hash.AppendData(content);
    VerifyHash(objectId, hash.GetHashAndReset());
  }

  private static void VerifyHash(string objectId, byte[] hash) {
    string actual = Convert.ToHexStringLower(hash);
    if (!string.Equals(actual, objectId, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidDataException($"Blob content hash {actual} does not match object id {objectId}.");
    }
  }
}
