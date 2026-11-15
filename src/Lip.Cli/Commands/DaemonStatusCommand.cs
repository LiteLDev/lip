using System.ComponentModel;
using System.Text.Json.Nodes;
using Lip.Cli.Daemon;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip daemon status (protocol/lip-daemon.md, "Commands"): whether a daemon listens on the
/// endpoint, and its version and process ID. Carried out by this process.
public class DaemonStatusCommand(CommandOutput output) : AsyncCommand<DaemonStatusCommand.Settings> {
  private readonly CommandOutput _output = output;

  public class Settings : CommandSettings {
    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  /// A client for the commands of the command group daemon, which never starts a daemon.
  public static DaemonClient Client(DaemonEndpoint endpoint) =>
      new(endpoint, DaemonProcess.Version, DaemonProcess.Executable, () => null);

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    DaemonEndpoint endpoint = DaemonEndpoint.ForCurrentUser();
    Welcome? daemon;
    await using (Stream? connection = await endpoint.TryConnect(cancellationToken)) {
      daemon = connection is null ? null : await Client(endpoint).Greet(connection);
    }

    if (settings.Json) {
      JsonObject result = new() { ["running"] = daemon is not null };
      if (daemon is not null) {
        result["version"] = daemon.Version;
        result["pid"] = daemon.ProcessId;
      }

      _output.Out.WriteLine(result.ToJsonString(CliJsonContext.Compact));
    } else {
      _output.Out.WriteLine(daemon is null
          ? "The daemon of lip does not run."
          : $"The daemon of lip {daemon.Version} runs as process {daemon.ProcessId}, carrying out"
              + $" {daemon.Commands} commands.");
    }

    return 0;
  }
}
