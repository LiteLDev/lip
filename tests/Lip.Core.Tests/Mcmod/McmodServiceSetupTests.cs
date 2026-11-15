using Flurl;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod;

/// lip setup (protocol/lip-setup.md), lip install carried out as it, and the game and the loader
/// in lip install and lip update (protocol/lip-install.md, protocol/lip-update.md).
public class McmodServiceSetupTests {
  private const string Bds = BedrockServer.Repository;
  private const string LeviLamina = GameSetup.LoaderRepository;
  private const string Mod = "example.com/mod";
  private const string NewOnly = "example.com/newonly";

  private static readonly OperationOptions _accept = new(AcceptEula: true);
  private static readonly SemVersion _new = SemVersion.Parse("1.21.0");
  private static readonly SemVersion _old = SemVersion.Parse("1.20.0");

  private readonly WorkspaceHarness _ws = new();

  public McmodServiceSetupTests() {
    // Two games for Windows, one of them for Linux too.
    _ws.ServerBuilds = """
        { "server/win-x64": ["1.21.0.03", "1.20.0.01", "1.19.0.05"],
          "server/linux-x64": ["1.21.0.03"] }
        """;
    AddServer("bin-win", "1.21.0.03", "bedrock_server.exe");
    AddServer("bin-win", "1.20.0.01", "bedrock_server.exe");
    AddServer("bin-linux", "1.21.0.03", "bedrock_server");
    AddLoader("2.0.0", "1.21.0");
    AddLoader("1.0.0", "1.20.0");
    AddMod(Mod, "Mod", "1.0.0", "2.*");
    AddMod(Mod, "Mod", "0.9.0", "1.*");
    AddMod(NewOnly, "NewOnly", "1.0.0", "2.*");
  }

  private static string ServerUrl(string directory, string build) =>
      $"https://www.minecraft.net/bedrockdedicatedserver/{directory}/bedrock-server-{build}.zip";

  private void AddServer(string directory, string build, string executable) =>
      _ws.AddDownload(ServerUrl(directory, build), WorkspaceHarness.Zip(new() {
        [executable] = $"server {build}",
        ["server.properties"] = "server-name=Dedicated Server",
      }));

  /// LeviLamina of a version for a game, which writes bedrock_server_mod.exe.
  private void AddLoader(string version, string game) => _ws.AddTag(LeviLamina, version, new() {
    ["lip.toml"] = WorkspaceHarness.LipToml(LeviLamina, version,
        $$""" "deps": { "{{Bds}}": "{{game}}" }, """
        + WorkspaceHarness.SelfAsset("loader.exe", "bedrock_server_mod.exe")),
    ["loader.exe"] = $"LeviLamina {version}",
  });

  private void AddMod(string id, string name, string version, string loader) {
    string url = $"https://example.com/{name}-{version}.mcmod";
    _ws.AddDownload(url, WorkspaceHarness.Mcmod(name));
    _ws.AddTag(id, version, new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(id, version,
          $$""" "deps": { "{{LeviLamina}}": "{{loader}}" }, """
          + $$""" "assets": [ { "type": "mcmod", "urls": [ "{{url}}" ] } ]"""),
    });
  }

  private async Task<Dictionary<string, LockedPackage>> Packages() =>
      (await _ws.Lock()).Packages.ToDictionary(p => p.Identity);

  private SortedDictionary<string, string> WorkspaceSnapshot() => new(
      _ws.Snapshot().Where(e => e.Key.StartsWith(_ws.Root, StringComparison.Ordinal))
          .ToDictionary(e => e.Key, e => e.Value),
      StringComparer.Ordinal);

  /// Makes the state file record the game and the loader from their sources without a ref, as
  /// lip install did before lip setup.
  private void UnpinGameAndLoader() {
    string state = _ws.Fs.File.ReadAllText(_ws.LockPath);
    _ws.Fs.File.WriteAllText(_ws.LockPath, System.Text.RegularExpressions.Regex.Replace(
        state, "(\"ref\":\\s*\")(github\\.com/LiteLDev/(?:bds|LeviLamina))@[^\"]*\"",
        "$1$2\""));
  }

  private void Answer(string answer) =>
      _ws.Ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync(answer);

  private void CannotAsk() =>
      _ws.Ui.Setup(u => u.Ask(It.IsAny<string>()))
          .ThrowsAsync(new CannotAskException("not a terminal"));

  private void VerifyServerNotDownloaded(string url = "bedrockdedicatedserver") =>
      _ws.Downloader.Verify(
          d => d.DownloadFile(
              It.Is<Url>(u => u.ToString().Contains(url)),
              It.IsAny<System.IO.Abstractions.IFileInfo>()),
          Times.Never);

  #region lip setup

  [Fact]
  public async Task Setup_NewWorkspace_InstallsTheGameAndTheLoaderPinned() {
    // Act: the loader is the highest LeviLamina that fits the game.
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);

    // Assert
    Dictionary<string, LockedPackage> packages = await Packages();
    Assert.Equal(2, packages.Count);
    Assert.True(packages[Bds].Explicit);
    Assert.Equal($"{Bds}@1.21.0", packages[Bds].Source);
    Assert.True(packages[LeviLamina].Explicit);
    Assert.Equal($"{LeviLamina}@2.0.0", packages[LeviLamina].Source);
    Assert.Equal("server 1.21.0.03", _ws.Fs.File.ReadAllText(_ws.Path("bedrock_server.exe")));
    Assert.Equal(
        "LeviLamina 2.0.0", _ws.Fs.File.ReadAllText(_ws.Path("bedrock_server_mod.exe")));
    // The system is prepared even though no hook command runs (protocol/lip-setup.md, "Order").
    _ws.SystemPreparer.Verify(
        p => p.Prepare("win-x64", _ws.Root, It.IsAny<CancellationToken>()), Times.Once);
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Setup_LoaderSearch_SkipsLoadersOfOtherGames() {
    // Arrange: the newest LeviLamina needs a game the server does not have yet.
    AddLoader("3.0.0", "1.22.0");

    // Act
    await _ws.Service.Setup(new SetupRequest(_old, GameBuild.Windows, Loader: true), _accept);

    // Assert
    Assert.Equal("1.0.0", (await Packages())[LeviLamina].Version);
    Assert.Equal("1.20.0", (await Packages())[Bds].Version);
  }

  [Fact]
  public async Task Setup_NoLoaderFitsTheGame_FailsWithoutChanges() {
    // Arrange
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Setup(
            new SetupRequest(SemVersion.Parse("1.19.0"), GameBuild.Windows, Loader: true),
            _accept));
    Assert.Contains("No version of LeviLamina supports the game 1.19.0", ex.Message);
    Assert.Contains("1.21.0", ex.Message);
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Setup_LoaderVersionThatDoesNotFit_FailsWithoutChanges() {
    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Setup(
            new SetupRequest(_new, GameBuild.Windows, Loader: true, SemVersion.Parse("1.0.0")),
            _accept));
    Assert.Contains("1.20.0", ex.Message);
    Assert.False(_ws.Exists("bedrock_server.exe"));
  }

  [Fact]
  public async Task Setup_GameVersionTheServerDoesNotHave_FailsWithoutChanges() {
    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Setup(
            new SetupRequest(SemVersion.Parse("9.9.9"), GameBuild.Windows, Loader: false),
            _accept));
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
  }

  [Fact]
  public async Task Setup_LinuxWithLoader_FailsWithoutChanges() {
    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Setup(new SetupRequest(_new, GameBuild.Linux, Loader: true), _accept));
    Assert.Contains("Linux", ex.Message);
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
  }

  [Fact]
  public async Task Setup_LinuxWithoutLoader_InstallsTheLinuxServerWithoutPreparing() {
    // Act
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Linux, Loader: false), _accept);

    // Assert
    LockedPackage game = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal($"{Bds}#linux@1.21.0", game.Source);
    Assert.True(_ws.Exists("bedrock_server"));
    _ws.SystemPreparer.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Setup_CannotAskForTheLicense_FailsBeforeDownloadingTheServer() {
    // Arrange: protocol/lip-setup.md, "The license".
    CannotAsk();
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Setup(
            new SetupRequest(_new, GameBuild.Windows, Loader: true), new OperationOptions()));
    Assert.Contains("--accept-eula", ex.Message);
    Assert.Contains(_ws.Infos, i => i.Contains(GameSetup.EulaUrl));
    Assert.Equal(before, WorkspaceSnapshot());
    VerifyServerNotDownloaded();
  }

  [Theory]
  [InlineData("yes", true)]
  [InlineData("Y", true)]
  [InlineData("no", false)]
  [InlineData("", false)]
  public async Task Setup_AsksForTheLicense_InstallsOnlyWhenAccepted(string answer, bool accepted) {
    // Arrange
    Answer(answer);

    // Act
    Task setup = _ws.Service.Setup(
        new SetupRequest(_new, GameBuild.Windows, Loader: false), new OperationOptions());

    // Assert
    if (accepted) {
      await setup;
      Assert.True(_ws.Exists("bedrock_server.exe"));
    } else {
      await Assert.ThrowsAsync<InvalidOperationException>(() => setup);
      Assert.False(_ws.Exists("bedrock_server.exe"));
      VerifyServerNotDownloaded();
    }

    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Once);
    Assert.Contains(_ws.Infos, i => i.Contains(GameSetup.PrivacyPolicyUrl));
  }

  [Fact]
  public async Task Setup_DryRun_SaysItWouldAskAndChangesNothing() {
    // Arrange
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act
    await _ws.Service.Setup(
        new SetupRequest(_new, GameBuild.Windows, Loader: true),
        new OperationOptions(DryRun: true));

    // Assert
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
    Assert.Contains(_ws.Infos, i => i.StartsWith(GameSetup.EulaDryRunMessage));
    Assert.Contains(_ws.Infos, i => i.StartsWith("Dry run: would install bds 1.21.0"));
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Setup_SameGameAndLoader_ChangesNothingAndDoesNotAsk() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    CannotAsk();
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act
    await _ws.Service.Setup(
        new SetupRequest(_new, GameBuild.Windows, Loader: true), new OperationOptions());

    // Assert
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Setup_LoaderInstalledBeforeSetup_IsKeptAndPinned() {
    // Arrange: the game and LeviLamina installed from sources without a ref, as lip install did
    // before lip setup.
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    UnpinGameAndLoader();
    Assert.Equal(LeviLamina, (await Packages())[LeviLamina].Source);
    CannotAsk();
    SortedDictionary<string, string> before = new(
        WorkspaceSnapshot().Where(e => !e.Key.EndsWith("state.json", StringComparison.Ordinal))
            .ToDictionary(e => e.Key, e => e.Value),
        StringComparer.Ordinal);

    // Act
    await _ws.Service.Setup(
        new SetupRequest(_new, GameBuild.Windows, Loader: true), new OperationOptions());

    // Assert
    Dictionary<string, LockedPackage> packages = await Packages();
    Assert.True(packages[Bds].Explicit);
    Assert.Equal($"{Bds}@1.21.0", packages[Bds].Source);
    Assert.Equal($"{LeviLamina}@2.0.0", packages[LeviLamina].Source);
    Assert.Equal(before, new SortedDictionary<string, string>(
        WorkspaceSnapshot().Where(e => !e.Key.EndsWith("state.json", StringComparison.Ordinal))
            .ToDictionary(e => e.Key, e => e.Value),
        StringComparer.Ordinal));
  }

  [Fact]
  public async Task Setup_OtherGame_ChoosesTheModsAgain() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    await _ws.Service.Install([Mod], new OperationOptions());
    Assert.Equal("1.0.0", (await Packages())[Mod].Version);

    // Act
    await _ws.Service.Setup(new SetupRequest(_old, GameBuild.Windows, Loader: true), _accept);

    // Assert
    Dictionary<string, LockedPackage> packages = await Packages();
    Assert.Equal("1.20.0", packages[Bds].Version);
    Assert.Equal("1.0.0", packages[LeviLamina].Version);
    Assert.Equal("0.9.0", packages[Mod].Version);
    Assert.True(packages[Mod].Explicit);
    Assert.Equal("server 1.20.0.01", _ws.Fs.File.ReadAllText(_ws.Path("bedrock_server.exe")));
  }

  [Fact]
  public async Task Setup_ModWithoutVersionForTheGame_AsksAndUninstallsIt() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    await _ws.Service.Install([Mod, NewOnly], new OperationOptions());
    Answer("yes");

    // Act
    await _ws.Service.Setup(new SetupRequest(_old, GameBuild.Windows, Loader: true), _accept);

    // Assert
    Dictionary<string, LockedPackage> packages = await Packages();
    Assert.DoesNotContain(NewOnly, packages.Keys);
    Assert.Equal("0.9.0", packages[Mod].Version);
    Assert.False(_ws.Fs.Directory.Exists(_ws.Path("mods/NewOnly")));
    Assert.Contains(_ws.Warnings, w => w.Contains("example.com/newonly 1.0.0"));
  }

  [Fact]
  public async Task Setup_ModWithoutVersionForTheGame_CannotAsk_FailsNamingIt() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    await _ws.Service.Install([Mod, NewOnly], new OperationOptions());
    CannotAsk();
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Setup(
            new SetupRequest(_old, GameBuild.Windows, Loader: true), _accept));
    Assert.Contains("lip uninstall example.com/newonly", ex.Message);
    Assert.Equal(before, WorkspaceSnapshot());
    VerifyServerNotDownloaded("1.20.0.01");
  }

  [Fact]
  public async Task Setup_DryRunWithModWithoutVersionForTheGame_NamesItWithoutAsking() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    await _ws.Service.Install([NewOnly], new OperationOptions());
    CannotAsk();

    // Act
    await _ws.Service.Setup(
        new SetupRequest(_old, GameBuild.Windows, Loader: true),
        new OperationOptions(DryRun: true));

    // Assert
    Assert.Contains(_ws.Warnings, w => w.StartsWith("Dry run:") && w.Contains("newonly"));
    Assert.Contains(_ws.Infos, i => i.StartsWith("Dry run: would uninstall example.com/newonly"));
    Assert.Equal("1.0.0", (await Packages())[NewOnly].Version);
  }

  [Fact]
  public async Task Setup_NoLoaderConfirmed_UninstallsTheLoaderAndTheModsThatNeedIt() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);
    await _ws.Service.Install([Mod], new OperationOptions());

    // Act: the user confirmed the plan in the interface (protocol/lip-tui.md).
    await _ws.Service.Setup(
        new SetupRequest(_new, GameBuild.Windows, Loader: false),
        new OperationOptions(Confirmed: true));

    // Assert: the game stays as it is, so the license is not asked for again.
    Assert.Equal([Bds], (await Packages()).Keys);
    Assert.False(_ws.Exists("bedrock_server_mod.exe"));
    Assert.True(_ws.Exists("bedrock_server.exe"));
    _ws.Ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
  }

  #endregion

  #region lip install and lip update

  [Fact]
  public async Task Install_ModWithoutGame_FailsPointingToSetup() {
    // Act & Assert: protocol/lip-install.md, "The game and the loader".
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Mod], _accept));
    Assert.Contains("lip setup", ex.Message);
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
  }

  [Fact]
  public async Task Install_LoaderWithoutGame_IsCarriedOutAsSetup() {
    // Act: the game is the highest one that LeviLamina 2.0.0, the newest, allows.
    await _ws.Service.Install(["LeviLamina", Mod], _accept);

    // Assert
    Dictionary<string, LockedPackage> packages = await Packages();
    Assert.Equal($"{Bds}@1.21.0", packages[Bds].Source);
    Assert.True(packages[Bds].Explicit);
    Assert.Equal($"{LeviLamina}@2.0.0", packages[LeviLamina].Source);
    Assert.True(packages[Mod].Explicit);
    Assert.Equal("1.0.0", packages[Mod].Version);
  }

  [Fact]
  public async Task Install_LoaderVersion_SetsUpTheGameItNeeds() {
    // Act
    await _ws.Service.Install(["LeviLamina@1.0.0"], _accept);

    // Assert
    Assert.Equal("1.20.0", (await Packages())[Bds].Version);
  }

  [Fact]
  public async Task Install_ServerAndLoaderWithoutVersion_FindsTheLoaderForTheGame() {
    // Act
    await _ws.Service.Install([$"{Bds}@1.20.0", "LeviLamina"], _accept);

    // Assert
    Assert.Equal("1.0.0", (await Packages())[LeviLamina].Version);
  }

  [Fact]
  public async Task Install_LinuxServer_IsSetupWithoutLoader() {
    // Act
    await _ws.Service.Install([$"{Bds}#linux"], _accept);

    // Assert
    LockedPackage game = Assert.Single((await _ws.Lock()).Packages);
    Assert.Equal($"{Bds}#linux@1.21.0", game.Source);
  }

  [Fact]
  public async Task Install_LoaderWithoutAcceptingTheLicense_FailsWithoutChanges() {
    // Arrange
    CannotAsk();

    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install(["LeviLamina"], new OperationOptions()));
    Assert.False(_ws.Fs.File.Exists(_ws.LockPath));
    VerifyServerNotDownloaded();
  }

  [Fact]
  public async Task Install_ModForAnotherLoader_FailsNamingTheLoaderAndSetup() {
    // Arrange: a mod for LeviLamina 3 only.
    AddMod("example.com/future", "Future", "1.0.0", "3.*");
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: true), _accept);

    // Act & Assert
    DependencyResolutionException ex = await Assert.ThrowsAsync<DependencyResolutionException>(
        () => _ws.Service.Install(["example.com/future"], new OperationOptions()));
    Assert.Contains(ex.Hints, h => h.Contains("LeviLamina 2.0.0") && h.Contains("lip setup"));
    Assert.Equal("2.0.0", (await Packages())[LeviLamina].Version);
  }

  [Fact]
  public async Task Install_ModNeedingTheLoaderWithoutLoader_FailsPointingToSetup() {
    // Arrange: lip install adds no loader (protocol/lip-install.md, "The game and the loader").
    await _ws.Service.Setup(new SetupRequest(_new, GameBuild.Windows, Loader: false), _accept);
    SortedDictionary<string, string> before = WorkspaceSnapshot();

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Install([Mod], new OperationOptions()));
    Assert.Contains("lip setup --game 1.21.0 --loader levilamina", ex.Message);
    Assert.Equal(before, WorkspaceSnapshot());
  }

  [Fact]
  public async Task Update_TheLoader_FailsPointingToSetup() {
    // Arrange
    await _ws.Service.Setup(new SetupRequest(_old, GameBuild.Windows, Loader: true), _accept);

    // Act & Assert
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
        () => _ws.Service.Update(["LeviLamina"], new OperationOptions()));
    Assert.Contains("lip setup", ex.Message);
  }

  [Fact]
  public async Task Update_WithoutOperands_KeepsTheGameAndTheLoader() {
    // Arrange: LeviLamina installed from its source without a ref, which updating would move
    // to 2.0.0, for another game.
    await _ws.Service.Install([$"{Bds}@1.20.0", "LeviLamina@1.0.0"], _accept);
    UnpinGameAndLoader();
    CannotAsk();

    // Act
    await _ws.Service.Update([], new OperationOptions());

    // Assert
    Dictionary<string, LockedPackage> packages = await Packages();
    Assert.Equal("1.20.0", packages[Bds].Version);
    Assert.Equal("1.0.0", packages[LeviLamina].Version);
  }

  #endregion
}
