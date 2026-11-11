using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class UpdateCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<UpdateCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : EnvironmentSettings {
    [CommandArgument(0, "[PACKAGES]")]
    [Description("The packages to update, in the forms install takes (default: every explicitly installed package, from the ref it was installed from)")]
    public string[] Packages { get; init; } = [];

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Update(
        settings.Packages,
        settings.DryRun,
        settings.Platform,
        cancellationToken);
    await _userInteraction.PrintSuccess("Packages updated successfully.");
    return 0;
  }
}
