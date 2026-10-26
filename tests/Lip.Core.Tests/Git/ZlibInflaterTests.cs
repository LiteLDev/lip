using System.IO.Compression;
using System.Text;
using Lip.Core.Git;

namespace Lip.Core.Tests.Git;

public class ZlibInflaterTests {
  public static TheoryData<int, CompressionLevel, bool> Cases() {
    TheoryData<int, CompressionLevel, bool> data = [];
    foreach (int size in new[] { 1, 100, 70_000, 1_000_000 }) {
      foreach (CompressionLevel level in Enum.GetValues<CompressionLevel>()) {
        data.Add(size, level, true);
        data.Add(size, level, false);
      }
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(Cases))]
  public void Inflate_MatchesZLibStreamAndStopsAtStreamEnd(
      int size,
      CompressionLevel level,
      bool compressible) {
    // Arrange
    byte[] original = CreateData(size, compressible);
    byte[] compressed = Compress(original, level);
    byte[] trailing = [0xDE, 0xAD, 0xBE, 0xEF, 1, 2, 3, 4, 5, 6, 7, 8, 9];
    MemoryStream stream = new([.. compressed, .. trailing]);

    // Act
    BufferedByteReader reader = new(stream);
    byte[] output = new byte[size];
    ZlibInflater.Inflate(reader, output);
    long inflateEnd = reader.Position;

    reader.Position = 0;
    ZlibInflater.Skip(reader, size);
    long skipEnd = reader.Position;

    // Assert
    Assert.Equal(original, output);
    Assert.Equal(compressed.Length, inflateEnd);
    Assert.Equal(compressed.Length, skipEnd);
  }

  [Fact]
  public void Inflate_EmptyStream_ConsumesHeaderAndTrailer() {
    // How git compresses an empty object.
    byte[] compressed = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01, 0xFF];
    BufferedByteReader reader = new(new MemoryStream(compressed));

    ZlibInflater.Inflate(reader, []);

    Assert.Equal(8, reader.Position);
  }

  [Fact]
  public void Inflate_TruncatedStream_Throws() {
    byte[] compressed = Compress(CreateData(10_000, compressible: false), CompressionLevel.Optimal);
    BufferedByteReader reader = new(new MemoryStream(compressed[..^100]));

    Assert.Throws<GitProtocolException>(() => ZlibInflater.Inflate(reader, new byte[10_000]));
  }

  [Fact]
  public void Inflate_SizeMismatch_Throws() {
    byte[] compressed = Compress(CreateData(1000, compressible: true), CompressionLevel.Optimal);

    Assert.Throws<GitProtocolException>(
        () => ZlibInflater.Inflate(new BufferedByteReader(new MemoryStream(compressed)), new byte[999]));
    Assert.Throws<GitProtocolException>(
        () => ZlibInflater.Skip(new BufferedByteReader(new MemoryStream(compressed)), 1001));
  }

  [Fact]
  public void Inflate_InvalidHeader_Throws() {
    BufferedByteReader reader = new(new MemoryStream([0x12, 0x34, 0, 0, 0, 0]));

    Assert.Throws<GitProtocolException>(() => ZlibInflater.Inflate(reader, new byte[1]));
  }

  private static byte[] CreateData(int size, bool compressible) {
    Random random = new(size);
    byte[] data = new byte[size];
    if (compressible) {
      byte[] words = Encoding.ASCII.GetBytes("lorem ipsum dolor sit amet tooth json levilamina ");
      for (int i = 0; i < size; i++) {
        data[i] = random.Next(10) == 0 ? (byte)random.Next(256) : words[(i * 7 / 3) % words.Length];
      }
    } else {
      random.NextBytes(data);
    }
    return data;
  }

  private static byte[] Compress(byte[] data, CompressionLevel level) {
    MemoryStream output = new();
    using (ZLibStream zlib = new(output, level, leaveOpen: true)) {
      zlib.Write(data);
    }
    return output.ToArray();
  }
}
