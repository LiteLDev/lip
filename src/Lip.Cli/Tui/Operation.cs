namespace Lip.Cli.Tui;

/// Something a command downloads.
public sealed class ProgressItem(string label) {
  public string Label { get; } = label;
  public int Percent { get; set; }
  public bool Done { get; set; }
}

/// A command that runs, with what it told the user so far. Read and written on the thread of
/// the interface only.
public sealed class Operation(string title, bool cancellable) {
  public string Title { get; } = title;

  /// Whether the user may cancel the command.
  public bool Cancellable { get; } = cancellable;

  /// The messages of the command, and what its hook commands wrote, in order.
  public List<StyledLine> Log { get; } = [];

  public List<ProgressItem> Progress { get; } = [];

  public CancellationTokenSource Cancel { get; } = new();

  /// Whether the command gave a warning.
  public bool Warned { get; set; }

  // The last line of the log, when a hook command wrote it as transient.
  internal StyledLine? TransientLine { get; set; }
}

/// The daemon of lip, as lip daemon status finds it (protocol/lip-daemon.md, "Commands").
public sealed record DaemonState(string Version, int ProcessId, int Commands);

/// What a message in the bottom bar is about.
public enum ToastKind {
  Info,
  Good,
  Warning,
  Bad,
}
