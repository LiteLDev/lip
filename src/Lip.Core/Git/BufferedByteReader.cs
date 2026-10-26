namespace Lip.Core.Git;

/// A seekable byte-at-a-time reader over a seekable stream with its own buffer.
public sealed class BufferedByteReader(Stream stream) {
  private readonly Stream _stream = stream;
  private readonly byte[] _buffer = new byte[64 * 1024];
  private long _bufferStart;
  private int _bufferLength;
  private int _bufferPosition;

  public long Length => _stream.Length;

  public long Position {
    get => _bufferStart + _bufferPosition;
    set {
      if (value >= _bufferStart && value <= _bufferStart + _bufferLength) {
        _bufferPosition = (int)(value - _bufferStart);
      } else {
        _bufferStart = value;
        _bufferLength = 0;
        _bufferPosition = 0;
      }
    }
  }

  /// Returns the next byte, or -1 at end of stream.
  public int ReadByte() {
    if (_bufferPosition >= _bufferLength && !Refill()) {
      return -1;
    }
    return _buffer[_bufferPosition++];
  }

  public void ReadExactly(Span<byte> destination) {
    for (int i = 0; i < destination.Length; i++) {
      int value = ReadByte();
      if (value < 0) {
        throw new GitProtocolException("Unexpected end of packfile.");
      }
      destination[i] = (byte)value;
    }
  }

  private bool Refill() {
    _bufferStart += _bufferLength;
    _bufferPosition = 0;
    _stream.Position = _bufferStart;
    _bufferLength = _stream.Read(_buffer);
    return _bufferLength > 0;
  }
}
