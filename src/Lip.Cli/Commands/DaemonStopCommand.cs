using Lip.Cli.Daemon;
using Lip.Core.Infrastructure;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip daemon stop (protocol/lip-daemon.md, "Commands"): asks the daemon to exit and waits until
/// it has; fails when it carries out a command. Carried out by this process.
public class DaemonStopCommand(IUserInteraction userInteraction)
    : AsyncCommand<DaemonStopCommand.Settings> {
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    DaemonEndpoint endpoint = DaemonEndpoint.ForCurrentUser();
    Welcome daemon;
    await using (Stream? connection = await endpoint.TryConnect(cancellationToken)) {
      if (connection is null) {
        await _userInteraction.PrintInfo("The daemon of lip does not run.");
        return 0;
      }

      daemon = await DaemonStatusCommand.Client(endpoint).Greet(connection);
      if (!await DaemonClient.AskToExit(connection)) {
        throw new InvalidOperationException(
            $"The daemon of lip (process {daemon.ProcessId}) carries out commands or runs managed"
            + " servers, so it keeps running. Stop the managed servers first (attach with"
            + " 'lip run --managed' in their workspaces and press Ctrl+C, or stop them in the"
            + " interface), then run 'lip daemon stop' again once the commands have ended.");
      }
    }

    await DaemonClient.WaitForExit(daemon.ProcessId);
    await _userInteraction.PrintSuccess($"Stopped the daemon of lip (process {daemon.ProcessId}).");
    return 0;
  }
}
