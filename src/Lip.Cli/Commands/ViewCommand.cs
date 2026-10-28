using System.ComponentModel;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Lip.Cli.Commands;

public class ViewCommand(ILipClient lipClient) : AsyncCommand<ViewCommand.Settings> {
  private readonly ILipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<PACKAGE>")]
    [Description("The package to view, in the forms install takes")]
    public required string Package { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    string json = await _lipClient.View(settings.Package);

    // Piped or redirected, the document is written as is: JsonText wraps long lines at the
    // console width (80 columns when redirected), breaking the JSON.
    if (Console.IsOutputRedirected) {
      Console.Out.WriteLine(json);
    } else {
      AnsiConsole.Write(new JsonText(json));
    }

    return 0;
  }
}
