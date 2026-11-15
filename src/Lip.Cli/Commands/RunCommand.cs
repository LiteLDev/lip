using System.ComponentModel;
using Lip.Cli.Servers;
using Lip.Cli.Workspaces;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip run (protocol/lip-run.md): exits with the exit code of the server once it ran, without a
/// message or a failure report, whatever the code. The server reads the standard input of the
/// invocation (Invocation). With --managed, the server is the managed server of the workspace in
/// the daemon (ServerSessions.Shared), which the client attaches to unless --detach is given.
public class RunCommand(LipClient lipClient) : AsyncCommand<RunCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandOption("--managed")]
    [Description("Run the server in the daemon of lip, where it keeps running when this client"
        + " goes away, or attach to the one that runs")]
    public bool Managed { get; init; }

    [CommandOption("--detach")]
    [Description("With --managed: start the server, or find it running, without attaching")]
    public bool Detach { get; init; }

    public override ValidationResult Validate() =>
        Detach && !Managed
            ? ValidationResult.Error("--detach is an option of 'lip run --managed' only.")
            : ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    Invocation invocation = Invocation.Current;
    if (settings.Managed) {
      return await RunManaged(invocation, settings.Detach, ServerSessions.Shared);
    }

    using Stream input = invocation.OpenStandardInput();
    return await _lipClient.Run(input, invocation.InputIsConsole);
  }

  /// lip run --managed (protocol/lip-run.md, "Managed servers" and "Attaching") for the
  /// invocation, with the servers of the daemon. createClient makes the client of a managed
  /// server, from its user interaction and its workspace: LipClient.Create when not given.
  public static async Task<int> RunManaged(
      Invocation invocation,
      bool detach,
      ServerSessions servers,
      Func<IUserInteraction, string, LipClient>? createClient = null) {
    if (invocation == Invocation.Process) {
      throw new InvalidOperationException(
          "lip run --managed needs the daemon of lip, which runs the managed server, but lip"
          + " carries out this command itself. Check what lip said above about the daemon, or"
          + " run the server without --managed.");
    }

    System.IO.Abstractions.FileSystem fileSystem = new();
    string workspace =
        new WorkspaceList(fileSystem, null).Normalize(invocation.WorkingDirectory);
    WorkspaceServer server = servers.For(workspace);
    Stream output = invocation.OpenStandardOutput();
    Stream error = invocation.OpenStandardError();
    ServerAttachment? attachment = detach
        ? null
        : new((bytes, toError) => {
          Stream stream = toError ? error : output;
          stream.Write(bytes);
          stream.Flush();
        });
    Task? starting = server.Start(
        interaction => createClient is not null
            ? createClient(interaction, workspace)
            : LipClient.Create(fileSystem, interaction, null, workspace),
        invocation,
        attachment);
    if (starting is not null) {
      await starting;
    } else if (attachment is not null && !server.Attach(attachment, replay: true)) {
      // The server stopped running as the client came.
      return server.ExitCode ?? 0;
    }

    if (attachment is null) {
      return 0;
    }

    TaskCompletionSource detached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    using CancellationTokenSource ended = new();
    // A client that ended already detaches, and does not ask the server to stop.
    using IDisposable detachRegistration =
        invocation.ListenForDetach(() => detached.TrySetResult());
    using IDisposable stopRegistration = invocation.ListenForStop(server.Stop);
    _ = Task.Run(() => Forward(invocation, server, ended.Token));
    try {
      if (await Task.WhenAny(attachment.Ended, detached.Task) == detached.Task) {
        RunTrace.Write("run", "The client detached from the managed server");
        server.Detach(attachment);
        // What was passed on before is written before the command ends.
        await attachment.Ended;
        return 0;
      }

      // The command fails as the lip run of the server did.
      return await attachment.Ended ?? 0;
    }
    finally {
      ended.Cancel();
    }
  }

  /// Gives the server what the client reads from its standard input, until the command ends.
  private static async Task Forward(
      Invocation invocation, WorkspaceServer server, CancellationToken ended) {
    using Stream input = invocation.OpenStandardInput();
    byte[] buffer = new byte[4096];
    try {
      while (true) {
        int read = await input.ReadAsync(buffer, ended);
        if (read == 0) {
          if (invocation.InputIsConsole) {
            continue;
          }

          return;
        }

        server.Send(buffer[..read]);
      }
    }
    catch (Exception ex)
        when (ex is IOException or ObjectDisposedException or OperationCanceledException) {
      RunTrace.Write("run", $"No more input for the managed server: {ex.Message}");
    }
  }
}
