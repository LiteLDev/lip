using System.Globalization;
using System.Text;

namespace Lip.Core.Git;

public enum PktLineType {
  Data,
  Flush,
  Delimiter,
  ResponseEnd,
  EndOfStream,
}

/// Reads git pkt-lines. The payload of the last data line is valid until the next read.
public sealed class PktLineReader(Stream stream) {
  public const int MaxLineLength = 65520;

  private readonly Stream _stream = stream;
  private readonly byte[] _header = new byte[4];
  private readonly byte[] _buffer = new byte[MaxLineLength];

  public ReadOnlyMemory<byte> Payload { get; private set; }

  /// The payload of the last data line decoded as UTF-8, without the trailing LF.
  public string PayloadText {
    get {
      ReadOnlySpan<byte> span = Payload.Span;
      if (span.Length > 0 && span[^1] == (byte)'\n') {
        span = span[..^1];
      }
      return Encoding.UTF8.GetString(span);
    }
  }

  public async Task<PktLineType> Read() {
    Payload = ReadOnlyMemory<byte>.Empty;

    int headerLength = await _stream.ReadAtLeastAsync(_header, 4, throwOnEndOfStream: false);
    if (headerLength == 0) {
      return PktLineType.EndOfStream;
    }
    if (headerLength < 4) {
      throw new GitProtocolException("The response ended in the middle of a pkt-line header.");
    }

    if (!int.TryParse(
        Encoding.ASCII.GetString(_header),
        NumberStyles.AllowHexSpecifier,
        CultureInfo.InvariantCulture,
        out int length)) {
      throw new GitProtocolException(
          $"Invalid pkt-line length '{Encoding.ASCII.GetString(_header)}'; the response is not in git"
          + " pkt-line format.");
    }

    switch (length) {
      case 0:
        return PktLineType.Flush;
      case 1:
        return PktLineType.Delimiter;
      case 2:
        return PktLineType.ResponseEnd;
      case 3:
      case > MaxLineLength:
        throw new GitProtocolException($"Invalid pkt-line length {length}; lengths must be 0 to 2 or 4 to {MaxLineLength}.");
    }

    try {
      await _stream.ReadExactlyAsync(_buffer.AsMemory(0, length - 4));
    }
    catch (EndOfStreamException ex) {
      throw new GitProtocolException("The response ended in the middle of a pkt-line payload.", ex);
    }
    Payload = _buffer.AsMemory(0, length - 4);
    return PktLineType.Data;
  }
}

/// Builds a request body made of git pkt-lines.
public sealed class PktLineWriter {
  public const int MaxPayloadLength = PktLineReader.MaxLineLength - 4;

  private readonly MemoryStream _stream = new();

  public PktLineWriter WriteLine(string text) {
    return WriteData(Encoding.UTF8.GetBytes(text + "\n"));
  }

  public PktLineWriter WriteData(ReadOnlySpan<byte> data) {
    if (data.Length > MaxPayloadLength) {
      throw new ArgumentException(
          $"A git request line of {data.Length} bytes is longer than the {MaxPayloadLength} bytes git allows.", nameof(data));
    }
    _stream.Write(Encoding.ASCII.GetBytes((data.Length + 4).ToString("x4", CultureInfo.InvariantCulture)));
    _stream.Write(data);
    return this;
  }

  public PktLineWriter WriteFlush() {
    _stream.Write("0000"u8);
    return this;
  }

  public PktLineWriter WriteDelimiter() {
    _stream.Write("0001"u8);
    return this;
  }

  public byte[] ToArray() {
    return _stream.ToArray();
  }
}
