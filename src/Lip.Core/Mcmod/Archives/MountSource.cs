using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// Several sources as one: the files of each source are under its mount, a directory name
/// without '/'. A source whose only key is "" (a single downloaded file) is the file named by the
/// mount itself. Used for the assets of a tooth.json converted to format 4 (spec §9.4).
public class MountSource : ISource {
  private readonly IReadOnlyList<(string Mount, ISource Source)> _mounts;

  public MountSource(IReadOnlyList<(string Mount, ISource Source)> mounts) {
    if (mounts.Any(m => m.Mount is "" || m.Mount.Contains('/'))
        || mounts.Select(m => m.Mount).Distinct(StringComparer.Ordinal).Count() != mounts.Count) {
      throw new ArgumentException("Mounts must be distinct directory names.", nameof(mounts));
    }

    _mounts = mounts;
  }

  public IReadOnlyList<(string Mount, ISource Source)> Mounts => _mounts;

  /// The key of a mounted source's key.
  public static string Key(string mount, string key) => key == "" ? mount : $"{mount}/{key}";

  /// Keys in mount order, each source's keys in its own order.
  public IEnumerable<string> Keys => _mounts.SelectMany(m => m.Source.Keys.Select(k => Key(m.Mount, k)));

  public Task<Stream> OpenRead(string key) {
    (ISource source, string innerKey) = Resolve(key);
    return source.OpenRead(innerKey);
  }

  public async Task Prefetch(IEnumerable<string> keys) {
    foreach (IGrouping<ISource, string> group in keys.Select(Resolve)
        .GroupBy(r => r.Source, r => r.Key)) {
      await group.Key.Prefetch(group);
    }
  }

  private (ISource Source, string Key) Resolve(string key) {
    int slash = key.IndexOf('/');
    string mount = slash < 0 ? key : key[..slash];
    ISource source = _mounts.FirstOrDefault(m => m.Mount == mount).Source
        ?? throw new ArgumentException($"Key not found: {key}", nameof(key));
    return (source, slash < 0 ? "" : key[(slash + 1)..]);
  }
}
