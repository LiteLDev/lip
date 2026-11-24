using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class UninstallCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<UninstallCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<PACKAGES>")]
    [Description("The packages to uninstall: a package name, or the path[[#variant]] it was installed from")]
    public required string[] Packages { get; init; }

    [CommandOption("--accept-rosetta-license")]
    [Description("Accept the license of Rosetta 2 without being asked, when lip installs it"
        + " on a Mac with Apple silicon to run the server")]
    public bool AcceptRosettaLicense { get; init; }

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Uninstall(
        settings.Packages,
        settings.DryRun,
        cancellationToken,
        acceptRosettaLicense: settings.AcceptRosettaLicense);
    if (settings.DryRun) {
      await _userInteraction.PrintInfo("Dry run: nothing was changed.");
    } else {
      await _userInteraction.PrintSuccess("Packages uninstalled successfully.");
    }
    return 0;
  }
}
