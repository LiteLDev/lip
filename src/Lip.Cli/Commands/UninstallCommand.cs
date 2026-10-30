using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class UninstallCommand(ILipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<UninstallCommand.Settings> {
  private readonly ILipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : EnvironmentSettings {
    [CommandArgument(0, "<PACKAGES>")]
    [Description("The packages to uninstall: a package name, or the path[[#variant]] it was installed from")]
    public required string[] Packages { get; init; }

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }

    [CommandOption("--ignore-scripts")]
    [Description("Do not run the scripts of the packages installed or uninstalled")]
    public bool IgnoreScripts { get; init; }

    [CommandOption("--no-dependencies")]
    [Description("Skip removing dependencies")]
    public bool NoDependencies { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Uninstall(
        settings.Packages,
        settings.DryRun,
        settings.IgnoreScripts,
        settings.NoDependencies,
        settings.Rid, cancellationToken);
    await _userInteraction.PrintSuccess("Packages uninstalled successfully.");
    return 0;
  }
}
