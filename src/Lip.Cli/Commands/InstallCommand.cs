using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class InstallCommand(ILipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<InstallCommand.Settings> {
  private readonly ILipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : EnvironmentSettings {
    [CommandArgument(0, "<PACKAGES>")]
    [Description("The packages to install: path[#variant][@version], ./dir/, ./file.mcmod, a local path or an https:// URL")]
    public required string[] Packages { get; init; }

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }

    [CommandOption("--ignore-scripts")]
    [Description("Skip running install scripts")]
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
        settings.Platform,
        settings.Rid);
    await _userInteraction.PrintSuccess("Packages installed successfully.");
    return 0;
  }
}
