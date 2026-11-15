using System.Text;

namespace Lip.Cli.Daemon;

/// Text for a standard stream of the client, sent as Text frames as it is written, which the
/// client writes as text, in its own encoding.
public sealed class ConnectionTextWriter(FrameWriter writer, byte stream) : TextWriter {
  public override Encoding Encoding { get; } = new UTF8Encoding(false);

  public override void Write(char value) => Send([value]);

  public override void Write(char[] buffer, int index, int count) =>
      Send(buffer.AsSpan(index, count));

  public override void Write(ReadOnlySpan<char> buffer) => Send(buffer);

  public override void Write(string? value) => Send(value);

  public override void WriteLine(string? value) => Send(value + NewLine);

  private void Send(ReadOnlySpan<char> text) {
    if (text.IsEmpty) {
      return;
    }

    writer.Send(FrameType.Text, Messages.Output(stream, Encoding.GetBytes(text.ToArray())));
  }
}

/// Bytes for a standard stream of the client, sent as Output frames as they are written.
public sealed class ConnectionOutputStream(FrameWriter writer, byte stream) : Stream {
  public override bool CanRead => false;

  public override bool CanSeek => false;

  public override bool CanWrite => true;

  public override long Length => throw new NotSupportedException();

  public override long Position {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override void Flush() {
  }

  public override int Read(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
      Write(buffer.AsSpan(offset, count));

  public override void Write(ReadOnlySpan<byte> buffer) {
    if (!buffer.IsEmpty) {
      writer.Send(FrameType.Output, Messages.Output(stream, buffer));
    }
  }

  // Closing the stream that a command opened does not close the standard stream of the client.
  protected override void Dispose(bool disposing) {
  }
}

/// The standard input of the client: each read asks the client for at most as many bytes as it
/// reads (InputWanted), so that only the bytes that lip reads are taken from the client
/// (protocol/lip-daemon.md, "Carrying out a command").
public sealed class ConnectionInput(FrameWriter writer) : Stream {
  private readonly object _gate = new();
  private readonly Queue<byte[]> _chunks = new();
  private byte[] _current = [];
  private int _offset;
  private bool _ended;
  private bool _wanted;
  private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

  public override bool CanRead => true;

  public override bool CanSeek => false;

  public override bool CanWrite => false;

  public override long Length => throw new NotSupportedException();

  public override long Position {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  /// Bytes from the client; none when its standard input ended.
  public void Receive(byte[] bytes) {
    TaskCompletionSource arrived;
    lock (_gate) {
      _wanted = false;
      if (bytes.Length == 0) {
        _ended = true;
      } else {
        _chunks.Enqueue(bytes);
      }

      arrived = _arrived;
      _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    arrived.TrySetResult();
  }

  /// The client is gone: reads return nothing from now on.
  public void End() => Receive([]);

  public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer, CancellationToken cancellationToken = default) {
    if (buffer.IsEmpty) {
      return 0;
    }

    while (true) {
      Task arrived;
      lock (_gate) {
        if (_offset == _current.Length && _chunks.TryDequeue(out byte[]? next)) {
          _current = next;
          _offset = 0;
        }

        if (_offset < _current.Length) {
          int count = Math.Min(buffer.Length, _current.Length - _offset);
          _current.AsMemory(_offset, count).CopyTo(buffer);
          _offset += count;
          return count;
        }

        if (_ended) {
          return 0;
        }

        arrived = _arrived.Task;
        if (!_wanted) {
          _wanted = true;
          if (!writer.Send(FrameType.InputWanted, Messages.Int32(buffer.Length))) {
            _ended = true;
            return 0;
          }
        }
      }

      await arrived.WaitAsync(cancellationToken);
    }
  }

  public override int Read(byte[] buffer, int offset, int count) =>
      ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

  public override Task<int> ReadAsync(
      byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
      ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

  public override void Flush() {
  }

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();

  // Closing the stream that a command opened does not close the standard input of the client.
  protected override void Dispose(bool disposing) {
  }
}
