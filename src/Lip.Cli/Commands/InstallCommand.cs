using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class InstallCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<InstallCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : EnvironmentSettings {
    [CommandArgument(0, "<PACKAGES>")]
    [Description("The packages to install: path[[#variant]][[@version]], path[[#variant]]@branch:<name>, @tag:<name> or @rev:<commit>, an https:// URL of a .zip file, or a local .zip file or directory")]
    public required string[] Packages { get; init; }

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }

    [CommandOption("--ignore-scripts")]
    [Description("Do not run the scripts of the packages installed or uninstalled")]
    public bool IgnoreScripts { get; init; }

    [CommandOption("--no-dependencies")]
    [Description("Skip installing dependencies")]
    public bool NoDependencies { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Install(
        settings.Packages,
        settings.DryRun,
        settings.IgnoreScripts,
        settings.NoDependencies,
        settings.Platform, cancellationToken);
    await _userInteraction.PrintSuccess("Packages installed successfully.");
    return 0;
  }
}
