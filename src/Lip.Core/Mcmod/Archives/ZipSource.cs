using System.IO.Abstractions;
using System.IO.Compression;
using Lip.Core.Infrastructure;
using Lip.Core.Sources;

namespace Lip.Core.Mcmod;

/// The files of a ZIP file. Unlike ArchiveSource, which detects the archive format, this reads
/// the file strictly as ZIP with the same parser ZipValidator checks it with, so the entries that
/// were validated are exactly the entries that are served.
///
/// description names the archive in error messages (e.g. "the cached download of <URL>");
/// cachedDownload says the file is a download in the lip cache, which 'lip cache clean' removes.
public class ZipSource(Func<Stream> openRead, string description, bool cachedDownload) : ISource {
  private readonly Func<Stream> _openRead = openRead;
  private readonly string _description = description;
  private readonly bool _cachedDownload = cachedDownload;

  public ZipSource(IFileInfo zipFile, string? description = null)
      : this(zipFile.OpenRead, description ?? $"the ZIP archive {zipFile.FullName}", cachedDownload: true) {
  }

  /// A ZIP file held in memory, e.g. one extracted from another archive.
  public ZipSource(byte[] content, string? description = null)
      : this(() => new MemoryStream(content, writable: false), description ?? "the ZIP archive",
          cachedDownload: false) {
  }

  public IEnumerable<string> Keys {
    get {
      using ZipArchive zip = OpenZip();

      return [.. zip.Entries.Where(e => !IsDirectory(e)).Select(e => e.FullName).Distinct()];
    }
  }

  public async Task<Stream> OpenRead(string key) {
    using ZipArchive zip = OpenZip();

    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName == key && !IsDirectory(e))
        ?? throw new ArgumentException($"{Capitalize(_description)} has no file '{key}'.");

    MemoryStream memory = new();
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
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError(
          $"Could not read '{key}' from {_description}." + UnreadableAdvice, ex);
    }

    memory.Position = 0;
    return memory;
  }

  /// Opens the archive, naming it in the errors of opening the file or reading its directory.
  private ZipArchive OpenZip() {
    Stream stream;
    try {
      stream = _openRead();
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw FileSystemExtensions.WrapFileError($"Could not open {_description}." + UnreadableAdvice, ex);
    }

    try {
      return new ZipArchive(stream, ZipArchiveMode.Read);
    }
    catch (InvalidDataException ex) {
      stream.Dispose();
      throw new InvalidDataException(
          $"{Capitalize(_description)} is not a valid ZIP archive or is damaged." + DamagedAdvice, ex);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      stream.Dispose();
      throw FileSystemExtensions.WrapFileError($"Could not read {_description}." + UnreadableAdvice, ex);
    }
  }

  private string DamagedAdvice => _cachedDownload
      ? " The download may have been damaged after it was stored: run 'lip cache clean' and try"
        + " again. If this keeps happening, ask the package author to create the archive with a"
        + " standard ZIP tool."
      : " Ask the package author to create the archive with a standard ZIP tool.";

  private string UnreadableAdvice => _cachedDownload
      ? " If another lip command is cleaning the cache, wait for it to finish; otherwise close"
        + " programs that may use the file, or run 'lip cache clean', and try again."
      : "";

  private static string Capitalize(string text) =>
      text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

  private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/');
}
