using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class InstallCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<InstallCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<PACKAGES>")]
    [Description("The packages to install: path[[#variant]][[@version]], path[[#variant]]@branch:<name>, @tag:<name> or @rev:<commit>, an https:// URL of a .zip file, or a local .zip file or directory")]
    public required string[] Packages { get; init; }

    [CommandOption("--accept-eula")]
    [Description("Accept the Minecraft EULA and the Privacy Policy without being asked, when the"
        + " packages include LeviLamina or the server and so set up the game")]
    public bool AcceptEula { get; init; }

    [CommandOption("--accept-rosetta-license")]
    [Description("Accept the license of Rosetta 2 without being asked, when lip installs it"
        + " on a Mac with Apple silicon to run the server")]
    public bool AcceptRosettaLicense { get; init; }

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Install(
        settings.Packages,
        settings.DryRun,
        cancellationToken,
        settings.AcceptEula,
        acceptRosettaLicense: settings.AcceptRosettaLicense);
    if (settings.DryRun) {
      await _userInteraction.PrintInfo("Dry run: nothing was changed.");
    } else {
      await _userInteraction.PrintSuccess("Packages installed successfully.");
    }
    return 0;
  }
}
