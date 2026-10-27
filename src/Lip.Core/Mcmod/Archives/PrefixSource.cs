using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// A view of the files under a directory of another source, with keys relative to that
/// directory. Used for directory refs into ZIP archives (spec §3).
public class PrefixSource(ISource inner, string directory) : ISource {
  private readonly ISource _inner = inner;
  private readonly string _prefix = directory.Trim('/') + "/";

  public IEnumerable<string> Keys => _inner.Keys
      .Where(k => k.StartsWith(_prefix, StringComparison.Ordinal) && k.Length > _prefix.Length)
      .Select(k => k[_prefix.Length..]);

  public Task<Stream> OpenRead(string key) => _inner.OpenRead(_prefix + key);

  public Task Prefetch(IEnumerable<string> keys) => _inner.Prefetch(keys.Select(k => _prefix + k));
}
