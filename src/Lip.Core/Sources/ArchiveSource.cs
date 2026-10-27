using System.IO.Abstractions;
using System.IO.Compression;
using SharpCompress.Archives;

namespace Lip.Core.Sources;

/// The files of an archive whose format (ZIP, tar, gzipped tar, ...) is detected from its
/// content.
public class ArchiveSource(IFileInfo archiveFileInfo) : ISource {
  private readonly IFileInfo _archiveFileInfo = archiveFileInfo;

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

  /// The archive file, decompressed into memory when it is gzipped: SharpCompress opens tar
  /// archives, but not gzipped ones.
  private Stream OpenArchiveStream() {
    Stream stream = _archiveFileInfo.OpenRead();
    Span<byte> magic = stackalloc byte[2];
    int read = stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
    stream.Position = 0;
    if (read < magic.Length || magic[0] != 0x1f || magic[1] != 0x8b) {
      return stream;
    }

    using (stream) {
      using GZipStream gzip = new(stream, CompressionMode.Decompress);
      MemoryStream memory = new();
      gzip.CopyTo(memory);
      memory.Position = 0;
      return memory;
    }
  }
}
