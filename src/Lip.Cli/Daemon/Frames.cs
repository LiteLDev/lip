using System.Buffers.Binary;

namespace Lip.Cli.Daemon;

/// The kinds of frames that a client and the daemon exchange. Each frame is its length (a 32-bit
/// little-endian count of the bytes that follow it), its kind (one byte) and its payload; the
/// payloads are written with BinaryWriter (Messages).
public enum FrameType : byte {
  /// Client: the version and the executable of the client (Hello).
  Hello = 1,
  /// Daemon: its version, executable, process ID and the number of commands it carries out.
  Welcome = 2,
  /// Client: asks the daemon to exit when it carries out no command.
  Shutdown = 3,
  /// Daemon: whether it exits (a boolean).
  ShutdownReply = 4,
  /// Client: the command to carry out (Start).
  Start = 5,
  /// Client: bytes of its standard input; none when it has ended.
  Input = 6,
  /// Client: a signal it received (a 32-bit PosixSignal).
  Signal = 7,
  /// Daemon: the command reads at most this many bytes of standard input (a 32-bit count).
  InputWanted = 8,
  /// Daemon: bytes for standard output (stream 1) or standard error (stream 2).
  Output = 9,
  /// Daemon: text for standard output (stream 1) or standard error (stream 2).
  Text = 10,
  /// Daemon: the exit code of the command.
  Exit = 11,
  /// Daemon: the signal that ends the client at once.
  Terminate = 12,
  /// Daemon: it is exiting and does not carry out the command; the client starts a new daemon.
  Refused = 13,
  /// Client: a key that the user pressed in the interface (KeyMessage).
  Key = 14,
  /// Client: the size of its terminal, in columns and rows (two 32-bit counts).
  Resize = 15,
}

/// A frame read from a connection.
public readonly record struct Frame(FrameType Type, byte[] Payload);

/// Reading and writing frames.
public static class Frames {
  /// The largest payload accepted; anything larger is not a frame of lip.
  public const int MaxPayload = 64 * 1024 * 1024;

  /// The next frame, or null when the connection ends between two frames. Throws
  /// InvalidDataException when it ends inside a frame or the frame is invalid.
  public static async Task<Frame?> Read(Stream stream, CancellationToken cancellationToken) {
    byte[] header = new byte[5];
    int read = await ReadFully(stream, header, cancellationToken);
    if (read == 0) {
      return null;
    }

    if (read < header.Length) {
      throw new InvalidDataException("The connection ended inside a frame.");
    }

    int length = BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length is < 0 or > MaxPayload) {
      throw new InvalidDataException($"A frame of {length} bytes is not a frame of lip.");
    }

    byte[] payload = new byte[length];
    if (await ReadFully(stream, payload, cancellationToken) < length) {
      throw new InvalidDataException("The connection ended inside a frame.");
    }

    return new Frame((FrameType)header[4], payload);
  }

  /// Writes a frame and flushes it.
  public static void Write(Stream stream, FrameType type, ReadOnlySpan<byte> payload) {
    byte[] frame = new byte[5 + payload.Length];
    BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
    frame[4] = (byte)type;
    payload.CopyTo(frame.AsSpan(5));
    stream.Write(frame);
    stream.Flush();
  }

  private static async Task<int> ReadFully(
      Stream stream, byte[] buffer, CancellationToken cancellationToken) {
    int total = 0;
    while (total < buffer.Length) {
      int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
      if (read == 0) {
        break;
      }

      total += read;
    }

    return total;
  }
}

/// Writes the frames of one end of a connection, one at a time, from any thread. Once the other
/// end is gone, or Close was called, frames are dropped.
public sealed class FrameWriter(Stream stream) {
  private readonly object _gate = new();
  private bool _closed;

  /// Sends the frame; false when it was dropped.
  public bool Send(FrameType type, ReadOnlySpan<byte> payload) {
    lock (_gate) {
      if (_closed) {
        return false;
      }

      try {
        Frames.Write(stream, type, payload);
        return true;
      }
      catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
        _closed = true;
        return false;
      }
    }
  }

  /// Drops every later frame.
  public void Close() {
    lock (_gate) {
      _closed = true;
    }
  }
}
