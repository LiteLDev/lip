using System.Text;
using Lip.Core.Infrastructure;
using Spectre.Console;

namespace Lip.Cli;

/// Messages on the standard error of an invocation (protocol/lip.md, "Output"), written through
/// console, and questions asked on its terminal.
public class ConsoleUserInteraction(IAnsiConsole console, Invocation invocation) : IUserInteraction {
  private readonly IAnsiConsole _console = console;
  private readonly Invocation _invocation = invocation;

  public async Task PrintInfo(string message) {
    RunTrace.Write("info", message);
    _console.WriteLine(message);
  }

  public async Task PrintSuccess(string message) {
    RunTrace.Write("success", message);
    _console.MarkupLine($"[green]✓ {Markup.Escape(message)}[/]");
  }

  public async Task PrintWarning(string message) {
    RunTrace.Write("warning", message);
    _console.MarkupLine($"[#FFA500]⚠[/] [yellow]{Markup.Escape(message)}[/]");
  }

  public async Task PrintError(string message) {
    RunTrace.Write("error", message);
    _console.MarkupLine($"[bold red]✗ {Markup.Escape(message)}[/]");
  }

  public async Task<string> Ask(string question) {
    RunTrace.Write("ask", question);
    if (_invocation.IsInputRedirected || _invocation.IsErrorRedirected) {
      throw new CannotAskException(
          $"lip needs {question}, but cannot ask for it: standard input or standard error is not"
          + " a terminal.");
    }

    string prompt = $"Enter {Markup.Escape(question)}:";
    if (_invocation.InheritsStandardStreams) {
      return _console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }

    // The terminal of a client edits and echoes the line itself; the answer is the line it
    // passes on (protocol/lip-daemon.md, "Carrying out a command").
    _console.Markup(prompt + " ");
    return await ReadLine(_invocation.OpenStandardInput())
        ?? throw new CannotAskException(
            $"lip needs {question}, but standard input ended before it was answered.");
  }

  public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
    RunTrace.Write("progress", message);
    Progress progress = _console.Progress();

    await progress.StartAsync(async ctx => {
      ProgressTask task = ctx.AddTask(Markup.Escape(message));

      await action(task);
    });
  }

  /// A line of input without its line break, or null when input ends first.
  private static async Task<string?> ReadLine(Stream input) {
    List<byte> line = [];
    byte[] buffer = new byte[1];
    while (await input.ReadAsync(buffer) > 0) {
      if (buffer[0] == '\n') {
        return Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
      }

      line.Add(buffer[0]);
    }

    return line.Count > 0 ? Encoding.UTF8.GetString([.. line]) : null;
  }
}
