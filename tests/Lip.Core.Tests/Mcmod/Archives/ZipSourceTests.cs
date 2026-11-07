using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod.Archives;

public class ZipSourceTests {
  private const uint _localHeaderSignature = 0x04034b50;
  private const uint _centralHeaderSignature = 0x02014b50;

  /// A ZIP archive of Deflate-compressed entries.
  internal static byte[] Zip(params (string Name, string Content)[] entries) =>
      Zip(CompressionLevel.Optimal, entries);

  /// A ZIP archive of uncompressed (stored) entries.
  private static byte[] StoredZip(params (string Name, string Content)[] entries) =>
      Zip(CompressionLevel.NoCompression, entries);

  private static byte[] Zip(CompressionLevel level, (string Name, string Content)[] entries) {
    using MemoryStream memory = new();
    using (ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach ((string name, string content) in entries) {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        using Stream stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
      }
    }
    return memory.ToArray();
  }

  /// The offset of the local file header of the entry with the given name.
  private static int LocalHeaderOffset(byte[] zip, string name) {
    byte[] nameBytes = Encoding.UTF8.GetBytes(name);
    for (int i = 0; i + 30 <= zip.Length; i++) {
      if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i)) == _localHeaderSignature
          && BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(i + 26)) == nameBytes.Length
          && zip.AsSpan(i + 30, nameBytes.Length).SequenceEqual(nameBytes)) {
        return i;
      }
    }
    throw new InvalidOperationException($"No local header for {name}.");
  }

  /// Sets the compression method of every entry, in the local and the central headers.
  private static void SetCompressionMethod(byte[] zip, ushort method) {
    for (int i = 0; i + 4 <= zip.Length; i++) {
      uint signature = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i));
      if (signature == _localHeaderSignature) {
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(i + 8), method);
      } else if (signature == _centralHeaderSignature) {
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(i + 10), method);
      }
    }
  }

  /// Damages the data of a Deflate-compressed entry: its first block gets the reserved block
  /// type, which the archive's directory does not reveal, only extracting the entry.
  internal static void CorruptData(byte[] zip, string name) {
    int header = LocalHeaderOffset(zip, name);
    int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(header + 26));
    int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(header + 28));
    zip[header + 30 + nameLength + extraLength] = 0xFF;
  }

  private static async Task<string> Read(ZipSource source, string key) {
    using StreamReader reader = new(await source.OpenRead(key));
    return await reader.ReadToEndAsync();
  }

  [Fact]
  public async Task Prefetch_ValidEntries_OpenReadServesThem() {
    // Arrange
    ZipSource source = new(StoredZip(("a.txt", "a"), ("sub/b.txt", "b")));

    // Act
    await source.Prefetch(["a.txt", "sub/b.txt"]);

    // Assert
    Assert.Equal("a", await Read(source, "a.txt"));
    Assert.Equal("b", await Read(source, "sub/b.txt"));
  }

  [Fact]
  public async Task Prefetch_UnsupportedCompressionMethod_ThrowsInvalidData() {
    // Arrange: 14 is LZMA, which lip does not support.
    byte[] zip = StoredZip(("a.txt", "a"));
    SetCompressionMethod(zip, 14);
    ZipSource source = new(zip, "the download of https://example.com/a.zip",
        cachedDownload: true);

    // Act
    InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
        () => source.Prefetch(["a.txt"]));

    // Assert
    Assert.Contains("'a.txt'", ex.Message);
    Assert.Contains("lip cache clean", ex.Message);
  }

  [Fact]
  public async Task Prefetch_DamagedEntry_ThrowsInvalidData() {
    // Arrange
    byte[] zip = Zip(("a.txt", "content"), ("b.txt", "b"));
    CorruptData(zip, "a.txt");
    ZipSource source = new(zip);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidDataException>(() => source.Prefetch(["b.txt", "a.txt"]));
  }

  [Fact]
  public async Task Prefetch_OnlyTheGivenKeys_LeavesOthersUnread() {
    // Arrange: a damaged entry that is not prefetched does not fail the prefetch.
    byte[] zip = Zip(("a.txt", "content"), ("b.txt", "b"));
    CorruptData(zip, "a.txt");
    ZipSource source = new(zip);

    // Act
    await source.Prefetch(["b.txt"]);

    // Assert
    Assert.Equal("b", await Read(source, "b.txt"));
  }

  [Fact]
  public async Task Prefetch_MissingKey_ThrowsArgument() {
    // Arrange
    ZipSource source = new(StoredZip(("a.txt", "a")));

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => source.Prefetch(["b.txt"]));
  }
}
