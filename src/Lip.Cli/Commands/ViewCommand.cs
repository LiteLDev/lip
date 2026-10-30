using System.ComponentModel;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

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

    // Written as is: Spectre.Console would wrap long lines, breaking JSON strings apart.
    Console.Out.WriteLine(json);

    return 0;
  }
}
