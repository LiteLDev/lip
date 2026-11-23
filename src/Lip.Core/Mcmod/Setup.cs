using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Mcmod;

/// The build of a game (protocol/lip-setup.md, "Terms").
public enum GameBuild {
  Windows,
  Linux,
}

/// What lip setup sets up (protocol/lip-setup.md): the game version, the build, and the loader:
/// none when Loader is false; otherwise LeviLamina of LoaderVersion, or, when that is null, of the
/// highest version that fits the game.
public sealed record SetupRequest(
    SemVersion Game, GameBuild Build, bool Loader, SemVersion? LoaderVersion = null);

/// A version of the game that the builds list names, and whether it has a build for each build
/// of the game (protocol/lip-tui.md, "Server setup").
public sealed record GameVersion(SemVersion Version, bool Windows, bool Linux) {
  /// Whether the version has a build of the game of the build.
  public bool Has(GameBuild build) => build == GameBuild.Linux ? Linux : Windows;
}

/// The game and the loader of a workspace (protocol/lip-setup.md, "Terms").
public static class GameSetup {
  /// The loader source.
  public const string LoaderRepository = "github.com/LiteLDev/LeviLamina";

  /// The license of the game (protocol/lip-setup.md, "The license").
  public const string EulaUrl = "https://www.minecraft.net/eula";

  public const string PrivacyPolicyUrl = "https://go.microsoft.com/fwlink/?LinkId=521839";

  /// How a dry run says that the setup would ask the user to accept the license.
  public const string EulaDryRunMessage = "Dry run: would ask you to accept the Minecraft EULA";

  /// The game: the installed package of the server source, whatever its variant.
  public static LockedPackage? Game(LockFile? lockFile) =>
      lockFile?.Packages.FirstOrDefault(p => BedrockServer.IsRepository(p.Id));

  /// The loader: the installed package of the loader source.
  public static LockedPackage? Loader(LockFile? lockFile) =>
      lockFile?.Packages.FirstOrDefault(p => IsLoaderRepository(p.Id));

  /// The build of a game: linux for the variant "linux", windows otherwise.
  public static GameBuild BuildOf(LockedPackage game) =>
      game.Variant == BedrockServer.LinuxVariant ? GameBuild.Linux : GameBuild.Windows;

  /// The name of a build, as --build takes it.
  public static string Name(GameBuild build) => build == GameBuild.Linux ? "linux" : "windows";

  public static bool IsLoaderRepository(string repository) =>
      PackageNames.AreEqual(repository, LoaderRepository);

  /// The source of the game request (protocol/lip-setup.md, "Setting up"); without a version,
  /// the same source without its ref.
  public static Ref GameSource(GameBuild build, SemVersion? version) => Ref.Parse(
      BedrockServer.Repository
          + (build == GameBuild.Linux ? $"#{BedrockServer.LinuxVariant}" : "")
          + (version is null ? "" : $"@{version}"),
      allowFile: false);

  /// The source of the loader request; without a version, the loader source.
  public static Ref LoaderSource(SemVersion? version) =>
      Ref.Parse(LoaderRepository + (version is null ? "" : $"@{version}"), allowFile: false);

  /// Whether a package argument names the server (protocol/lip-install.md, "Setting up").
  public static bool NamesServer(Ref source) => BedrockServer.IsServer(source);

  /// Whether a package argument names the loader: the loader source without a variant, and
  /// without a ref or with a version (protocol/lip-install.md, "Setting up").
  public static bool NamesLoader(Ref source) =>
      source.Kind == RefKind.Spec && IsLoaderRepository(source.Target) && source.Variant == ""
      && source.GitRef is null or { Kind: GitRefKind.Version };

  /// The range of the dependency of a package of the loader on the server source, or null when
  /// it has none.
  public static VersionRange? GameRange(Candidate loader) =>
      loader.Package.Entry.Dependencies
          .FirstOrDefault(d => d.Source.Kind == RefKind.Spec
              && BedrockServer.IsRepository(d.Source.Target))
          ?.Range;

  /// Whether a package of the loader fits a game version (protocol/lip-setup.md, "Terms").
  public static bool Fits(Candidate loader, SemVersion game) =>
      GameRange(loader)?.IsSatisfiedBy(game) ?? false;

  /// Whether an answer to a question of lip setup is yes.
  public static bool IsYes(string? answer) =>
      answer?.Trim().ToLowerInvariant() is "y" or "yes";
}

/// lip setup (protocol/lip-setup.md), and lip install carried out as it
/// (protocol/lip-install.md, "Setting up").
public partial class McmodService {
  /// lip setup (protocol/lip-setup.md).
  public async Task<OperationResult> Setup(SetupRequest request, OperationOptions options) {
    using WorkspaceMutex? mutex = await BeginOperation(options);
    Workspace workspace = await OpenWorkspace(options);
    return await SetUp(workspace, request, [], options);
  }

  /// The versions of the game that the builds list names, as the interface offers them for a new
  /// server (protocol/lip-tui.md, "Server setup"): the versions that are not of one build only,
  /// from highest to lowest, then the others, from highest to lowest. Throws
  /// ArchiveUnavailableException when the builds list cannot be obtained.
  public async Task<IReadOnlyList<GameVersion>> GameVersions() {
    BedrockServer server = await CreateResolver().Server();
    return [
      .. server.ReleaseVersions.Concat(server.Versions.Where(v => v.IsPrerelease))
          .Select(v => new GameVersion(
              v, server.HasBuild(GameBuild.Windows, v), server.HasBuild(GameBuild.Linux, v))),
    ];
  }

  /// The versions of LeviLamina that fit a game version, from highest to lowest
  /// (protocol/lip-tui.md, "Server setup"): the candidates of the loader source that fit it, searched
  /// from the first, as a setup searches for its loader (protocol/lip-setup.md, "The loader"),
  /// stopping at a LeviLamina for older games only, and at the first one that does not fit after
  /// one that does.
  public async Task<IReadOnlyList<SemVersion>> LoaderVersions(
      SemVersion game, CancellationToken cancellationToken = default) {
    Resolver resolver = CreateResolver();
    BedrockServer server = await resolver.Server();
    List<SemVersion> fitting = [];
    await foreach (Candidate candidate in resolver.ResolveLazy(
        GameSetup.LoaderSource(null), null, DetermineEnvironment(null))) {
      cancellationToken.ThrowIfCancellationRequested();
      VersionRange? range = GameSetup.GameRange(candidate);
      if (range is not null && range.IsSatisfiedBy(game)) {
        fitting.Add(candidate.Package.Version);
      } else if (fitting.Count > 0 || ForOlderGamesOnly(server, range, game)) {
        break;
      }
    }

    return fitting;
  }

  /// Whether a LeviLamina whose dependency on the server source has the range is for games older
  /// than the game only: some version of the server lower than the game satisfies the range, and
  /// none at least as high.
  private static bool ForOlderGamesOnly(
      BedrockServer server, VersionRange? range, SemVersion game) =>
      range is not null
      && server.Versions.Any(v => v.ComparePrecedenceTo(game) < 0 && range.IsSatisfiedBy(v))
      && !server.Versions.Any(v => v.ComparePrecedenceTo(game) >= 0 && range.IsSatisfiedBy(v));

  /// The setup that lip install with arguments naming the server or the loader is carried out as
  /// (protocol/lip-install.md, "Setting up").
  private async Task<SetupRequest> SetupFromArguments(
      Workspace workspace, List<(string Argument, PackageRequest Request)> requests) {
    List<(string Argument, Ref Source)> servers = [
      .. requests.Where(r => GameSetup.NamesServer(r.Request.Ref))
          .Select(r => (r.Argument, r.Request.Ref)),
    ];
    List<(string Argument, Ref Source)> loaders = [
      .. requests.Where(r => GameSetup.NamesLoader(r.Request.Ref))
          .Select(r => (r.Argument, r.Request.Ref)),
    ];
    if (servers.Count > 1 || loaders.Count > 1) {
      List<(string Argument, Ref Source)> twice = servers.Count > 1 ? servers : loaders;
      throw new InvalidOperationException(
          $"The arguments '{twice[0].Argument}' and '{twice[1].Argument}' both name the"
          + (servers.Count > 1 ? " game" : " loader") + " of the server, which a workspace has"
          + " once. Name it once.");
    }

    (string Argument, Ref Source)? server = servers.Count == 1 ? servers[0] : null;
    (string Argument, Ref Source)? loader = loaders.Count == 1 ? loaders[0] : null;
    GameBuild build = GameBuild.Windows;
    SemVersion? game = null;
    if (server is (string serverArgument, Ref serverSource)) {
      if (serverSource.Variant is not ("" or "windows" or BedrockServer.LinuxVariant)) {
        throw new InvalidOperationException(
            $"'{serverArgument}' names the variant '{serverSource.Variant}' of the server, which"
            + $" has none: its variants are {BedrockServer.Repository} (the Windows server, also"
            + $" #windows) and {BedrockServer.Repository}#{BedrockServer.LinuxVariant}.");
      }

      build = serverSource.Variant == BedrockServer.LinuxVariant
          ? GameBuild.Linux
          : GameBuild.Windows;
      if (serverSource.GitRef is { } gitRef) {
        if (gitRef.Kind != GitRefKind.Version) {
          throw new InvalidOperationException(
              $"'{serverArgument}': lip installs the server by version only, e.g."
              + $" '{BedrockServer.Repository}@1.21.0', not by branch, tag or commit.");
        }

        game = SemVersion.Parse(gitRef.Name, SemVersionStyles.Strict);
      }
    }

    if (loader is not (string loaderArgument, Ref loaderSource)) {
      game ??= (await FirstCandidate(
          workspace, server!.Value.Argument,
          new PackageRequest(GameSource(build, null), null))).Package.Version;
      return new SetupRequest(game, build, Loader: false);
    }

    SemVersion? loaderVersion = loaderSource.GitRef is { } loaderRef
        ? SemVersion.Parse(loaderRef.Name, SemVersionStyles.Strict)
        : null;
    if (game is not null) {
      // The game is given: without a version, the loader is the highest one that fits it.
      return new SetupRequest(game, build, Loader: true, loaderVersion);
    }

    // The game is the highest version that the loader allows.
    Candidate candidate = await FirstCandidate(
        workspace, loaderArgument, new PackageRequest(loaderSource, null));
    VersionRange range = GameSetup.GameRange(candidate)
        ?? throw new InvalidOperationException(
            $"{PackageDisplay.NameAndVersion(candidate)} does not name the game it needs (it has"
            + $" no dependency on {BedrockServer.Repository}), so lip cannot tell which game to"
            + $" set up for it. Name the game too, e.g. 'lip install {loaderArgument}"
            + $" {BedrockServer.Repository}@<version>', or run 'lip setup --game <version>'.");
    Candidate? gameCandidate = await FirstOrDefault(workspace.Resolver.ResolveLazy(
        GameSource(build, null), range, workspace.Environment));
    if (gameCandidate is null) {
      throw new InvalidOperationException(
          $"{PackageDisplay.NameAndVersion(candidate)} needs the game {range}, which the"
          + $" server {BedrockServer.Repository} does not have"
          + (build == GameBuild.Linux ? " for Linux" : "") + " (yet). Name an older LeviLamina,"
          + $" e.g. 'lip install LeviLamina@<version>', or set up another game with 'lip setup"
          + " --game <version>'.");
    }

    return new SetupRequest(
        gameCandidate.Package.Version, build, Loader: true, candidate.Package.Version);
  }

  /// Carries out a setup (protocol/lip-setup.md, "Setting up") with the requests of the other
  /// package arguments of lip install, if any.
  private async Task<OperationResult> SetUp(
      Workspace workspace,
      SetupRequest request,
      List<(string Argument, PackageRequest Request)> others,
      OperationOptions options) {
    if (request.Build == GameBuild.Linux && request.Loader) {
      throw new InvalidOperationException(
          "LeviLamina has no Linux build, so the Linux server cannot have it. Set up the Windows"
          + " server (--build windows, which lip runs through Wine off Windows), or the Linux"
          + " server without a loader (--no-loader).");
    }

    Ref gameSource = GameSource(request.Build, request.Game);
    Candidate game = await FirstCandidate(
        workspace, PackageDisplay.Name(gameSource), new PackageRequest(gameSource, null));
    Candidate? loader = request.Loader ? await FindLoader(workspace, request) : null;
    List<PackageRequest> setupRequests = [new PackageRequest(gameSource, null)];
    if (loader is not null) {
      setupRequests.Add(
          new PackageRequest(GameSetup.LoaderSource(loader.Package.Version), null));
    }

    await _ui.PrintInfo($"Setting up the game {game.Package.Version}"
        + $" ({GameSetup.Name(request.Build)})"
        + (loader is null ? " without a loader." : $" with LeviLamina {loader.Package.Version}."));

    (_, Dictionary<string, Candidate> pinnedAs, List<string> othersUnpinned) =
        await InstallRequests(workspace, others);
    List<PackageRequest> requests = [.. setupRequests, .. others.Select(o => o.Request)];

    // The game and the loader the workspace had are chosen again; every other explicitly
    // installed package is kept.
    List<string> previous = GameAndLoader(workspace);
    List<LockedPackage> kept = [
      .. workspace.Lock.Packages.Where(
          p => p.Explicit && !previous.Any(i => SameIdentity(i, p.Identity))),
    ];
    List<string> unpinned = [.. previous, .. othersUnpinned];

    Plan plan;
    List<LockedPackage> leftOut = [];
    try {
      plan = await MakePlan(
          workspace,
          requests,
          unpinned: unpinned,
          explicitIdentities: [.. kept.Select(p => p.Identity)],
          pinnedAs: pinnedAs);
      RequireNoLoader(plan, loader);
    }
    catch (DependencyResolutionException first) {
      RunTrace.Write("resolve", "With the kept packages as they are: " + first);
      // The kept packages may change version too, by their sources; a kept package that does not
      // work with the game and the loader alone is left out.
      List<(LockedPackage Package, PackageRequest Request)> keptRequests = [
        .. kept.Select(p => (p, new PackageRequest(ParseRecordedSource(p), null))),
      ];
      foreach ((LockedPackage package, PackageRequest keptRequest) in keptRequests) {
        try {
          RequireNoLoader(await MakePlan(
              workspace,
              [.. setupRequests, keptRequest],
              unpinned: unpinned,
              explicitIdentities: [],
              preferDependencyPackages: true), loader);
        }
        catch (Exception ex) when (ex is DependencyResolutionException
            or InvalidOperationException or ArchiveUnavailableException
            or McmodInstallException) {
          // Including a package whose source cannot be resolved any more, e.g. a local file
          // that was deleted.
          RunTrace.Write("resolve", $"Leaving out {package.Identity}: {ex}");
          leftOut.Add(package);
        }
      }

      try {
        plan = await MakePlan(
            workspace,
            [
              .. requests,
              .. keptRequests.Where(k => !leftOut.Contains(k.Package)).Select(k => k.Request),
            ],
            unpinned: unpinned,
            explicitIdentities: [],
            preferDependencyPackages: true);
        RequireNoLoader(plan, loader);
      }
      catch (DependencyResolutionException ex) {
        throw new DependencyResolutionException(
            ex.Message, ex.InnerException, ex.Reasons,
            [
              .. ex.Hints,
              "The packages you installed do not work together with this game and loader. Choose"
                  + " another game or loader, or uninstall some of the packages first.",
            ]);
      }
    }

    ChangeSet changes = await Changes(workspace, options, plan);
    if (leftOut.Count > 0) {
      await ConfirmLeftOut(leftOut, game, loader, options);
    }

    if (changes.Changes.OfType<InstallChange>().Any(
        c => BedrockServer.IsServer(c.Candidate.Source))) {
      await AcceptLicense(options);
    }

    return await Execute(
        workspace, options, changes, prepareSystem: request.Build == GameBuild.Windows);
  }

  /// The loader of a setup (protocol/lip-setup.md, "The loader"): LeviLamina of the version
  /// asked for, which must fit the game, or else the first one that does, searched from the
  /// highest version; the search stops at a LeviLamina for older games only.
  private async Task<Candidate> FindLoader(Workspace workspace, SetupRequest request) {
    SemVersion game = request.Game;
    if (request.LoaderVersion is { } version) {
      Ref source = GameSetup.LoaderSource(version);
      Candidate candidate = await FirstCandidate(
          workspace, PackageDisplay.Name(source), new PackageRequest(source, null));
      if (!GameSetup.Fits(candidate, game)) {
        throw new InvalidOperationException(
            $"LeviLamina {version} does not support the game {game}: it needs the game "
            + (GameSetup.GameRange(candidate)?.ToString() ?? "(none named)") + ". Set up the"
            + " game it needs, or leave out the version of the loader (--loader levilamina) to"
            + $" get the newest LeviLamina for the game {game}.");
      }

      return candidate;
    }

    await _ui.PrintInfo($"Finding the newest LeviLamina for the game {game}...");
    BedrockServer server = await workspace.Resolver.Server();
    List<(SemVersion Loader, VersionRange? Range)> tried = [];
    await foreach (Candidate candidate in workspace.Resolver.ResolveLazy(
        GameSetup.LoaderSource(null), null, workspace.Environment)) {
      VersionRange? range = GameSetup.GameRange(candidate);
      if (range is not null && range.IsSatisfiedBy(game)) {
        return candidate;
      }

      tried.Add((candidate.Package.Version, range));
      // The LeviLamina versions from here on are for older games.
      if (ForOlderGamesOnly(server, range, game)) {
        break;
      }
    }

    List<string> games = [
      .. tried.Select(t => t.Range is null
              ? null
              : server.ReleaseVersions.FirstOrDefault(t.Range.IsSatisfiedBy))
          .OfType<SemVersion>().Select(v => v.ToString()).Distinct().Take(5),
    ];
    throw new InvalidOperationException(
        $"No version of LeviLamina supports the game {game}."
        + (games.Count == 0
            ? ""
            : $" LeviLamina supports the games {string.Join(", ", games)}"
              + (tried.Count > 0 ? " among others" : "") + "; set up one of them, e.g. 'lip setup"
              + $" --game {games[0]}'.")
        + " To run the game without a loader, use --no-loader.");
  }

  /// A setup without a loader holds no package of the loader source (protocol/lip-setup.md,
  /// "Setting up").
  private static void RequireNoLoader(Plan plan, Candidate? loader) {
    if (loader is null && plan.Members.FirstOrDefault(
        c => GameSetup.IsLoaderRepository(c.Package.Id)) is { } needed) {
      throw new DependencyResolutionException(
          $"The packages need {PackageDisplay.NameAndVersion(needed)}, but the setup has no"
          + " loader.");
    }
  }

  /// Names the left out packages, and asks the user whether to uninstall them, unless the user
  /// confirmed the plan already (protocol/lip-setup.md, "Setting up").
  private async Task ConfirmLeftOut(
      List<LockedPackage> leftOut, Candidate game, Candidate? loader, OperationOptions options) {
    string setup = $"the game {game.Package.Version}"
        + (loader is null ? " without a loader" : $" with LeviLamina {loader.Package.Version}");
    string names = string.Join(", ", leftOut.Select(p => $"{PackageDisplay.Name(p)} {p.Version}"));
    if (options.DryRun) {
      await _ui.PrintWarning(
          $"Dry run: these packages have no version for {setup}, so the setup would uninstall"
          + $" them, after asking you to confirm it: {names}");
      return;
    }

    await _ui.PrintWarning(
        $"These packages have no version for {setup}, so the setup uninstalls them: {names}");
    if (options.Confirmed) {
      return;
    }

    string answer;
    try {
      answer = await _ui.Ask("yes to uninstall them, or anything else to stop");
    }
    catch (CannotAskException ex) {
      throw new InvalidOperationException(
          $"lip setup would uninstall {names}, which have no version for {setup}, but cannot ask"
          + " you to confirm it here. Uninstall them first with 'lip uninstall "
          + string.Join(' ', leftOut.Select(PackageDisplay.Name)) + "', then set up again.",
          ex);
    }

    if (!GameSetup.IsYes(answer)) {
      throw new InvalidOperationException(
          "You did not confirm uninstalling the packages, so lip did not change the workspace.");
    }
  }

  /// Asks the user to accept the license of the game, unless they accepted it with --accept-eula
  /// (protocol/lip-setup.md, "The license"). A dry run only says it would ask.
  private async Task AcceptLicense(OperationOptions options) {
    if (options.AcceptEula) {
      return;
    }

    if (options.DryRun) {
      await _ui.PrintInfo(GameSetup.EulaDryRunMessage
          + $" ({GameSetup.EulaUrl}) and the Privacy Policy ({GameSetup.PrivacyPolicyUrl}),"
          + " unless you give --accept-eula.");
      return;
    }

    await _ui.PrintInfo(
        "The Minecraft server is available under the Minecraft End User License Agreement"
        + $" ({GameSetup.EulaUrl}) and the Microsoft Privacy Policy"
        + $" ({GameSetup.PrivacyPolicyUrl}). Read them before you go on.");
    string answer;
    try {
      answer = await _ui.Ask("yes to accept the Minecraft EULA and the Privacy Policy");
    }
    catch (CannotAskException ex) {
      throw new InvalidOperationException(
          "Installing the Minecraft server needs you to accept the Minecraft EULA and the"
          + " Privacy Policy named above, but lip cannot ask you here. If you accept them, run"
          + " the command again with --accept-eula.",
          ex);
    }

    if (!GameSetup.IsYes(answer)) {
      throw new InvalidOperationException(
          "You did not accept the Minecraft EULA, so lip did not change the workspace.");
    }
  }

  /// The identities of the game and the loader of a workspace, which only a setup changes.
  private static List<string> GameAndLoader(Workspace workspace) => [
    .. new[] { GameSetup.Game(workspace.Lock), GameSetup.Loader(workspace.Lock) }
        .OfType<LockedPackage>().Select(p => p.Identity),
  ];

  /// Fails when an install or update of the arguments other than a setup would install the
  /// loader in a workspace without one, or, in a workspace without a game, a mod or a package of
  /// the server or the loader (protocol/lip-install.md, "The game and the loader",
  /// protocol/lip-update.md).
  private static void CheckGameAndLoader(
      Workspace workspace, IReadOnlyList<string> arguments, Plan plan) {
    List<Candidate> installed = [
      .. plan.Members.Where(m => !workspace.Lock.Packages.Any(p => IsSamePackage(p, m))),
    ];
    if (GameSetup.Game(workspace.Lock) is not { } game) {
      // The loader or a mod that needs the server says more than the server does.
      foreach (Candidate member in installed.OrderBy(
          m => BedrockServer.IsRepository(m.Package.Id))) {
        RequireGame(arguments, member);
      }

      return;
    }

    if (GameSetup.Loader(workspace.Lock) is null && installed.FirstOrDefault(
        m => GameSetup.IsLoaderRepository(m.Package.Id)) is { } loader) {
      throw new InvalidOperationException(
          (arguments.Count == 0 ? "Updating" : $"Installing {string.Join(' ', arguments)}")
          + $" would install {PackageDisplay.NameAndVersion(loader)}, a loader, but the game"
          + $" {game.Version} of this workspace is set up without one, which only lip setup"
          + $" changes. Set it up with 'lip setup --game {game.Version} --loader levilamina',"
          + " then try again.");
    }
  }

  /// Fails when lip install of the arguments in a workspace without a game would get a mod or a
  /// package of the server or the loader, the candidate, other than by a setup
  /// (protocol/lip-install.md, "The game and the loader").
  private static void RequireGame(IReadOnlyList<string> arguments, Candidate candidate) {
    bool mod = candidate.Package.Entry.McmodAsset is not null;
    if (!mod && !BedrockServer.IsRepository(candidate.Package.Id)
        && !GameSetup.IsLoaderRepository(candidate.Package.Id)) {
      return;
    }

    string named = string.Join(' ', arguments);
    string what = PackageDisplay.NameAndVersion(candidate) + (
        mod ? ", a mod, which needs a game"
        : GameSetup.IsLoaderRepository(candidate.Package.Id) ? ", the loader, which needs a game"
        : ", the game");
    throw new InvalidOperationException(
        "This workspace has no game yet, and "
        + (arguments.Count == 0 ? "updating" : $"installing {named}") + $" would install {what}."
        + " Set up the server first, e.g. with 'lip setup --game <version>' (see 'lip setup"
        + " --help'), or with 'lip install LeviLamina', which sets up the newest game that"
        + " LeviLamina supports.");
  }

  /// A failure to satisfy the requests with the game and the loader as they are, with what to do
  /// about it (protocol/lip-install.md, "The game and the loader").
  private static DependencyResolutionException WithGameHint(
      Workspace workspace, DependencyResolutionException ex, string command) {
    LockedPackage? game = GameSetup.Game(workspace.Lock);
    if (game is null) {
      return ex;
    }

    LockedPackage? loader = GameSetup.Loader(workspace.Lock);
    string hint = $"The game of this workspace is {game.Version}"
        + (loader is null ? ", without a loader" : $", with LeviLamina {loader.Version}")
        + $"; {command} does not change them. To change them, run 'lip setup --game <version>'.";
    return new DependencyResolutionException(
        ex.Message, ex.InnerException, ex.Reasons, [.. ex.Hints, hint]);
  }

  private static Ref GameSource(GameBuild build, SemVersion? version) =>
      GameSetup.GameSource(build, version);

  private static async Task<Candidate?> FirstOrDefault(IAsyncEnumerable<Candidate> candidates) {
    await foreach (Candidate candidate in candidates) {
      return candidate;
    }

    return null;
  }
}
