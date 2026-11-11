using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip run (protocol/lip-run.md): exits with the exit code of the server once it ran, without a
/// message or a failure report, whatever the code.
public class RunCommand(LipClient lipClient) : AsyncCommand<RunCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    using Stream input = Console.OpenStandardInput();
    return await _lipClient.Run(
        input, inputIsConsole: OperatingSystem.IsWindows() && !Console.IsInputRedirected);
  }
}
