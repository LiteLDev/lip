using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui.Pages.Workspaces;

/// The choices of a setup, one dialog after the other (protocol/lip-tui.md, "New server"): for a
/// new server the directory, then the game version, the build and the loader; then lip creates
/// the directory, adds it to the workspace list, selects it, and sets it up as lip setup does.
/// For a workspace of the list, the same choices without the directory. Esc in a step goes back
/// to the step before it, and in the first step ends the wizard, which then changed nothing;
/// what a step offers that cannot be read is said, and the wizard is back at the step before.
///
/// The build linux is offered only where lip runs the Linux server, which runsLinuxServer
/// tells. A game version without a build that can be chosen here is not listed, and the build
/// is not asked for when the game version has one such build only. Nothing is set up in a
/// directory whose server runs. Everything that a wizard reads and carries out is in one
/// session, so that nothing is read twice.
///
/// workspaceSelected is called once the wizard selected the workspace that it then sets up.
internal sealed class ServerWizard(
    AppContext context, Action workspaceSelected, Func<bool> runsLinuxServer) {
  // How long the game versions wait for what the catalogue tells of LeviLamina, once they are
  // read: the list is shown without it after that. A wizard waits so once.
  private static readonly TimeSpan _supportWait = TimeSpan.FromSeconds(3);

  /// What a workspace has of what a setup chooses, each null when it has none: the version of
  /// its game, its build, and the version of its loader.
  private sealed record Installed(string? Game, GameBuild? Build, string? Loader);

  /// One run of the wizard, from its first step to its end: the session of everything that
  /// it reads and carries out, and which game versions have a LeviLamina, which is read from
  /// the catalogue once, and is null when the catalogue cannot be read.
  private sealed class Flow {
    public OperationSession Session { get; } = new();

    public Task<LoaderSupport?>? Support { get; set; }

    /// Whether the game versions were once shown without what the catalogue tells, which
    /// was slow: they do not wait for it again.
    public volatile bool SupportWasSlow;
  }

  /// The directory to set up, and whether it is that of a new server, which lip creates and
  /// adds to the workspace list; and, for a workspace of the list, what it has now, which is
  /// what each step starts on.
  private sealed record Target(string Directory, bool Create, Flow Flow, Installed? Has = null);

  /// Why a directory cannot be set up.
  private enum Obstacle {
    None,
    ServerRuns,
    IsFile,
  }

  /// What was read for the game versions: why the directory cannot be set up, or the versions
  /// of the builds list, what the workspace has, and which versions have a LeviLamina, null
  /// when the catalogue did not tell.
  private sealed record Offer(
      Obstacle Obstacle,
      IReadOnlyList<GameVersion> Versions,
      Installed? Has,
      LoaderSupport? Support);

  /// What the steps after the directory choose from: the game versions that have a build for
  /// this system, which of them have a LeviLamina when the catalogue told, and the step
  /// before the game version.
  private sealed record Choices(
      Target Target,
      LipClient Client,
      IReadOnlyList<GameVersion> Versions,
      LoaderSupport? Support,
      Action? Back);

  /// Asks for a new server. In a workspace without a game, the server may go there.
  public void NewServer() {
    LipClient client = context.Client;
    Flow flow = new();
    context.RunOperation(
        WorkspacesText.NewServerTitle,
        async _ => {
          try {
            return (await client.ListPackages()).Any(
                p => BedrockServer.IsRepository(p.Identity.Split('#')[0]));
          }
          catch (Exception) {
            // A workspace that cannot be read is not offered.
            return true;
          }
        },
        hasGame => {
          if (hasGame) {
            AskDirectory(flow, context.Workspace, back: null);
          } else {
            ChoosePlace(flow, elsewhere: false);
          }
        });
  }

  /// Asks for another game or loader of a workspace of the list.
  public void ChangeVersions(string workspace) =>
      ReadGames(new Target(workspace, Create: false, new Flow()), back: null);

  /// The selected workspace, which has no game, or another directory: the one that the user
  /// chose before, when they come back to this step.
  private void ChoosePlace(Flow flow, bool elsewhere) =>
      context.Show(new ChoiceDialog(
          WorkspacesText.NewServerTitle,
          [
            new ChoiceItem(WorkspacesText.Here, context.Workspace),
            new ChoiceItem(WorkspacesText.Elsewhere, WorkspacesText.ElsewhereDetail),
          ],
          chosen => {
            if (chosen == 0) {
              ReadGames(
                  new Target(context.Workspace, Create: true, flow),
                  () => ChoosePlace(flow, elsewhere: false));
            } else {
              AskDirectory(flow, context.Workspace, () => ChoosePlace(flow, elsewhere: true));
            }
          },
          selected: elsewhere ? 1 : 0));

  /// Asks for the directory of the new server in a folder browser that starts at a folder: one
  /// that exists, or one that lip creates once the choices are made.
  private void AskDirectory(Flow flow, string start, Action? back) =>
      context.Show(PathPicker.Folder(
          WorkspacesText.NewServerTitle,
          start,
          // Back from the game versions, the browser shows the folder that was chosen, or the
          // nearest one above it that exists.
          folder => ReadGames(
              new Target(folder, Create: true, flow), () => AskDirectory(flow, folder, back)),
          back,
          mayNotExist: true));

  private void ReadGames(Target target, Action? back) {
    LipClient client = context.ClientFor(target.Directory);
    IServerSetup setup = context.ServerSetup;
    IFileSystem fileSystem = context.FileSystem;
    AppContext shell = context;
    Flow flow = target.Flow;
    OperationSession session = flow.Session;
    // The catalogue is read while the game versions are, once for the wizard.
    Task<LoaderSupport?> support = flow.Support ??= ReadSupport(context.Catalog);
    Read(
        WorkspacesText.ReadingGames,
        async token => {
          Obstacle obstacle = await Task.Run(
              () => shell.ServerRunsNow(target.Directory) ? Obstacle.ServerRuns
                  : target.Create && IsFile(fileSystem, target.Directory) ? Obstacle.IsFile
                  : Obstacle.None,
              token);
          if (obstacle != Obstacle.None) {
            return new Offer(obstacle, [], null, null);
          }

          IReadOnlyList<GameVersion> versions =
              await setup.GameVersions(client, session).WaitAsync(token);
          // A workspace of the list starts on what it has; a new server has nothing yet.
          Installed? has = target.Create ? null : await ReadInstalled(client).WaitAsync(token);
          LoaderSupport? known = null;
          try {
            known = await support.WaitAsync(
                flow.SupportWasSlow ? TimeSpan.Zero : _supportWait, token);
          }
          catch (TimeoutException) {
            // The list goes without what the catalogue is slow to tell.
            flow.SupportWasSlow = true;
          }

          return new Offer(obstacle, versions, has, known);
        },
        read => {
          if (read.Obstacle != Obstacle.None) {
            back?.Invoke();
            if (read.Obstacle == Obstacle.ServerRuns) {
              context.Message(Title(target), CommonText.StopServerFirst, Theme.Warning);
            } else {
              context.Message(Title(target), WorkspacesText.IsFile, Theme.Bad);
            }

            return;
          }

          // A game version that has no build for this system cannot be set up here.
          List<GameVersion> versions = [.. read.Versions.Where(v => Builds(v).Count > 0)];
          if (versions.Count == 0) {
            back?.Invoke();
            context.Message(Title(target), WorkspacesText.NoGames, Theme.Bad);
            return;
          }

          int game = versions.FindIndex(v => v.Version.ToString() == read.Has?.Game);
          ChooseGame(
              new Choices(target with { Has = read.Has }, client, versions, read.Support, back),
              Math.Max(0, game));
        },
        back);
  }

  /// Which game versions have a LeviLamina, as the catalogue tells in one request; null when
  /// the catalogue cannot be read, which is not a failure of the wizard.
  private static async Task<LoaderSupport?> ReadSupport(ModCatalogClient catalog) {
    try {
      return await Task.Run(() => catalog.GetLoaderSupport());
    }
    catch (Exception) {
      return null;
    }
  }

  /// Whether a file is where the directory of a new server would be.
  private static bool IsFile(IFileSystem fileSystem, string path) {
    try {
      return fileSystem.File.Exists(path);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException
        or IOException) {
      return false;
    }
  }

  /// The game and the loader that a workspace has. A workspace that cannot be read has none
  /// that the wizard knows of, and says why when it is set up.
  private static async Task<Installed> ReadInstalled(LipClient client) {
    try {
      IReadOnlyList<InstalledPackage> packages = await client.ListPackages();
      InstalledPackage? game = packages.FirstOrDefault(
          p => BedrockServer.IsRepository(p.Identity.Split('#')[0]));
      InstalledPackage? loader = packages.FirstOrDefault(
          p => GameSetup.IsLoaderRepository(p.Identity.Split('#')[0]));
      GameBuild? build = game is null ? null
          : game.Identity.EndsWith('#' + BedrockServer.LinuxVariant, StringComparison.Ordinal)
              ? GameBuild.Linux
              : GameBuild.Windows;
      return new Installed(game?.Version, build, loader?.Version);
    }
    catch (Exception) {
      return new Installed(null, null, null);
    }
  }

  /// Reads what a step offers, with its progress shown. When the user cancels the reading, or
  /// it fails, which is then shown, the wizard is back at the step before.
  private void Read<T>(
      string title, Func<CancellationToken, Task<T>> read, Action<T> done, Action? back) =>
      context.RunOperation<(T? Value, Exception? Failure)>(
          title,
          async token => {
            try {
              return (await read(token), null);
            }
            catch (Exception ex) {
              return (default, ex);
            }
          },
          result => {
            if (result.Failure is null) {
              done(result.Value!);
              return;
            }

            back?.Invoke();
            if (result.Failure is not OperationCanceledException) {
              context.ShowFailure(title, result.Failure);
            }
          },
          cancellable: true);

  /// The game versions, each with whether it has a LeviLamina, in a column of its own, when
  /// the catalogue told; with its build, when it has one only and others have more that can
  /// be chosen on this system; and with what it is: the newest, a preview, the one that the
  /// workspace has.
  private void ChooseGame(Choices choices, int selected) {
    IReadOnlyList<GameVersion> versions = choices.Versions;
    LoaderSupport? support = choices.Support;
    string installed = choices.Target.Has?.Game ?? "";
    int widest = versions.Max(v => v.Version.ToString().Length);
    int column = Math.Max(
        TextWidth.Of(WorkspacesText.HasLoader), TextWidth.Of(WorkspacesText.HasNoLoader));
    bool builds = versions.Any(v => Builds(v).Count > 1);
    context.Show(new ChoiceDialog(
        WorkspacesText.GameVersion,
        [
          .. versions.Select((v, i) => {
            bool? loader = support?.Supports(v.Version);
            // What follows the version, each two columns after what is before it.
            List<(string Text, Style Style)> marks = [];
            if (loader is { } has) {
              marks.Add((
                  Cells.Pad(has ? WorkspacesText.HasLoader : WorkspacesText.HasNoLoader, column),
                  has ? Theme.Good : Theme.Muted));
            }

            if (builds && Builds(v) is [GameBuild only]) {
              marks.Add((WorkspacesText.OnlyBuild(GameSetup.Name(only)), Theme.Muted));
            }

            if (i == 0) {
              marks.Add((WorkspacesText.Newest, Theme.Good));
            }

            if (v.Version.IsPrerelease) {
              marks.Add((WorkspacesText.Prerelease, Theme.Warning));
            }

            if (v.Version.ToString() == installed) {
              marks.Add((WorkspacesText.Installed, Theme.Accent));
            }

            StyledLine line = new(v.Version.ToString().PadRight(widest), Theme.Strong);
            foreach ((string text, Style style) in marks) {
              line.Add("  ").Add(text, style);
            }

            return new ChoiceItem(
                line,
                loader is null ? null
                    : loader.Value ? WorkspacesText.HasLoaderDetail
                    : WorkspacesText.HasNoLoaderDetail);
          }),
        ],
        chosen => ChooseBuild(choices, chosen, null),
        choices.Back,
        selected));
  }

  /// Asks for the build of the game version at an index, unless one only can be chosen on
  /// this system, which is then the build.
  private void ChooseBuild(Choices choices, int game, int? selected) {
    GameVersion version = choices.Versions[game];
    List<GameBuild> builds = Builds(version);
    void ToGame() => ChooseGame(choices, game);
    if (builds.Count == 1) {
      Chosen(choices, version.Version, builds[0], ToGame);
      return;
    }

    // Unless the user comes back to the build that they chose, the one that the workspace
    // has is selected when the game version has it.
    int start = selected
        ?? (choices.Target.Has?.Build is { } has ? Math.Max(0, builds.IndexOf(has)) : 0);
    context.Show(new ChoiceDialog(
        WorkspacesText.Build,
        [
          .. builds.Select(b => new ChoiceItem(
              GameSetup.Name(b),
              b == GameBuild.Linux ? WorkspacesText.LinuxDetail : WorkspacesText.WindowsDetail)),
        ],
        chosen => Chosen(
            choices, version.Version, builds[chosen], () => ChooseBuild(choices, game, chosen)),
        ToGame,
        start));
  }

  /// The game version and its build are chosen: the loader is next for the build windows.
  private void Chosen(Choices choices, SemVersion game, GameBuild build, Action back) {
    if (build == GameBuild.Linux) {
      // LeviLamina has no Linux build (protocol/lip-setup.md, "Setting up").
      Finish(choices.Target, new SetupRequest(game, GameBuild.Linux, Loader: false));
    } else {
      ReadLoaders(choices.Target, choices.Client, game, back);
    }
  }

  private void ReadLoaders(Target target, LipClient client, SemVersion game, Action back) {
    IServerSetup setup = context.ServerSetup;
    OperationSession session = target.Flow.Session;
    Read(
        WorkspacesText.ReadingLoaders,
        token => setup.LoaderVersions(client, game, token, session),
        loaders => ChooseLoader(target, game, loaders, back),
        back);
  }

  private void ChooseLoader(
      Target target, SemVersion game, IReadOnlyList<SemVersion> loaders, Action back) =>
      context.Show(new ChoiceDialog(
          WorkspacesText.Loader,
          [
            .. loaders.Select((v, i) => new ChoiceItem(
                new StyledLine()
                    .Add($"LeviLamina {v}", Theme.Strong)
                    .Add(i == 0 ? "  " + WorkspacesText.Newest : "", Theme.Good),
                WorkspacesText.LoaderDetail)),
            new ChoiceItem(
                WorkspacesText.NoLoader,
                loaders.Count == 0 ? WorkspacesText.NoLoaderFits : WorkspacesText.NoLoaderDetail),
          ],
          chosen => Finish(target, chosen < loaders.Count
              ? new SetupRequest(game, GameBuild.Windows, Loader: true, loaders[chosen])
              : new SetupRequest(game, GameBuild.Windows, Loader: false)),
          back,
          LoaderOf(target, loaders)));

  /// The row of the loaders that the dialog starts on: the loader that the workspace has, or
  /// "No loader" when it has a game of the build windows without one; otherwise the newest
  /// loader, as for a game of the build linux, which could have none.
  private static int LoaderOf(Target target, IReadOnlyList<SemVersion> loaders) {
    if (target.Has is not { Build: GameBuild.Windows } has) {
      return 0;
    }

    if (has.Loader is null) {
      return loaders.Count;
    }

    return Math.Max(0, loaders.ToList().FindIndex(v => v.ToString() == has.Loader));
  }

  /// Creates the directory of a new server when it is missing and adds it to the workspace
  /// list; selects the directory; then sets it up, in the session of the wizard. A step that
  /// fails ends the wizard, and so does a server that runs in the directory by now, which is
  /// looked for once more: nothing is created or selected then.
  private void Finish(Target target, SetupRequest request) {
    AppContext shell = context;
    context.RunOperation(
        Title(target),
        _ => Task.Run(() => shell.ServerRunsNow(target.Directory)),
        runs => {
          if (runs) {
            context.Message(Title(target), CommonText.StopServerFirst, Theme.Warning);
          } else {
            SelectAndSetUp(target, request);
          }
        });
  }

  private void SelectAndSetUp(Target target, SetupRequest request) {
    void SetUp() {
      workspaceSelected();
      LipClient client = context.Client;
      IServerSetup setup = context.ServerSetup;
      // A server that started meanwhile is seen there, and the setup is refused.
      context.PlanAndRun(
          WorkspacesText.SetUpTitle, run => setup.Setup(client, request, run),
          session: target.Flow.Session);
    }

    if (!target.Create) {
      context.SwitchWorkspace(target.Directory, SetUp);
      return;
    }

    try {
      context.FileSystem.Directory.CreateDirectory(target.Directory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException) {
      context.Message(
          Title(target), $"{WorkspacesText.CannotCreate}: {ex.Message}", Theme.Bad);
      return;
    }

    if (!context.AddWorkspace(target.Directory, out string? error, SetUp)) {
      context.Message(Title(target), error!, Theme.Bad);
    }
  }

  private static string Title(Target target) =>
      target.Create ? WorkspacesText.NewServerTitle : WorkspacesText.SetUpTitle;

  /// The builds of a game version that can be chosen on this system: those that the builds
  /// list has, linux only where lip runs the Linux server (protocol/lip-tui.md, "New server").
  private List<GameBuild> Builds(GameVersion version) => [
    .. ((GameBuild[])[GameBuild.Windows, GameBuild.Linux]).Where(
        build => version.Has(build) && (build != GameBuild.Linux || runsLinuxServer())),
  ];
}
