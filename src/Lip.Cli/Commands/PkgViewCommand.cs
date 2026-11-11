using System.ComponentModel;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class PkgViewCommand(LipClient lipClient) : AsyncCommand<PkgViewCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<PACKAGE>")]
    [Description("The package to view, in the forms install takes")]
    public required string Package { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    string json = await _lipClient.View(settings.Package);

    // Always JSON (protocol/lip-pkg.md, "lip pkg view"), written as is: Spectre.Console would
    // wrap long lines, breaking JSON strings apart.
    Console.Out.WriteLine(json);

    return 0;
  }
}
