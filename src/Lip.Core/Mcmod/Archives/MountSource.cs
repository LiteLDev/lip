using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// Several sources as one: the files of each source are under its mount, a distinct directory
/// name without '/'. Used for the assets of an entry (protocol/workspace.md §4 step 2).
public class MountSource(IReadOnlyList<(string Mount, ISource Source)> mounts) : ISource {
  private readonly IReadOnlyList<(string Mount, ISource Source)> _mounts = mounts;

  /// Keys in mount order, each source's keys in its own order.
  public IEnumerable<string> Keys => _mounts.SelectMany(m => m.Source.Keys.Select(k => $"{m.Mount}/{k}"));

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
    ISource? source = slash < 0 ? null : _mounts.FirstOrDefault(m => m.Mount == key[..slash]).Source;
    return source is null
        ? throw new ArgumentException($"Key not found: {key}", nameof(key))
        : (source, key[(slash + 1)..]);
  }
}
