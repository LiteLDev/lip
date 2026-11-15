using System.Collections.Concurrent;
using System.Text;
using Lip.Cli.Tui;

namespace Lip.Cli.Daemon;

/// The terminal of a client, for the interface that the daemon carries out for it
/// (protocol/lip-daemon.md, "Carrying out a command"): what the interface writes goes to the
/// standard output of the client, and the keys and the sizes come from the Key and Resize frames
/// of the client. Once the client is gone, ReadKey throws.
public sealed class ClientTerminal(FrameWriter writer, int width, int height) : ITerminal {
  private static readonly Encoding s_utf8 = new UTF8Encoding(false);

  private readonly BlockingCollection<ConsoleKeyInfo> _keys = new();
  private readonly CancellationTokenSource _ended = new();
  private int _width = width;
  private int _height = height;

  public int Width => Volatile.Read(ref _width);

  public int Height => Volatile.Read(ref _height);

  public void Write(string text) =>
      writer.Send(FrameType.Text, Messages.Output(Messages.StandardOutput, s_utf8.GetBytes(text)));

  public ConsoleKeyInfo ReadKey() => _keys.Take(_ended.Token);

  /// A key that the user pressed.
  public void Key(ConsoleKeyInfo key) => _keys.Add(key);

  /// The terminal of the client has the size now.
  public void Resize(int width, int height) {
    Volatile.Write(ref _width, Math.Max(1, width));
    Volatile.Write(ref _height, Math.Max(1, height));
  }

  /// The client is gone.
  public void End() => _ended.Cancel();
}
