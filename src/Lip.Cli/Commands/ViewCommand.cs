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

    JsonText jsonText = new(json);

    AnsiConsole.Write(jsonText);

    return 0;
  }
}
