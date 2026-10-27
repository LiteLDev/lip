using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// A source that is opened on first use. Installed packages are rebuilt from the lock without
/// touching their archives; the archive is only opened (and its identity verified, spec §8) when
/// something reads it, e.g. a relative ref of an installed package's install edge.
public class DeferredSource(Func<Task<ISource>> open) : ISource {
  private readonly Lazy<Task<ISource>> _source = new(open);

  public IEnumerable<string> Keys => Task.Run(GetSource).GetAwaiter().GetResult().Keys;

  public async Task<Stream> OpenRead(string key) => await (await GetSource()).OpenRead(key);

  public async Task Prefetch(IEnumerable<string> keys) => await (await GetSource()).Prefetch(keys);

  private Task<ISource> GetSource() => _source.Value;
}
