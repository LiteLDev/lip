namespace Lip.Cli.Tui;

public sealed partial class TuiApp {
  /// Something a command downloads.
  private sealed class ProgressItem(string label) {
    public string Label { get; } = label;
    public int Percent { get; set; }
    public bool Done { get; set; }
  }

  /// A command that runs, with what it told the user so far.
  private sealed class Operation(string title, bool cancellable) {
    public string Title { get; } = title;
    public bool Cancellable { get; } = cancellable;
    public List<StyledLine> Log { get; } = [];
    public List<ProgressItem> Progress { get; } = [];
    public CancellationTokenSource Cancel { get; } = new();
    public bool Warned { get; set; }

    // The last line of the log, when a hook command wrote it as transient.
    public StyledLine? TransientLine { get; set; }
  }

  private Operation? _operation;

  /// Runs a command in the background, showing its progress; then calls succeeded, or shows
  /// why it failed. One command runs at a time.
  private void Start(
      string title,
      Func<CancellationToken, Task> action,
      Action<Operation> succeeded,
      bool cancellable = false) {
    if (_operation is not null) {
      return;
    }

    Operation operation = new(title, cancellable);
    ProgressDialog dialog = new(operation);
    _operation = operation;
    Push(dialog);
    _ = Task.Run(async () => {
      Exception? failure = null;
      try {
        await action(operation.Cancel.Token);
      }
      catch (Exception ex) {
        failure = ex;
      }

      Post(() => {
        Close(dialog);
        _operation = null;
        if (failure is null) {
          succeeded(operation);
        } else {
          ShowFailure(title, failure, operation.Cancel.IsCancellationRequested, operation.Log);
        }
      });
    });
  }

  /// Shows why a command failed, with what to do about it and the failure report, as lip
  /// writes them to standard error outside the interface.
  private void ShowFailure(
      string title, Exception exception, bool cancelled, IReadOnlyList<StyledLine> log) {
    Failure failure = _describeFailure(exception, ["(interface)", title], cancelled);
    List<StyledLine> lines = [.. log.TakeLast(200)];
    if (lines.Count > 0) {
      lines.Add(new StyledLine());
    }

    if (failure.Cancelled) {
      lines.Add(new StyledLine("Cancelled. Any changes to the workspace were undone.", Theme.Bad));
    } else {
      string mark = "error: ";
      foreach (string line in failure.Summary.Split('\n')) {
        lines.Add(new StyledLine().Add(mark, Theme.Bad).Add(line));
        mark = "       ";
      }

      foreach (string hint in failure.Hints) {
        lines.Add(new StyledLine().Add("hint: ", Theme.Warning).Add(hint));
      }

      lines.Add(new StyledLine());
      lines.Add(failure.ReportPath is { } path
          ? new StyledLine().Add("Full report: ", Theme.Muted).Add(path, Theme.Accent)
          : new StyledLine($"Could not write a failure report: {failure.ReportError}", Theme.Muted));
    }

    Push(new MessageDialog(
        this, $"{title} failed", lines, ["Close"], border: Theme.Bad, fromEnd: true));
  }

  /// Shows what a command told the user, once it succeeded.
  private void ShowResult(string title, Operation operation, string done) {
    List<StyledLine> lines = [.. operation.Log, new StyledLine("✓ " + done, Theme.Good)];
    Push(new MessageDialog(this, title, lines, ["OK"], fromEnd: true));
  }

  /// A workspace operation: first as a dry run, whose messages are the plan the user confirms,
  /// then for real.
  private void PlanAndRun(string title, Func<bool, CancellationToken, Task> operation) {
    Start($"Planning: {title}", token => operation(true, token), plan => {
      // What would happen to which packages stands out from the files it would touch.
      List<StyledLine> lines = [
        .. plan.Log.Select(line =>
            line.PlainText.StartsWith("Dry run:", StringComparison.Ordinal)
                ? new StyledLine(line.PlainText, Theme.Strong)
                : line),
      ];
      if (lines.Count == 0) {
        lines.Add(new StyledLine("Nothing would change.", Theme.Muted));
      }

      Push(new MessageDialog(this, title, lines, ["Apply", "Cancel"], choice => {
        if (choice != 0) {
          return;
        }

        Start(title, token => operation(false, token), done => {
          LoadPackages();
          ShowResult(title, done, "Done.");
        }, cancellable: true);
      }));
    }, cancellable: true);
  }

  /// The words of a line of arguments; a word in double quotes may hold spaces.
  private static List<string> SplitArguments(string text) {
    List<string> words = [];
    System.Text.StringBuilder word = new();
    bool quoted = false;
    bool any = false;
    foreach (char c in text) {
      if (c == '"') {
        quoted = !quoted;
        any = true;
      } else if (char.IsWhiteSpace(c) && !quoted) {
        if (any) {
          words.Add(word.ToString());
          word.Clear();
          any = false;
        }
      } else {
        word.Append(c);
        any = true;
      }
    }

    if (any) {
      words.Add(word.ToString());
    }

    return words;
  }
}
