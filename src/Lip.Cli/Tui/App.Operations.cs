using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;

namespace Lip.Cli.Tui;

/// The commands that the interface carries out: one at a time, in the background, with their
/// progress, their plan, their result or their failure shown in dialogs.
public sealed partial class App {
  // What a dry run says of the packages that an operation would change.
  private static readonly string[] _packageChanges = [
    "Dry run: would install", "Dry run: would uninstall", "Dry run: would mark",
  ];

  private Operation? _operation;

  /// Runs a command in the background, showing its progress; then calls succeeded, or shows
  /// why it failed. One command runs at a time: false, and nothing runs, while another does.
  internal bool RunOperation(
      string title,
      Func<CancellationToken, Task> action,
      Action<Operation>? succeeded,
      bool cancellable) {
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
        } else {
          ShowFailure(title, failure, operation.Cancel.IsCancellationRequested, operation.Log);
        }
      });
    });
    return true;
  }

  /// Does work in the background without a dialog, then calls done, or failed, on the thread of
  /// the interface; neither is called when another workspace was selected meanwhile.
  internal void Background<T>(Func<Task<T>> work, Action<T> done, Action<Exception>? failed) {
    LipClient client = _client;
    _ = Task.Run(async () => {
      try {
        T result = await work();
        Post(() => {
          if (client == _client) {
            done(result);
          }
        });
      }
      catch (Exception ex) {
        Post(() => {
          if (client != _client) {
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
  /// written below the selected workspace.
  internal void ShowFailure(
      string title, Exception exception, bool cancelled, IReadOnlyList<StyledLine> log) {
    Failure failure = _describeFailure(exception, ["(interface)", title], cancelled, _workspace);
    List<StyledLine> lines = [.. log.TakeLast(200)];
    if (lines.Count > 0) {
      lines.Add(new StyledLine());
    }

    if (failure.Cancelled) {
      lines.Add(new StyledLine(CommonText.Cancelled, Theme.Bad));
    } else {
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
    }

    Show(new ConfirmDialog(
        $"{title} · {CommonText.Failed}", lines, [CommonText.Close], border: Theme.Bad,
        fromEnd: true));
  }

  /// Shows what a command told the user, once it succeeded.
  internal void ShowResult(string title, Operation operation, string done) {
    List<StyledLine> lines = [.. operation.Log, new StyledLine("✓ " + done, Theme.Good)];
    Show(new ConfirmDialog(title, lines, [CommonText.Ok], fromEnd: true));
  }

  /// A workspace operation: first as a dry run, whose messages are the plan; then, when the
  /// user applies the plan, for real, after the license of the game when the dry run says that
  /// a setup would ask for it (protocol/lip-tui.md, "Commands"). Cancelling the plan is the
  /// preview only. done is called once the operation succeeded and its result is shown.
  internal void PlanAndRun(
      string title, Func<bool, bool, CancellationToken, Task> operation, Action? done) {
    RunOperation($"{CommonText.Preview} · {title}", token => operation(true, false, token), plan => {
      // What would happen to which packages ("Dry run: would install X 1.0.0 (N files)") stands
      // out from the rest of the log, such as what was resolved and downloaded.
      List<StyledLine> lines = [
        .. plan.Log.Select(line =>
            line.PlainText.StartsWith("Dry run:", StringComparison.Ordinal)
                ? new StyledLine(line.PlainText, Theme.Strong)
                : line),
      ];
      if (!lines.Any(l => _packageChanges.Any(
          c => l.PlainText.StartsWith(c, StringComparison.Ordinal)))) {
        lines.Add(new StyledLine(CommonText.NothingWouldChange, Theme.Muted));
      }

      bool asksForLicense = plan.Log.Any(l => l.PlainText.StartsWith(
          GameSetup.EulaDryRunMessage, StringComparison.Ordinal));
      void Run(bool acceptEula) =>
          RunOperation(title, token => operation(false, acceptEula, token), result => {
            WorkspaceChanged();
            ShowResult(title, result, CommonText.Done);
            done?.Invoke();
          }, cancellable: true);

      Show(new ConfirmDialog(
          title, lines, [CommonText.Apply, CommonText.Cancel], choice => {
            if (choice != 0) {
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
                  }
                }));
          }));
    }, cancellable: true);
  }

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

  /// The messages and questions of the commands, shown in the interface.
  private sealed class Interaction(App app) : IUserInteraction {
    public Task PrintInfo(string message) => Print("info", message, "", default, ToastKind.Info);

    public Task PrintSuccess(string message) =>
        Print("success", message, "✓ ", Theme.Good, ToastKind.Good);

    public Task PrintWarning(string message) =>
        Print("warning", message, "⚠ ", Theme.Warning, ToastKind.Warning);

    public Task PrintError(string message) =>
        Print("error", message, "✗ ", Theme.Bad, ToastKind.Bad);

    public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
      RunTrace.Write("progress", message);
      ProgressItem item = new(message);
      app.Post(() => app._operation?.Progress.Add(item));
      await action(new Reporter(app, item));
      app.Post(() => item.Done = true);
    }

    public Task<string> Ask(string question) {
      RunTrace.Write("ask", question);
      TaskCompletionSource<string> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
      app.Post(() => app.Show(new PromptDialog(
          CommonText.Question, [new PromptField(question)],
          values => answer.SetResult(values[0]),
          () => {
            app._operation?.Cancel.Cancel();
            answer.SetException(new OperationCanceledException("The question was not answered."));
          })));
      return answer.Task;
    }

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
