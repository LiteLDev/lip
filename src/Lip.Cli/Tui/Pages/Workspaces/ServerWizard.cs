using System.IO.Abstractions;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
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
internal sealed class ServerWizard(AppContext context) {
  /// The directory to set up, and whether it is that of a new server, which lip creates and
  /// adds to the workspace list.
  private sealed record Target(string Directory, bool Create);

  /// Asks for a new server. In a workspace without a game, the server may go there.
  public void NewServer() {
    LipClient client = context.Client;
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
            AskDirectory("", back: null);
          } else {
            ChoosePlace("");
          }
        });
  }

  /// Asks for another game or loader of a workspace of the list.
  public void ChangeVersions(string workspace) =>
      ReadGames(new Target(workspace, Create: false), back: null);

  /// The selected workspace, which has no game, or another directory.
  private void ChoosePlace(string typed) =>
      context.Show(new ChoiceDialog(
          WorkspacesText.NewServerTitle,
          [
            new ChoiceItem(WorkspacesText.Here, context.Workspace),
            new ChoiceItem(WorkspacesText.Elsewhere, WorkspacesText.ElsewhereDetail),
          ],
          chosen => {
            if (chosen == 0) {
              ReadGames(new Target(context.Workspace, Create: true), () => ChoosePlace(typed));
            } else {
              AskDirectory(typed, () => ChoosePlace(typed));
            }
          },
          selected: typed.Length > 0 ? 1 : 0));

  private void AskDirectory(string typed, Action? back) {
    IFileSystem fileSystem = context.FileSystem;
    context.Show(new PromptDialog(
        WorkspacesText.NewServerTitle,
        [new PromptField(WorkspacesText.Folder, typed, WorkspacesText.NewFolderExample)],
        values => ReadGames(
            new Target(context.FullPath(values[0]), Create: true),
            () => AskDirectory(values[0], back)),
        back,
        values => {
          if (values[0].Length == 0) {
            return CommonText.Required;
          }

          try {
            return fileSystem.File.Exists(context.FullPath(values[0]))
                ? WorkspacesText.IsFile
                : null;
          }
          catch (Exception ex) when (ex is ArgumentException or NotSupportedException
              or IOException) {
            return WorkspacesText.InvalidPath;
          }
        }));
  }

  private void ReadGames(Target target, Action? back) {
    LipClient client = context.ClientFor(target.Directory);
    Read(
        WorkspacesText.ReadingGames,
        token => context.ServerSetup.GameVersions(client).WaitAsync(token),
        versions => {
          if (versions.Count == 0) {
            back?.Invoke();
            context.Message(Title(target), WorkspacesText.NoGames, Theme.Bad);
          } else {
            ChooseGame(target, client, versions, 0, back);
          }
        },
        back);
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

  private void ChooseGame(
      Target target,
      LipClient client,
      IReadOnlyList<GameVersion> versions,
      int selected,
      Action? back) {
    int widest = versions.Max(v => v.Version.ToString().Length);
    context.Show(new ChoiceDialog(
        WorkspacesText.GameVersion,
        [
          .. versions.Select((v, i) => new ChoiceItem(new StyledLine()
              .Add(v.Version.ToString().PadRight(widest + 2), Theme.Strong)
              .Add(string.Join(", ", Builds(v).Select(GameSetup.Name)), Theme.Muted)
              .Add(i == 0 ? "  " + WorkspacesText.Newest : "", Theme.Good)
              .Add(v.Version.IsPrerelease ? "  " + WorkspacesText.Prerelease : "", Theme.Warning))),
        ],
        chosen => ChooseBuild(target, client, versions, chosen, 0, back),
        back,
        selected));
  }

  private void ChooseBuild(
      Target target,
      LipClient client,
      IReadOnlyList<GameVersion> versions,
      int game,
      int selected,
      Action? back) {
    GameVersion version = versions[game];
    List<GameBuild> builds = [.. Builds(version)];
    context.Show(new ChoiceDialog(
        WorkspacesText.Build,
        [
          .. builds.Select(b => new ChoiceItem(
              GameSetup.Name(b),
              b == GameBuild.Linux ? WorkspacesText.LinuxDetail : WorkspacesText.WindowsDetail)),
        ],
        chosen => {
          if (builds[chosen] == GameBuild.Linux) {
            // LeviLamina has no Linux build (protocol/lip-setup.md, "Setting up").
            Finish(target, new SetupRequest(version.Version, GameBuild.Linux, Loader: false));
          } else {
            ReadLoaders(
                target, client, version.Version,
                () => ChooseBuild(target, client, versions, game, chosen, back));
          }
        },
        () => ChooseGame(target, client, versions, game, back),
        selected));
  }

  private void ReadLoaders(Target target, LipClient client, SemVersion game, Action back) =>
      Read(
          WorkspacesText.ReadingLoaders,
          token => context.ServerSetup.LoaderVersions(client, game, token),
          loaders => ChooseLoader(target, game, loaders, back),
          back);

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
          back));

  /// Creates the directory of a new server when it is missing and adds it to the workspace
  /// list; selects the directory; then sets it up. A step that fails ends the wizard.
  private void Finish(Target target, SetupRequest request) {
    void SetUp() {
      LipClient client = context.Client;
      context.PlanAndRun(
          WorkspacesText.SetUpTitle,
          (dryRun, acceptEula, token) =>
              context.ServerSetup.Setup(client, request, dryRun, acceptEula, token));
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

  /// The builds that the builds list has of a game version.
  private static IEnumerable<GameBuild> Builds(GameVersion version) =>
      ((GameBuild[])[GameBuild.Windows, GameBuild.Linux]).Where(version.Has);
}
