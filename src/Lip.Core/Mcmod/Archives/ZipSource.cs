using System.Collections.Concurrent;
using System.IO.Compression;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// The files of a ZIP file held in memory. Unlike ArchiveSource, which detects the archive format,
/// this reads the content strictly as ZIP with the same parser ZipValidator checks it with, so the
/// entries that were validated are exactly the entries that are served.
///
/// description names the archive in error messages (e.g. "the download of <URL>");
/// cachedDownload says the content was read from a download in the lip cache, which
/// 'lip cache clean' removes.
public class ZipSource(byte[] content, string? description = null, bool cachedDownload = false)
    : ISource {
  private readonly byte[] _content = content;
  private readonly string _description = description ?? "the ZIP archive";
  private readonly bool _cachedDownload = cachedDownload;

  // Files extracted by Prefetch, by key.
  private readonly ConcurrentDictionary<string, byte[]> _prefetched = new();

  public IEnumerable<string> Keys {
    get {
      using ZipArchive zip = OpenZip();

      return [.. zip.Entries.Where(e => !IsDirectory(e)).Select(e => e.FullName).Distinct()];
    }
  }

  public async Task<Stream> OpenRead(string key) {
    if (_prefetched.TryGetValue(key, out byte[]? prefetched)) {
      return new MemoryStream(prefetched, writable: false);
    }

    using ZipArchive zip = OpenZip();
    return new MemoryStream(await Extract(zip, key), writable: false);
  }

  /// Extracts the given files now, so that a damaged file or one compressed with a method lip
  /// does not support fails here rather than when it is read.
  public async Task Prefetch(IEnumerable<string> keys) {
    string[] missing = [.. keys.Where(k => !_prefetched.ContainsKey(k)).Distinct()];
    if (missing.Length == 0) {
      return;
    }

    using ZipArchive zip = OpenZip();
    foreach (string key in missing) {
      _prefetched.TryAdd(key, await Extract(zip, key));
    }
  }

  private async Task<byte[]> Extract(ZipArchive zip, string key) {
    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName == key && !IsDirectory(e))
        ?? throw new ArgumentException($"{Capitalize(_description)} has no file '{key}'.");

    using MemoryStream memory = new();
    try {
      using Stream entryStream = entry.Open();
      await entryStream.CopyToAsync(memory);
    }
    catch (InvalidDataException ex) {
      throw new InvalidDataException(
          $"Could not extract '{key}' from {_description}: the archive is damaged, or the file is"
          + " compressed with a method lip does not support (only Deflate, Deflate64 and no"
          + " compression are supported)." + DamagedAdvice, ex);
    }

    return memory.ToArray();
  }

  /// Opens the archive, naming it in the errors of reading its directory.
  private ZipArchive OpenZip() {
    try {
      return new ZipArchive(new MemoryStream(_content, writable: false), ZipArchiveMode.Read);
    }
    catch (InvalidDataException ex) {
      throw new InvalidDataException(
          $"{Capitalize(_description)} is not a valid ZIP archive or is damaged." + DamagedAdvice,
          ex);
    }
  }

  private string DamagedAdvice => _cachedDownload
      ? " The download may have been damaged after it was stored: run 'lip cache clean' and try"
        + " again. If this keeps happening, ask the package author to create the archive with a"
        + " standard ZIP tool."
      : " Ask the package author to create the archive with a standard ZIP tool.";

  private static string Capitalize(string text) =>
      text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

  private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/');
}
