using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui;

/// What the interface needs of lip to set up a new server (protocol/lip-tui.md, "New server"):
/// the versions of the game, the versions of LeviLamina that fit one, and lip setup, each with the
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

/// The new server (protocol/lip-tui.md, "New server"): n asks for a directory, then for the game
/// version, the build and the loader; then lip creates the directory, adds it to the workspace
/// list, selects it, and sets it up as lip setup does, with the plan and the license of the
/// game asked in the interface.
public sealed partial class TuiApp {
  /// Whether the selected workspace is known to have no game (protocol/lip-setup.md, "Terms").
  private bool HasNoGame =>
      _installedLoaded && _installedError is null && !_installed.Any(
          p => BedrockServer.IsRepository(p.Identity.Split('#')[0]));

  private void NewServer() {
    if (ConfigDirty) {
      _tab = ConfigTab;
      LeaveConfigFile(NewServer);
      return;
    }

    // In a workspace without a game, the server goes there unless the user names another
    // directory.
    Push(new PromptDialog(
        this, "New server",
        [
          new PromptField(
              "The directory of the server, absolute or relative to the selected workspace; lip"
                  + " creates it when it is missing",
              HasNoGame ? _workspace : "", "my-server"),
        ],
        values => {
          if (values[0].Length == 0) {
            return;
          }

          string directory = FullPath(values[0]);
          if (_workspaces.FileSystem.File.Exists(directory)) {
            _status = new StyledLine($"{directory} is a file, not a directory.", Theme.Bad);
            return;
          }

          ChooseGame(directory);
        }));
  }

  private void ChooseGame(string directory) {
    LipClient client = _createClient(_interaction, _hookOutput, directory);
    IReadOnlyList<GameVersion> versions = [];
    Start(
        "Reading the versions of the game",
        async _ => versions = await _serverSetup.GameVersions(client),
        _ => {
          if (versions.Count == 0) {
            Push(new MessageDialog(
                this, "New server",
                [new StyledLine("The builds list names no version of the game.", Theme.Bad)],
                ["Close"]));
            return;
          }

          Push(new ListDialog(
              this, "Game version",
              [
                .. versions.Select((v, i) => new StyledLine()
                    .Add(v.Version.ToString().PadRight(20), Theme.Strong)
                    .Add(string.Join(", ", ((GameBuild[])[GameBuild.Windows, GameBuild.Linux])
                        .Where(v.Has).Select(GameSetup.Name)), Theme.Muted)
                    .Add(i == 0 ? "  (newest)" : "", Theme.Good)),
              ],
              chosen => ChooseBuild(directory, client, versions[chosen])));
        });
  }

  private void ChooseBuild(string directory, LipClient client, GameVersion game) {
    List<(GameBuild Build, string About)> builds = [];
    if (game.Windows) {
      builds.Add((GameBuild.Windows, "windows  the Windows server, which LeviLamina and its mods"
          + " run on" + (OperatingSystem.IsWindows() ? "" : " (through Wine)")));
    }

    if (game.Linux) {
      builds.Add((GameBuild.Linux,
          "linux    the Linux server, without a loader (Linux on x64 only)"));
    }

    Push(new ListDialog(
        this, $"Build of the game {game.Version}",
        [.. builds.Select(b => new StyledLine(b.About))],
        chosen => {
          if (builds[chosen].Build == GameBuild.Linux) {
            // LeviLamina has no Linux build (protocol/lip-setup.md, "Setting up").
            SetUpServer(
                directory, new SetupRequest(game.Version, GameBuild.Linux, Loader: false));
          } else {
            ChooseLoader(directory, client, game.Version);
          }
        }));
  }

  private void ChooseLoader(string directory, LipClient client, SemVersion game) {
    IReadOnlyList<SemVersion> loaders = [];
    Start(
        $"Finding the versions of LeviLamina for the game {game}",
        async token => loaders = await _serverSetup.LoaderVersions(client, game, token),
        _ => {
          List<StyledLine> items = [
            .. loaders.Select((v, i) => new StyledLine()
                .Add($"LeviLamina {v}", Theme.Strong)
                .Add(i == 0 ? "  (newest)" : "", Theme.Good)),
            loaders.Count == 0
                ? new StyledLine($"No loader: no version of LeviLamina supports the game {game}")
                : new StyledLine("No loader: the server without mods"),
          ];
          Push(new ListDialog(this, $"Loader for the game {game}", items, chosen =>
              SetUpServer(directory, chosen < loaders.Count
                  ? new SetupRequest(game, GameBuild.Windows, Loader: true, loaders[chosen])
                  : new SetupRequest(game, GameBuild.Windows, Loader: false))));
        },
        cancellable: true);
  }

  /// Creates the directory when it is missing, adds it to the workspace list, selects it, then
  /// sets it up.
  private void SetUpServer(string directory, SetupRequest request) {
    try {
      _workspaces.FileSystem.Directory.CreateDirectory(directory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      _status = new StyledLine($"Could not create {directory}: {ex.Message}", Theme.Bad);
      return;
    }

    // Selecting another workspace shows its packages.
    _tab = PackagesTab;
    if (!AddWorkspace(directory, out string? error)) {
      _status = new StyledLine(error!, Theme.Bad);
      return;
    }

    LipClient client = _client;
    string loader = request.Loader ? $"LeviLamina {request.LoaderVersion}" : "no loader";
    PlanAndRun(
        $"Set up the game {request.Game} ({GameSetup.Name(request.Build)}) with {loader}",
        (dryRun, acceptEula, token) =>
            _serverSetup.Setup(client, request, dryRun, acceptEula, token));
  }
}
