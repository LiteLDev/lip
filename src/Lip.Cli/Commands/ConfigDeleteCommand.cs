using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class ConfigDeleteCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<ConfigDeleteCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<KEY>")]
    [Description("The configuration key to delete")]
    public required string Key { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.ConfigDelete(settings.Key);
    await _userInteraction.PrintSuccess($"Config key '{settings.Key}' deleted.");
    return 0;
  }
}
