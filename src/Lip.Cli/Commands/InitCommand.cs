using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class InitCommand(ILipClient lipClient, IUserInteraction userInteraction) : AsyncCommand<InitCommand.Settings> {
  private readonly ILipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Init();
    await _userInteraction.PrintSuccess("Created manifest.json.");
    return 0;
  }
}
