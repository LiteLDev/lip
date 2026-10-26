using System.Text;
using Lip.Core.Git;

namespace Lip.Core.Tests.Git;

public class PktLineTests {
  [Fact]
  public async Task WriteAndRead_RoundTripsAllPacketTypes() {
    // Arrange
    byte[] binary = [1, 0, 255, 10];
    byte[] bytes = new PktLineWriter()
        .WriteLine("command=ls-refs")
        .WriteDelimiter()
        .WriteData(binary)
        .WriteResponseEnd()
        .WriteFlush()
        .ToArray();
    PktLineReader reader = new(new MemoryStream(bytes));

    // Act & Assert
    Assert.Equal("0014command=ls-refs\n00010008", Encoding.ASCII.GetString(bytes, 0, 28));
    Assert.Equal(PktLineType.Data, await reader.Read());
    Assert.Equal("command=ls-refs", reader.PayloadText);
    Assert.Equal(PktLineType.Delimiter, await reader.Read());
    Assert.Equal(PktLineType.Data, await reader.Read());
    Assert.Equal(binary, reader.Payload.ToArray());
    Assert.Equal(PktLineType.ResponseEnd, await reader.Read());
    Assert.Equal(PktLineType.Flush, await reader.Read());
    Assert.Equal(PktLineType.EndOfStream, await reader.Read());
    Assert.Equal(bytes.Length, reader.BytesRead);
  }

  [Fact]
  public async Task Read_MaxLengthPayload_Succeeds() {
    byte[] payload = new byte[PktLineWriter.MaxPayloadLength];
    Random.Shared.NextBytes(payload);
    PktLineReader reader = new(new MemoryStream(new PktLineWriter().WriteData(payload).ToArray()));

    Assert.Equal(PktLineType.Data, await reader.Read());
    Assert.Equal(payload, reader.Payload.ToArray());
  }

  [Fact]
  public void WriteData_TooLong_Throws() {
    Assert.Throws<ArgumentException>(
        () => new PktLineWriter().WriteData(new byte[PktLineWriter.MaxPayloadLength + 1]));
  }

  [Theory]
  [InlineData("zzzz")]
  [InlineData("0003")]
  [InlineData("fff1")]
  [InlineData("00")]
  [InlineData("0010abc")]
  public async Task Read_Malformed_Throws(string data) {
    PktLineReader reader = new(new MemoryStream(Encoding.ASCII.GetBytes(data)));

    await Assert.ThrowsAsync<GitProtocolException>(reader.Read);
  }
}
