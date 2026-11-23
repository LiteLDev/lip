using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui;

/// What the interface needs of lip to set up a server (protocol/lip-tui.md, "Server setup"): the
/// versions of the game, the versions of LeviLamina that fit one, and lip setup, each with the
/// client of the directory of the server. Give the three the same session, and the setup
/// reads of the network only what reading the versions did not.
public interface IServerSetup {
  Task<IReadOnlyList<GameVersion>> GameVersions(
      LipClient client, OperationSession? session = null);

  Task<IReadOnlyList<SemVersion>> LoaderVersions(
      LipClient client,
      SemVersion game,
      CancellationToken cancellationToken,
      OperationSession? session = null);

  /// lip setup, as AppContext.PlanAndRun carries it out: its plan is confirmed by the user
  /// when it is not a dry run. Returns what it changed, or would change.
  Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run);
}

/// The server setup of LipClient.
public sealed class ClientServerSetup : IServerSetup {
  public static readonly ClientServerSetup Instance = new();

  private ClientServerSetup() {
  }

  public Task<IReadOnlyList<GameVersion>> GameVersions(
      LipClient client, OperationSession? session = null) =>
      client.GameVersions(session);

  public Task<IReadOnlyList<SemVersion>> LoaderVersions(
      LipClient client,
      SemVersion game,
      CancellationToken cancellationToken,
      OperationSession? session = null) =>
      client.LoaderVersions(game, cancellationToken, session);

  public Task<OperationResult> Setup(LipClient client, SetupRequest request, OperationRun run) =>
      client.Setup(
          request, run.DryRun, run.Token, run.AcceptEula, confirmed: true, run.Session);
}
