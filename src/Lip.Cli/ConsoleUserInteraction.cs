using Lip.Core.Infrastructure;
using Spectre.Console;

namespace Lip.Cli;

public class ConsoleUserInteraction : IUserInteraction {
  private static readonly IAnsiConsole _console = AnsiConsole.Create(new() {
    Out = new AnsiConsoleOutput(Console.Error)
  });

  public async Task PrintInfo(string message) {
    _console.WriteLine(message);
  }

  public async Task PrintSuccess(string message) {
    _console.MarkupLine($"[green]✓ {Markup.Escape(message)}[/]");
  }

  public async Task PrintWarning(string message) {
    _console.MarkupLine($"[#FFA500]⚠[/] [yellow]{Markup.Escape(message)}[/]");
  }

  public async Task PrintError(string message) {
    _console.MarkupLine($"[bold red]✗ {Markup.Escape(message)}[/]");
  }

  public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
    Progress progress = _console.Progress();

    await progress.StartAsync(async ctx => {
      ProgressTask task = ctx.AddTask(Markup.Escape(message));

      await action(task);
    });
  }
}
