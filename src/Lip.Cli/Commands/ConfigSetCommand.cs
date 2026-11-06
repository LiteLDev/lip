using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class ConfigSetCommand(LipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<ConfigSetCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<KEY>")]
    [Description("The configuration key to set")]
    public required string Key { get; init; }

    [CommandArgument(1, "<VALUE>")]
    [Description("The value to set")]
    public required string Value { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.ConfigSet(settings.Key, settings.Value);
    await _userInteraction.PrintSuccess($"Config key '{settings.Key}' set to '{settings.Value}'.");
    return 0;
  }
}
