namespace Lip.Core.Sources;

public interface ISource {
  public IEnumerable<string> Keys { get; }

  public Task<Stream> OpenRead(string key);

  /// Hints that the given keys are about to be read, so sources that fetch lazily can
  /// download them in a single batch. Sources that already hold their content do nothing.
  public Task Prefetch(IEnumerable<string> keys) => Task.CompletedTask;

  /// Reads the whole content of a key from where the source gets it, never from a cache, so a
  /// remote source fetches it again on every call.
  public async Task<byte[]> ReadUncached(string key) {
    using Stream stream = await OpenRead(key);
    using MemoryStream memory = new();
    await stream.CopyToAsync(memory);
    return memory.ToArray();
  }
}
