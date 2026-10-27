using System.IO.Abstractions;
using System.IO.Compression;
using SharpCompress.Archives;

namespace Lip.Core.Sources;

/// The files of an archive whose format (ZIP, tar, gzipped tar, ...) is detected from its
/// content.
public class ArchiveSource(IFileInfo archiveFileInfo) : ISource {
  // A gzipped archive, decompressed once: SharpCompress opens tar archives, but not gzipped ones.
  private readonly Lazy<byte[]?> _decompressed = new(() => Decompress(archiveFileInfo));

  public IEnumerable<string> Keys {
    get {
      using Stream archiveStream = OpenArchiveStream();
      using IArchive archive = ArchiveFactory.OpenArchive(archiveStream);

      return [.. archive.Entries
                .Where(e => !e.IsDirectory && e.Key is not null)
                .Select(e => e.Key!)];
    }
  }

  public virtual async Task<Stream> OpenRead(string key) {
    using Stream archiveStream = OpenArchiveStream();
    using IArchive archive = ArchiveFactory.OpenArchive(archiveStream);

    IArchiveEntry entry = archive.Entries.FirstOrDefault(e => e.Key == key && !e.IsDirectory)
        ?? throw new ArgumentException($"Key not found: {key}", nameof(key));

    MemoryStream memoryStream = new();

    await entry.WriteToAsync(memoryStream);

    memoryStream.Position = 0;

    return memoryStream;
  }

  private Stream OpenArchiveStream() => _decompressed.Value is byte[] decompressed
      ? new MemoryStream(decompressed, writable: false)
      : archiveFileInfo.OpenRead();

  /// The decompressed content of a gzipped file, or null when the file is not gzipped.
  private static byte[]? Decompress(IFileInfo file) {
    using Stream stream = file.OpenRead();
    Span<byte> magic = stackalloc byte[2];
    if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length
        || magic[0] != 0x1f || magic[1] != 0x8b) {
      return null;
    }

    stream.Position = 0;
    using GZipStream gzip = new(stream, CompressionMode.Decompress);
    using MemoryStream memory = new();
    gzip.CopyTo(memory);
    return memory.ToArray();
  }
}
