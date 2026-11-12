using System.Globalization;
using System.Text;

namespace Lip.Cli.Tui;

/// The output of a program as styled lines: UTF-8 text whose SGR escape sequences set the colors
/// and whose other escape sequences are dropped. The last line is the one being written.
public sealed class AnsiLog(int maxLines = 5000) {
  private enum State {
    Text,
    Escape,
    Csi,
    Osc,
    OscEscape,
  }

  private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
  private readonly StringBuilder _parameters = new();
  private readonly StringBuilder _run = new();
  private State _state;
  private Style _style;
  private Style _runStyle;
  private bool _carriageReturn;
  private int _column;

  public List<StyledLine> Lines { get; } = [new StyledLine()];

  public void Append(byte[] bytes) {
    char[] chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length, flush: false)];
    int count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
    Append(chars.AsSpan(0, count));
  }

  public void Append(ReadOnlySpan<char> text) {
    foreach (char c in text) {
      switch (_state) {
        case State.Escape:
          _state = c switch {
            '[' => State.Csi,
            ']' => State.Osc,
            _ => State.Text,
          };
          _parameters.Clear();
          break;
        case State.Csi:
          if (c is >= '@' and <= '~') {
            if (c == 'm') {
              ApplySgr(_parameters.ToString());
            }

            _state = State.Text;
          } else {
            _parameters.Append(c);
          }
          break;
        case State.Osc:
          _state = c switch {
            '\a' => State.Text,
            '\x1b' => State.OscEscape,
            _ => State.Osc,
          };
          break;
        case State.OscEscape:
          _state = State.Text;
          break;
        default:
          AppendText(c);
          break;
      }
    }

    Flush();
  }

  /// Ends the line being written, when it holds anything.
  public void EndLine() {
    Flush();
    if (Lines[^1].Spans.Count > 0) {
      NewLine();
    }
  }

  /// Adds a line of its own, after the one being written.
  public void AddLine(StyledLine line) {
    EndLine();
    Lines[^1] = line;
    NewLine();
  }

  private void AppendText(char c) {
    switch (c) {
      case '\x1b':
        Flush();
        _state = State.Escape;
        return;
      case '\n':
        Flush();
        _carriageReturn = false;
        NewLine();
        return;
      case '\r':
        _carriageReturn = true;
        return;
      case '\t':
        do {
          Put(' ');
        } while (_column % 8 != 0);
        return;
    }

    if (c < ' ') {
      return;
    }

    Put(c);
  }

  private void Put(char c) {
    if (_carriageReturn) {
      // A carriage return that no line feed follows: the line is written again.
      _carriageReturn = false;
      _run.Clear();
      Lines[^1].Spans.Clear();
      _column = 0;
    }

    if (_run.Length > 0 && _runStyle != _style) {
      Flush();
    }

    _runStyle = _style;
    _run.Append(c);
    _column++;
  }

  private void Flush() {
    if (_run.Length > 0) {
      Lines[^1].Add(_run.ToString(), _runStyle);
      _run.Clear();
    }
  }

  private void NewLine() {
    Lines.Add(new StyledLine());
    _column = 0;
    if (Lines.Count > maxLines) {
      Lines.RemoveRange(0, Lines.Count - maxLines);
    }
  }

  private void ApplySgr(string parameters) {
    int[] codes = [
      .. parameters.Split(';', ':').Select(p =>
          int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out int code)
              ? code
              : 0),
    ];
    for (int i = 0; i < codes.Length; i++) {
      int code = codes[i];
      switch (code) {
        case 0:
          _style = default;
          break;
        case 1:
          _style = _style with { Bold = true };
          break;
        case 4:
          _style = _style with { Underline = true };
          break;
        case 7:
          _style = _style with { Inverse = true };
          break;
        case 22:
          _style = _style with { Bold = false };
          break;
        case 24:
          _style = _style with { Underline = false };
          break;
        case 27:
          _style = _style with { Inverse = false };
          break;
        case >= 30 and <= 37:
          _style = _style with { Fg = code - 30 + 1 };
          break;
        case 39:
          _style = _style with { Fg = Color.Default };
          break;
        case >= 40 and <= 47:
          _style = _style with { Bg = code - 40 + 1 };
          break;
        case 49:
          _style = _style with { Bg = Color.Default };
          break;
        case >= 90 and <= 97:
          _style = _style with { Fg = code - 90 + 9 };
          break;
        case >= 100 and <= 107:
          _style = _style with { Bg = code - 100 + 9 };
          break;
        case 38 or 48:
          int color = Color.Default;
          if (i + 2 < codes.Length && codes[i + 1] == 5) {
            color = Math.Clamp(codes[i + 2], 0, 255) + 1;
            i += 2;
          } else if (i + 4 < codes.Length && codes[i + 1] == 2) {
            color = Color.TrueColor | ((codes[i + 2] & 0xFF) << 16)
                | ((codes[i + 3] & 0xFF) << 8) | (codes[i + 4] & 0xFF);
            i += 4;
          } else {
            i = codes.Length;
          }

          _style = code == 38 ? _style with { Fg = color } : _style with { Bg = color };
          break;
      }
    }
  }
}

/// A stream that passes each line written to it, in UTF-8, to a delegate: what hook commands
/// write in the interface.
public sealed class LineSinkStream(Action<string> line) : Stream {
  private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
  private readonly StringBuilder _pending = new();

  public override bool CanRead => false;
  public override bool CanSeek => false;
  public override bool CanWrite => true;
  public override long Length => throw new NotSupportedException();

  public override long Position {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override void Write(byte[] buffer, int offset, int count) {
    char[] chars = new char[_decoder.GetCharCount(buffer, offset, count, flush: false)];
    int read = _decoder.GetChars(buffer, offset, count, chars, 0, flush: false);
    foreach (char c in chars.AsSpan(0, read)) {
      if (c == '\n') {
        Emit();
      } else if (c != '\r') {
        _pending.Append(c);
      }
    }
  }

  public override void Flush() {
  }

  protected override void Dispose(bool disposing) {
    if (disposing && _pending.Length > 0) {
      Emit();
    }

    base.Dispose(disposing);
  }

  public override int Read(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();

  private void Emit() {
    line(_pending.ToString());
    _pending.Clear();
  }
}

/// A stream whose reads wait for the bytes written with Send, and end once Complete is called:
/// the standard input of the server in the interface.
public sealed class QueueInputStream : Stream {
  private readonly System.Threading.Channels.Channel<byte[]> _channel =
      System.Threading.Channels.Channel.CreateUnbounded<byte[]>();

  private byte[] _current = [];
  private int _offset;

  public void Send(string text) => _channel.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

  public void Complete() => _channel.Writer.TryComplete();

  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => throw new NotSupportedException();

  public override long Position {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer, CancellationToken cancellationToken = default) {
    while (_offset >= _current.Length) {
      if (!await _channel.Reader.WaitToReadAsync(cancellationToken)) {
        return 0;
      }

      if (_channel.Reader.TryRead(out byte[]? next)) {
        _current = next;
        _offset = 0;
      }
    }

    int count = Math.Min(buffer.Length, _current.Length - _offset);
    _current.AsMemory(_offset, count).CopyTo(buffer);
    _offset += count;
    return count;
  }

  public override int Read(byte[] buffer, int offset, int count) =>
      ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

  public override void Flush() {
  }

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
}
