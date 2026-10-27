using System.IO.Abstractions;
using System.IO.Compression;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// The files of a ZIP file. Unlike ArchiveSource, which detects the archive format, this reads
/// the file strictly as ZIP with the same parser ZipValidator checks it with, so the entries that
/// were validated are exactly the entries that are served.
public class ZipSource(Func<Stream> openRead) : ISource {
  private readonly Func<Stream> _openRead = openRead;

  public ZipSource(IFileInfo zipFile) : this(zipFile.OpenRead) {
  }

  /// A ZIP file held in memory, e.g. one extracted from another archive.
  public ZipSource(byte[] content) : this(() => new MemoryStream(content, writable: false)) {
  }

  public IEnumerable<string> Keys {
    get {
      using Stream stream = _openRead();
      using ZipArchive zip = new(stream, ZipArchiveMode.Read);

      return [.. zip.Entries.Where(e => !IsDirectory(e)).Select(e => e.FullName).Distinct()];
    }
  }

  public async Task<Stream> OpenRead(string key) {
    using Stream stream = _openRead();
    using ZipArchive zip = new(stream, ZipArchiveMode.Read);

    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName == key && !IsDirectory(e))
        ?? throw new ArgumentException($"Key not found: {key}", nameof(key));

    MemoryStream memory = new();
    using (Stream entryStream = entry.Open()) {
      await entryStream.CopyToAsync(memory);
    }

    memory.Position = 0;
    return memory;
  }

  private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/');
}
