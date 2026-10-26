namespace Lip.Core.Sources;

public interface ISource {
  public IEnumerable<string> Keys { get; }

  public Task<Stream> OpenRead(string key);

  /// Hints that the given keys are about to be read, so sources that fetch lazily can
  /// download them in a single batch. Sources that already hold their content do nothing.
  public Task Prefetch(IEnumerable<string> keys) => Task.CompletedTask;
}
