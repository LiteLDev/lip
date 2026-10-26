namespace Lip.Core.Sources;

public class CompositeSource(IEnumerable<ISource> providers) : ISource {
  private readonly IEnumerable<ISource> _providers = providers;

  public IEnumerable<string> Keys => _providers
      .SelectMany(p => p.Keys)
      .Distinct();

  public async Task<Stream> OpenRead(string key) {
    foreach (ISource provider in _providers) {
      if (provider.Keys.Contains(key)) {
        return await provider.OpenRead(key);
      }
    }

    throw new ArgumentException($"Key not found: {key}", nameof(key));
  }

  /// Forwards each key to the provider OpenRead would read it from (the first one serving it).
  public async Task Prefetch(IEnumerable<string> keys) {
    HashSet<string> remaining = [.. keys];

    foreach (ISource provider in _providers) {
      if (remaining.Count == 0) {
        return;
      }

      List<string> served = [.. provider.Keys.Where(remaining.Remove)];

      if (served.Count > 0) {
        await provider.Prefetch(served);
      }
    }
  }
}
