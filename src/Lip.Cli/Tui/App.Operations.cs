using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui;

/// The commands that the interface carries out: one at a time, in the background, with their
/// progress, their plan, their result or their failure shown in dialogs.
public sealed partial class App {
  // What starts a message of a command that is a warning, and what a dry run starts the
  // messages with that say what it would do.
  private const string WarningMark = "⚠ ";
  private const string DryRunMark = "Dry run:";

  private Operation? _operation;

  // The questions of the commands that the interface asks and the user has not answered, and
  // whether the interface has ended, after which it asks none (AskInDialog).
  private readonly HashSet<TaskCompletionSource<string>> _openQuestions = [];
  private bool _questionsEnded;

  /// Runs a command in the background, showing its progress; then calls succeeded, or shows
  /// why it failed, unless failed, when given, says that it took care of the failure. One
  /// command runs at a time: false, and nothing runs, while another does. failureClosed is
  /// called once the user closed the dialog that says why the command failed.
  internal bool RunOperation(
      string title,
      Func<CancellationToken, Task> action,
      Action<Operation>? succeeded,
      bool cancellable,
      Func<Exception, bool>? failed = null,
      Action? failureClosed = null) {
    if (_operation is not null) {
      return false;
    }

    Operation operation = new(title, cancellable);
    ProgressDialog dialog = new(operation);
    _operation = operation;
    Show(dialog);
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
          succeeded?.Invoke(operation);
        } else if (failed?.Invoke(failure) != true) {
          ShowFailure(
              title, failure, operation.Cancel.IsCancellationRequested, operation.Log,
              failureClosed);
        }
      });
    });
    return true;
  }

  /// Does work in the background without a dialog, then calls done, or failed, on the thread of
  /// the interface. Unless anyWorkspace is set, neither is called when another workspace was
  /// selected meanwhile: what the work read was of the workspace before.
  internal void Background<T>(
      Func<Task<T>> work, Action<T> done, Action<Exception>? failed, bool anyWorkspace) {
    LipClient client = _client;
    _ = Task.Run(async () => {
      try {
        T result = await work();
        Post(() => {
          if (anyWorkspace || client == _client) {
            done(result);
          }
        });
      }
      catch (Exception ex) {
        Post(() => {
          if (!anyWorkspace && client != _client) {
            return;
          }

          if (failed is not null) {
            failed(ex);
          } else {
            Toast(ex.Message, ToastKind.Bad);
          }
        });
      }
    });
  }

  /// Shows why a command failed, with what to do about it and the failure report, which is
  /// written below the selected workspace. A command that the user cancelled did not fail: its
  /// dialog says that it was cancelled, in the colors of any other message. closed is called
  /// once the user closed the dialog.
  internal void ShowFailure(
      string title, Exception exception, bool cancelled, IReadOnlyList<StyledLine> log,
      Action? closed = null) {
    Failure failure = _describeFailure(exception, ["(interface)", title], cancelled, _workspace);
    List<StyledLine> lines = [.. log.TakeLast(200)];
    if (lines.Count > 0) {
      lines.Add(new StyledLine());
    }

    if (failure.Cancelled) {
      // The user asked for it: it is no failure, and is not shown as one.
      lines.Add(new StyledLine(CommonText.Cancelled));
      Show(new ConfirmDialog(
          $"{title} · {CommonText.CancelledWord}", lines, [CommonText.Close],
          _ => closed?.Invoke(), fromEnd: true));
      return;
    }

    string mark = CommonText.Error + ": ";
    foreach (string line in failure.Summary.Split('\n')) {
      lines.Add(new StyledLine().Add(mark, Theme.Bad).Add(line.TrimEnd('\r')));
      mark = new string(' ', TextWidth.Of(mark));
    }

    foreach (string hint in failure.Hints) {
      lines.Add(new StyledLine().Add(CommonText.HintWord + ": ", Theme.Warning).Add(hint));
    }

    lines.Add(new StyledLine());
    lines.Add(failure.ReportPath is { } path
        ? new StyledLine().Add(CommonText.FullReport + ": ", Theme.Muted)
            .Add(_workspaces.FileSystem.Path.Join(_workspace, path), Theme.Accent)
        : new StyledLine($"{CommonText.NoReport}: {failure.ReportError}", Theme.Muted));
    Show(new ConfirmDialog(
        $"{title} · {CommonText.Failed}", lines, [CommonText.Close], _ => closed?.Invoke(),
        border: Theme.Bad, fromEnd: true));
  }

  /// Shows what a command told the user, once it succeeded.
  internal void ShowResult(string title, Operation operation, string done) {
    List<StyledLine> lines = [.. operation.Log, new StyledLine("✓ " + done, Theme.Good)];
    Show(new ConfirmDialog(title, lines, [CommonText.Ok], fromEnd: true));
  }

  /// A workspace operation: first as a dry run, whose messages are the plan; then, when the
  /// user applies the plan, for real, after the license of the game when the dry run says that
  /// a setup would ask for it (protocol/lip-tui.md, "Commands"). Cancelling the plan is the
  /// preview only, and a plan that would change nothing cannot be applied. Both are carried
  /// out in one session, a new one when none is given. Nothing is carried out while the
  /// server of the selected workspace runs. done is called once the operation succeeded and
  /// its result is shown; notApplied once the operation is not carried out: the user cancelled
  /// the plan or declined the license, or closed the dialog that says that the dry run or the
  /// operation failed or was cancelled, or that the server runs.
  internal void PlanAndRun(
      string title,
      Func<OperationRun, Task<OperationResult>> operation,
      Action? done,
      OperationSession? session,
      Action? notApplied = null) {
    if (RefuseWhileServerRuns(title)) {
      return;
    }

    OperationSession shared = session ?? new OperationSession();
    string workspace = _workspace;
    // The server may have started since the interface last looked: it is looked for again,
    // off the thread of the interface, before each of the two.
    void RequireStopped() {
      if (ServerRunsNow(workspace)) {
        throw new ServerRunsException();
      }
    }

    bool ServerRuns(Exception failure) {
      if (failure is not ServerRunsException) {
        return false;
      }

      if (_servers.Find(workspace)?.Running != true) {
        // Another lip runs it: the interface knows before it next looks.
        _serverLocksHeld.Add(workspace);
      }

      ShowServerRuns(title, notApplied);
      return true;
    }

    OperationResult? planned = null;
    RunOperation($"{CommonText.Preview} · {title}", async token => {
      RequireStopped();
      planned = await operation(new OperationRun(shared, DryRun: true, AcceptEula: false, token));
    }, plan => {
      List<StyledLine> lines = [.. plan.Log.Select(PlanLine)];
      if (planned is not { Changes: true }) {
        // Nothing to apply.
        lines.Add(new StyledLine(CommonText.NothingWouldChange, Theme.Muted));
        Show(new ConfirmDialog(title, lines, [CommonText.Close], fromEnd: true));
        return;
      }

      if (planned.OnlyStateFile) {
        lines.Add(new StyledLine(CommonText.OnlyRecordChanges, Theme.Muted));
      }

      bool asksForLicense = plan.Log.Any(l => l.PlainText.StartsWith(
          GameSetup.EulaDryRunMessage, StringComparison.Ordinal));
      void Run(bool acceptEula) =>
          RunOperation(title, async token => {
            RequireStopped();
            await operation(new OperationRun(shared, DryRun: false, acceptEula, token));
          }, result => {
            WorkspaceChanged();
            // What the plan said already, such as what was resolved, is not said again.
            HashSet<string> said = [.. plan.Log.Select(line => line.PlainText)];
            result.Log.RemoveAll(line => Repeats(said, line));
            ShowResult(title, result, CommonText.Done);
            done?.Invoke();
          }, cancellable: true, ServerRuns, notApplied);

      Show(new ConfirmDialog(
          title, lines, [CommonText.Apply, CommonText.Cancel], choice => {
            if (choice != 0) {
              notApplied?.Invoke();
              return;
            }

            if (!asksForLicense) {
              Run(acceptEula: false);
              return;
            }

            // The license of the game, asked in the interface.
            Show(new ConfirmDialog(
                CommonText.EulaTitle,
                [
                  new StyledLine(CommonText.EulaIntro),
                  new StyledLine(),
                  new StyledLine(GameSetup.EulaUrl, Theme.Accent),
                  new StyledLine(GameSetup.PrivacyPolicyUrl, Theme.Accent),
                  new StyledLine(),
                  new StyledLine(CommonText.EulaQuestion),
                ],
                [CommonText.Accept, CommonText.Decline],
                answer => {
                  if (answer == 0) {
                    Run(acceptEula: true);
                  } else {
                    notApplied?.Invoke();
                  }
                }));
          }));
    }, cancellable: true, ServerRuns, notApplied);
  }

  /// A line of the log of a dry run, as the plan shows it: what the operation would do ("Dry
  /// run: would install X 1.0.0 (N files)") stands out from the rest of the log, such as what
  /// was resolved and downloaded, and so does a warning, such as the packages that a setup
  /// would leave out.
  private static StyledLine PlanLine(StyledLine line) {
    string text = line.PlainText;
    return text.StartsWith(WarningMark, StringComparison.Ordinal)
        ? new StyledLine(text, Theme.Warning with { Bold = true })
        : text.StartsWith(DryRunMark, StringComparison.Ordinal)
            ? new StyledLine(text, Theme.Strong)
            : line;
  }

  /// Whether a line of the log of an operation says again what a line of the log of its dry
  /// run said: the same text, which is neither a warning nor an error, nor what a hook
  /// command wrote.
  private static bool Repeats(HashSet<string> plan, StyledLine line) {
    string text = line.PlainText;
    return line.Spans.Count == 1 && line.Spans[0].Style == default(Style)
        && text.Trim().Length > 0 && plan.Contains(text);
  }

  /// The server of the workspace of an operation runs, so the operation is not carried out.
  private sealed class ServerRunsException : Exception;

  /// A message of a command: part of the log of the operation that runs, otherwise a message in
  /// the bottom bar, where a message about something wrong stays rather than give way to one
  /// that is not.
  private void AddMessage(string message, string mark, Style style, ToastKind kind) {
    if (_operation is { } operation) {
      foreach (string line in message.Split('\n')) {
        operation.Log.Add(new StyledLine(mark + line.TrimEnd('\r'), style));
        mark = "  ";
      }
    } else if (kind != ToastKind.Info
        || _toast is not { Kind: ToastKind.Bad or ToastKind.Warning }) {
      Toast(mark + message, kind);
    }
  }

  /// A line that a hook command wrote. A transient one, such as a progress display, gives way
  /// to the line after it.
  private void AddHookLine(StyledLine line, bool transient) {
    if (_operation is not { } operation) {
      return;
    }

    if (operation.TransientLine is { } last && operation.Log.Count > 0
        && ReferenceEquals(operation.Log[^1], last)) {
      operation.Log[^1] = line;
    } else {
      operation.Log.Add(line);
    }

    operation.TransientLine = transient ? line : null;
  }

  /// Asks a question of a command in a dialog, and returns the answer; when the user closes the
  /// dialog without answering, calls dismissed and fails with OperationCanceledException.
  /// Once the interface has ended, fails with CannotAskException, and so does a question still
  /// open then (EndQuestions).
  private Task<string> AskInDialog(string question, Action? dismissed) {
    RunTrace.Write("ask", question);
    TaskCompletionSource<string> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_openQuestions) {
      if (_questionsEnded) {
        throw new CannotAskException(
            $"lip needs {question}, but cannot ask for it: the interface was quit.");
      }

      _openQuestions.Add(answer);
    }

    Post(() => Show(new PromptDialog(
        CommonText.Question, [new PromptField(question)],
        values => answer.TrySetResult(values[0]),
        () => {
          dismissed?.Invoke();
          answer.TrySetException(
              new OperationCanceledException("The question was not answered."));
        })));
    _ = answer.Task.ContinueWith(
        _ => {
          lock (_openQuestions) {
            _openQuestions.Remove(answer);
          }
        },
        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
    return answer.Task;
  }

  /// The interface has ended: the questions still open, such as one of a managed server that it
  /// started, fail, and so does every question asked from now on.
  private void EndQuestions() {
    List<TaskCompletionSource<string>> open;
    lock (_openQuestions) {
      _questionsEnded = true;
      open = [.. _openQuestions];
      _openQuestions.Clear();
    }

    foreach (TaskCompletionSource<string> answer in open) {
      answer.TrySetException(new CannotAskException(
          "lip cannot get the answer to its question: the interface was quit."));
    }
  }

  /// The messages of lip run for a server that the interface starts, which go where server
  /// sends them; its questions, such as whether to install Rosetta 2 (protocol/lip-tui.md,
  /// "Server"), are asked in the interface, after the message that came last before each, such
  /// as the license that the question is about, which the user may not be looking at.
  private sealed class ServerQuestions(App app, IUserInteraction server) : IUserInteraction {
    private string? _lastMessage;

    public Task PrintInfo(string message) {
      _lastMessage = message;
      return server.PrintInfo(message);
    }

    public Task PrintSuccess(string message) => server.PrintSuccess(message);

    public Task PrintWarning(string message) => server.PrintWarning(message);

    public Task PrintError(string message) => server.PrintError(message);

    public Task RunWithProgress(string message, Func<IProgress<double>, Task> action) =>
        server.RunWithProgress(message, action);

    public Task<string> Ask(string question) => app.AskInDialog(
        _lastMessage is { } message ? $"{message} Enter {question}:" : question,
        dismissed: null);
  }

  /// The messages and questions of the commands, shown in the interface.
  private sealed class Interaction(App app) : IUserInteraction {
    public Task PrintInfo(string message) => Print("info", message, "", default, ToastKind.Info);

    public Task PrintSuccess(string message) =>
        Print("success", message, "✓ ", Theme.Good, ToastKind.Good);

    public Task PrintWarning(string message) =>
        Print("warning", message, WarningMark, Theme.Warning, ToastKind.Warning);

    public Task PrintError(string message) =>
        Print("error", message, "✗ ", Theme.Bad, ToastKind.Bad);

    public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
      RunTrace.Write("progress", message);
      ProgressItem item = new(message);
      app.Post(() => app._operation?.Progress.Add(item));
      await action(new Reporter(app, item));
      app.Post(() => item.Done = true);
    }

    public Task<string> Ask(string question) =>
        app.AskInDialog(question, () => app._operation?.Cancel.Cancel());

    private Task Print(string category, string message, string mark, Style style, ToastKind kind) {
      RunTrace.Write(category, message);
      app.Post(() => {
        if (category == "warning" && app._operation is { } operation) {
          operation.Warned = true;
        }

        app.AddMessage(message, mark, style, kind);
      });
      return Task.CompletedTask;
    }

    private sealed class Reporter(App app, ProgressItem item) : IProgress<double> {
      private int _reported = -1;

      public void Report(double value) {
        // Whole percents only: a download reports far more often than the screen changes.
        int percent = (int)Math.Clamp(value, 0, 100);
        if (Interlocked.Exchange(ref _reported, percent) != percent) {
          app.Post(() => item.Percent = percent);
        }
      }
    }
  }
}
