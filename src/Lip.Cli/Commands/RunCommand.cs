using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip run (protocol/lip-run.md): exits with the exit code of the server once it ran, without a
/// message or a failure report, whatever the code. The server reads the standard input of the
/// invocation (Invocation).
public class RunCommand(LipClient lipClient) : AsyncCommand<RunCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    Invocation invocation = Invocation.Current;
    using Stream input = invocation.OpenStandardInput();
    return await _lipClient.Run(input, invocation.InputIsConsole);
  }
}
