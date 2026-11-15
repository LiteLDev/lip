using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui;

/// What the interface needs of lip to set up a server (protocol/lip-tui.md, "New server"): the
/// versions of the game, the versions of LeviLamina that fit one, and lip setup, each with the
/// client of the directory of the server.
public interface IServerSetup {
  Task<IReadOnlyList<GameVersion>> GameVersions(LipClient client);

  Task<IReadOnlyList<SemVersion>> LoaderVersions(
      LipClient client, SemVersion game, CancellationToken cancellationToken);

  /// lip setup, whose plan the user confirmed when it is not a dry run.
  Task Setup(
      LipClient client,
      SetupRequest request,
      bool dryRun,
      bool acceptEula,
      CancellationToken cancellationToken);
}

/// The server setup of LipClient.
public sealed class ClientServerSetup : IServerSetup {
  public static readonly ClientServerSetup Instance = new();

  private ClientServerSetup() {
  }

  public Task<IReadOnlyList<GameVersion>> GameVersions(LipClient client) =>
      client.GameVersions();

  public Task<IReadOnlyList<SemVersion>> LoaderVersions(
      LipClient client, SemVersion game, CancellationToken cancellationToken) =>
      client.LoaderVersions(game, cancellationToken);

  public Task Setup(
      LipClient client,
      SetupRequest request,
      bool dryRun,
      bool acceptEula,
      CancellationToken cancellationToken) =>
      client.Setup(request, dryRun, cancellationToken, acceptEula, confirmed: true);
}
