using System.Formats.Tar;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Flurl;
using Lip.Core.Infrastructure;
using Moq;

namespace Lip.Core.Tests.Infrastructure;

/// protocol/lip-workspace.md, "System dependencies": preparing the system for a workspace, with
/// the system, the downloads and the programs faked, and the data directory on the real file
/// system.
public class SystemDependenciesTests : IDisposable {
  private const string WineUrl = "https://example.com/wine.tar.xz";
  private const string VcRedistUrl = "https://example.com/vc_redist.x64.exe";
  private const string RegInstalled =
      "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64\r\n"
      + "    Installed    REG_DWORD    0x1\r\n";

  private static readonly SystemInfo _linux = new(HostOs.Linux, Architecture.X64);
  private static readonly SystemInfo _windows = new(HostOs.Windows, Architecture.X64);
  private static readonly SystemInfo _armMac = new(HostOs.MacOS, Architecture.Arm64);
  private static readonly byte[] _installerBytes = "MZ installer"u8.ToArray();

  private readonly string _temp =
      Path.Combine(Path.GetTempPath(), "lip-sysdeps-test-" + Guid.NewGuid().ToString("N"));
  private readonly Dictionary<string, byte[]> _served = new(StringComparer.Ordinal);
  private readonly List<string> _downloaded = [];
  private readonly Mock<IFileDownloader> _downloader = new();
  private readonly Mock<IUserInteraction> _ui = new();
  private readonly FakeProbe _probe = new();
  private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);
  private byte[] _wineArchive;

  public SystemDependenciesTests() {
    Directory.CreateDirectory(_temp);
    _wineArchive = Tar(("w/bin/wine", "wine"), ("w/lib/wine.so", "lib"));
    _served[WineUrl] = _wineArchive;
    _served[VcRedistUrl] = _installerBytes;
    _downloader.Setup(d => d.DownloadFile(
            It.IsAny<Url>(), It.IsAny<IFileInfo>(), It.IsAny<Action<Url>?>()))
        .Returns((Url url, IFileInfo file, Action<Url>? validate) => {
          _downloaded.Add(url.ToString());
          File.WriteAllBytes(file.FullName, _served[url.ToString()]);
          validate?.Invoke(url);
          return Task.CompletedTask;
        });
    _ui.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    _ui.Setup(u => u.RunWithProgress(
            It.IsAny<string>(), It.IsAny<Func<IProgress<double>, Task>>()))
        .Returns((string _, Func<IProgress<double>, Task> action) =>
            action(new Progress<double>()));
  }

  public void Dispose() {
    if (Directory.Exists(_temp)) {
      Directory.Delete(_temp, recursive: true);
    }

    GC.SuppressFinalize(this);
  }

  private static CancellationToken Token => TestContext.Current.CancellationToken;

  private string Data => Path.Join(_temp, "data");

  private string Root => Path.Join(_temp, "ws");

  private WineBuild Build => new(
      "wine-test", "1.0", new PinnedFile(WineUrl, Sha256(_wineArchive)), "w/bin/wine");

  private string WineExecutable => Path.Join(Data, "wine", "wine-test", "w/bin/wine");

  private SystemDependencies Create(
      SystemInfo system, Func<Stream, Stream>? decompress = null, string? vcRedistSha = null) {
    _probe.System = system;
    SystemDependencyFiles files = new(
        Build, Build, new PinnedFile(VcRedistUrl, vcRedistSha ?? Sha256(_installerBytes)));
    return new SystemDependencies(
        new FileSystem(), _downloader.Object, _ui.Object, _probe, Data,
        _environment.GetValueOrDefault, files, decompress ?? (s => s));
  }

  private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

  /// A tar archive of the files (path, content), with the directories above them.
  private static byte[] Tar(params (string Path, string Content)[] files) {
    using MemoryStream stream = new();
    using (TarWriter writer = new(stream, TarEntryFormat.Pax, leaveOpen: true)) {
      foreach ((string path, string content) in files) {
        PaxTarEntry entry = new(TarEntryType.RegularFile, path) {
          DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
          Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
        };
        writer.WriteEntry(entry);
      }
    }

    return stream.ToArray();
  }

  [Fact]
  public async Task Prepare_NotAWindowsPlatform_DoesNothing() {
    // Act
    await Create(_linux).Prepare("linux-x64", Root, false, Token);

    // Assert
    Assert.Empty(_downloaded);
    Assert.Empty(_probe.Runs);
    Assert.False(Directory.Exists(Data));
  }

  [Fact]
  public async Task Prepare_WindowsWithTheRuntime_InstallsNothing() {
    // Arrange
    _probe.VcRuntimeInstalled = true;

    // Act
    await Create(_windows).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Empty(_downloaded);
    Assert.Empty(_probe.AdministratorRuns);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1638)]
  [InlineData(3010)]
  public async Task Prepare_WindowsWithoutTheRuntime_RunsItsInstallerAsAdministrator(int exitCode) {
    // Arrange
    _probe.AdministratorExitCode = exitCode;

    // Act
    await Create(_windows).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Equal([VcRedistUrl], _downloaded);
    (string fileName, IReadOnlyList<string> arguments, byte[] content) =
        Assert.Single(_probe.AdministratorRuns);
    Assert.Equal(_installerBytes, content);
    Assert.StartsWith(Data, fileName);
    Assert.Equal(["/install", "/quiet", "/norestart"], arguments);
    Assert.False(File.Exists(fileName));
  }

  [Fact]
  public async Task Prepare_InstallerFails_Throws() {
    // Arrange
    _probe.AdministratorExitCode = 1603;

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_windows).Prepare("win-x64", Root, false, Token));
    Assert.Contains("1603", ex.Message);
  }

  [Fact]
  public async Task Prepare_UserDoesNotAllowTheInstaller_Throws() {
    // Arrange
    _probe.AdministratorDenied = true;

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_windows).Prepare("win-x64", Root, false, Token));
    Assert.Contains("administrator", ex.Message);
  }

  [Fact]
  public async Task Prepare_InstallerShaMismatch_ThrowsWithoutRunningIt() {
    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_windows, vcRedistSha: new string('0', 64))
            .Prepare("win-x64", Root, false, Token));
    Assert.Contains("SHA-256", ex.Message);
    Assert.Empty(_probe.AdministratorRuns);
    Assert.Empty(Directory.GetFiles(Data, "*.exe"));
  }

  [Fact]
  public async Task Prepare_LinuxWithoutWine_InstallsItAndQueriesThePrefix() {
    // Arrange
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert: the build is extracted, and nothing is left beside it.
    Assert.Equal([WineUrl], _downloaded);
    Assert.Equal("wine", File.ReadAllText(WineExecutable));
    Assert.Equal("lib", File.ReadAllText(Path.Join(Data, "wine", "wine-test", "w/lib/wine.so")));
    Assert.Equal(["wine-test"], Directory.GetFileSystemEntries(Path.Join(Data, "wine"))
        .Select(Path.GetFileName));
    Assert.Empty(Directory.GetFiles(Data, "*.tmp"));
    if (!OperatingSystem.IsWindows()) {
      Assert.True(File.GetUnixFileMode(WineExecutable).HasFlag(UnixFileMode.UserExecute));
    }

    // The runtime is looked up in the registry of the prefix, through the Wine of lip.
    ProgramStartInfo query = _probe.Runs[0];
    Assert.Equal(WineExecutable, query.FileName);
    Assert.Equal("reg", query.Arguments[0]);
    Assert.Contains(
        @"HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", query.Arguments);
    Assert.Equal(Path.Join(Root, ".lip", "wine"), query.EnvironmentOverrides["WINEPREFIX"]);
    Assert.Equal("-all", query.EnvironmentOverrides["WINEDEBUG"]);
    Assert.Equal(
        WorkspaceProgram.WineDllOverrides, query.EnvironmentOverrides["WINEDLLOVERRIDES"]);
    Assert.DoesNotContain(_probe.Runs, r => r.Arguments.Contains("/install"));
  }

  [Fact]
  public async Task Prepare_LinuxWithoutTheRuntime_RunsItsInstallerInThePrefix() {
    // Arrange: the key is missing in both views until the installer has run.
    _probe.RegExitCode = 1;

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Equal(["/reg:64", "/reg:32"], _probe.Runs.Take(2).Select(r => r.Arguments[^1]));
    ProgramStartInfo install = _probe.Runs[^1];
    Assert.Equal(WineExecutable, install.FileName);
    Assert.Equal(["/install", "/quiet", "/norestart"], install.Arguments.Skip(1));
    Assert.EndsWith("vc_redist.x64.exe", install.Arguments[0]);
    Assert.Equal(Path.Join(Root, ".lip", "wine"), install.EnvironmentOverrides["WINEPREFIX"]);
    Assert.Empty(_probe.AdministratorRuns);
  }

  [Fact]
  public async Task Prepare_WinePrefixSet_QueriesThatPrefix() {
    // Arrange
    _environment["WINEPREFIX"] = "/home/u/.wine";
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.DoesNotContain("WINEPREFIX", _probe.Runs[0].EnvironmentOverrides.Keys);
  }

  [Fact]
  public async Task Prepare_AlreadyPrepared_ShortCircuits() {
    // Arrange
    _probe.RegOutput = RegInstalled;
    SystemDependencies dependencies = Create(_linux);
    await dependencies.Prepare("win-x64", Root, false, Token);
    int runs = _probe.Runs.Count;

    // Act
    await dependencies.Prepare("win-x64", Root, false, Token);

    // Assert: neither downloaded nor looked up again.
    Assert.Equal([WineUrl], _downloaded);
    Assert.Equal(runs, _probe.Runs.Count);
  }

  [Fact]
  public async Task Prepare_WineInstalledByAnotherProcess_DoesNotDownloadIt() {
    // Arrange
    _probe.RegOutput = RegInstalled;
    Directory.CreateDirectory(Path.GetDirectoryName(WineExecutable)!);
    File.WriteAllText(WineExecutable, "wine");

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Empty(_downloaded);
    Assert.Equal(WineExecutable, _probe.Runs[0].FileName);
  }

  /// The downloader sends the download of Wine, a GitHub release asset, through github_proxy
  /// (protocol/config.toml.md); what the proxy serves is checked, so that GitHub is tried when
  /// it is not the archive.
  [Fact]
  public async Task Prepare_ProxyServesAnotherWine_RejectsItAndInstallsWhatGithubServes() {
    // Arrange
    _probe.RegOutput = RegInstalled;
    _downloader.Setup(d => d.DownloadFile(
            It.IsAny<Url>(), It.IsAny<IFileInfo>(), It.IsAny<Action<Url>?>()))
        .Returns((Url url, IFileInfo file, Action<Url>? validate) => {
          File.WriteAllBytes(file.FullName, Tar(("w/bin/wine", "tampered")));
          Assert.Throws<SystemDependencyException>(
              () => validate!(Url.Parse("https://proxy.example/" + url.Path)));
          File.WriteAllBytes(file.FullName, _served[url.ToString()]);
          validate!(url);
          return Task.CompletedTask;
        });

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Equal("wine", File.ReadAllText(WineExecutable));
  }

  [Fact]
  public async Task Prepare_WineShaMismatch_ThrowsAndExtractsNothing() {
    // Arrange
    _served[WineUrl] = Tar(("w/bin/wine", "tampered"));

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Contains("SHA-256", ex.Message);
    Assert.False(Directory.Exists(Path.Join(Data, "wine")));
    Assert.Empty(Directory.GetFiles(Data, "*.tmp"));
    Assert.Empty(_probe.Runs);
  }

  [Fact]
  public async Task Prepare_ArchiveEscapingItsDirectory_ThrowsAndInstallsNothing() {
    // Arrange
    _wineArchive = Tar(("w/bin/wine", "wine"), ("../../evil", "evil"));
    _served[WineUrl] = _wineArchive;

    // Act & Assert
    await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Empty(Directory.GetFileSystemEntries(Path.Join(Data, "wine")));
    Assert.False(File.Exists(Path.Join(_temp, "evil")));
  }

  [Fact]
  public async Task Prepare_ArchiveWithSymbolicLinks_ExtractsThemInside() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs a privilege.");

    // Arrange
    using MemoryStream stream = new();
    using (TarWriter writer = new(stream, TarEntryFormat.Pax, leaveOpen: true)) {
      writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "w/bin/wine") {
        DataStream = new MemoryStream("wine"u8.ToArray()),
      });
      writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "w/bin/wineboot") {
        LinkName = "wine",
      });
    }

    _wineArchive = stream.ToArray();
    _served[WineUrl] = _wineArchive;
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Equal(
        "wine", new FileInfo(Path.Join(Path.GetDirectoryName(WineExecutable)!, "wineboot"))
            .LinkTarget);
  }

  [Fact]
  public async Task Prepare_ArchiveWritingThroughItsSymbolicLink_Throws() {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs a privilege.");

    // Arrange: w/s points at w/.. (inside), so w/s/x would land in the directory of w.
    using MemoryStream stream = new();
    using (TarWriter writer = new(stream, TarEntryFormat.Pax, leaveOpen: true)) {
      writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "w/s") { LinkName = ".." });
      writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "w/s/t") {
        LinkName = "..",
      });
    }

    _wineArchive = stream.ToArray();
    _served[WineUrl] = _wineArchive;

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Contains("symbolic link", ex.Message);
    Assert.Empty(Directory.GetFileSystemEntries(Path.Join(Data, "wine")));
  }

  [Fact]
  public async Task Prepare_ArchiveWithSymbolicLinkOutside_Throws() {
    // Arrange
    using MemoryStream stream = new();
    using (TarWriter writer = new(stream, TarEntryFormat.Pax, leaveOpen: true)) {
      writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "w/bin/wine") {
        LinkName = "../../../../etc/passwd",
      });
    }

    _wineArchive = stream.ToArray();
    _served[WineUrl] = _wineArchive;

    // Act & Assert
    await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Empty(Directory.GetFileSystemEntries(Path.Join(Data, "wine")));
  }

  [Fact]
  public async Task Prepare_XzArchive_IsDecompressed() {
    // Arrange: w/bin/wine holding "wine", as tar --format=pax and xz -9 write it.
    _wineArchive = Convert.FromBase64String(
        "/Td6WFoAAATm1rRGBMCOAoBQIQEcAAAAAAAAADBpyxDgJ/8BBl0AFwvGh5JaO6ePxyUOuA/z0OIIcS/6MuUPE"
        + "QoXIDO1DQHb1AJ9vs2EffGbDeqeFH0LuW9vUVTR/Vk+PeV6l11jEoLJfxLjXYCNOdVpn9geUKZOdQ3eiwz"
        + "tdFG9PQ0bP3s4cjvsmW4rkgHHoNaj+34Ece4RAldAaM29/hJNEXfdYzVTNgXUOXhm0vKFqpCdRPV5Xwwd"
        + "G74lkTPnTbN69KuDu19mVaK4IWT83ey1znpJCtKUe1wXE/hLSM12klMgY6nOKMEhyT941o1MXy2qWsFUIeJ"
        + "FpheF7xB2FuAMcf2uxOnmx7+Que6G/xloOi3VVHxoOp3Mqhx5/iTtwyQTfstJMpHxnEEXAAAAANRl5nL4vU"
        + "qtAAGqAoBQAAC+LfeAscRn+wIAAAAABFla");
    _served[WineUrl] = _wineArchive;
    _probe.RegOutput = RegInstalled;
    SystemDependencies dependencies = new(
        new FileSystem(), _downloader.Object, _ui.Object, _probe, Data,
        _environment.GetValueOrDefault,
        new SystemDependencyFiles(Build, Build, new PinnedFile(VcRedistUrl, "")));
    _probe.System = _linux;

    // Act
    await dependencies.Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.Equal("wine", File.ReadAllText(WineExecutable));
  }

  [Fact]
  public async Task Prepare_LinuxOnArm64_ThrowsWithoutDownloading() {
    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(new SystemInfo(HostOs.Linux, Architecture.Arm64))
            .Prepare("win-x64", Root, false, Token));
    Assert.Contains("no Wine", ex.Message);
    Assert.Empty(_downloaded);
  }

  [Theory]
  [InlineData(Architecture.Arm64, true, false)]
  [InlineData(Architecture.Arm64, true, true)]
  [InlineData(Architecture.X64, false, false)]
  [InlineData(Architecture.X64, false, true)]
  public async Task Prepare_MacOSWithRosettaOrOnX64_NeitherAsksNorInstallsRosetta(
      Architecture architecture, bool rosetta, bool accept) {
    // Arrange
    _probe.RosettaInstalled = rosetta;
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(new SystemInfo(HostOs.MacOS, architecture))
        .Prepare("win-x64", Root, accept, Token);

    // Assert
    Assert.True(File.Exists(WineExecutable));
    _ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
    Assert.DoesNotContain(_probe.Runs, IsRosettaInstaller);
  }

  [Fact]
  public async Task Prepare_ArmMacWithoutRosetta_AcceptingTheLicense_InstallsItWithoutAsking() {
    // Arrange (protocol/lip-workspace.md, "Installing Rosetta 2").
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: true, Token);

    // Assert
    _ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
    ProgramStartInfo installer = Assert.Single(_probe.Runs, IsRosettaInstaller);
    Assert.Equal(new[] { "--install-rosetta", "--agree-to-license" }, installer.Arguments);
    Assert.True(_probe.RosettaInstalled);
    Assert.True(File.Exists(WineExecutable));
  }

  [Theory]
  [InlineData("yes")]
  [InlineData(" Y ")]
  public async Task Prepare_ArmMacWithoutRosetta_UserAnswersYes_InstallsIt(string answer) {
    // Arrange
    _probe.RegOutput = RegInstalled;
    _ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync(answer);

    // Act
    await Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: false, Token);

    // Assert: the user heard of the license before the question.
    _ui.Verify(u => u.PrintInfo(It.Is<string>(m => m.Contains("license"))), Times.AtLeastOnce);
    _ui.Verify(u => u.Ask(It.Is<string>(q => q.Contains("Rosetta 2"))), Times.Once);
    Assert.Single(_probe.Runs, IsRosettaInstaller);
    Assert.True(File.Exists(WineExecutable));
  }

  [Theory]
  [InlineData("no")]
  [InlineData("")]
  public async Task Prepare_ArmMacWithoutRosetta_UserDeclines_FailsWithoutInstalling(
      string answer) {
    // Arrange
    _ui.Setup(u => u.Ask(It.IsAny<string>())).ReturnsAsync(answer);

    // Act
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: false, Token));

    // Assert
    AssertNamesTheManualInstall(ex);
    Assert.DoesNotContain(_probe.Runs, IsRosettaInstaller);
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Prepare_ArmMacWithoutRosetta_CannotAsk_FailsWithoutInstalling() {
    // Arrange: standard input or standard error is not a terminal.
    _ui.Setup(u => u.Ask(It.IsAny<string>()))
        .ThrowsAsync(new CannotAskException("not a terminal"));

    // Act
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: false, Token));

    // Assert
    AssertNamesTheManualInstall(ex);
    Assert.Contains("--accept-rosetta-license", ex.Message);
    Assert.IsType<CannotAskException>(ex.InnerException);
    Assert.DoesNotContain(_probe.Runs, IsRosettaInstaller);
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Prepare_ArmMacWithoutRosetta_InstallerFails_Fails() {
    // Arrange
    _probe.RosettaInstallerExitCode = 1;
    _probe.RosettaInstallerOutput = "Installing Rosetta 2 on this system is not supported.";

    // Act
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: true, Token));

    // Assert
    AssertNamesTheManualInstall(ex);
    Assert.Contains("exit code 1", ex.Message);
    Assert.Contains("not supported", ex.Message);
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Prepare_ArmMacWithoutRosetta_InstallerFails_SaysWhatItWroteToStandardError() {
    // Arrange
    _probe.RosettaInstallerExitCode = 1;
    _probe.RosettaInstallerOutput = "Software Update Tool\n";
    _probe.RosettaInstallerError = "softwareupdate: must be run as root";

    // Act
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: true, Token));

    // Assert
    AssertNamesTheManualInstall(ex);
    Assert.Contains("must be run as root", ex.Message);
  }

  [Fact]
  public async Task Prepare_ArmMacWithoutRosetta_QuestionClosedUnanswered_FailsWithoutInstalling() {
    // Arrange: an interface lets the user close the question.
    _ui.Setup(u => u.Ask(It.IsAny<string>()))
        .ThrowsAsync(new OperationCanceledException("The question was not answered."));

    // Act
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: false, Token));

    // Assert
    AssertNamesTheManualInstall(ex);
    Assert.DoesNotContain(_probe.Runs, IsRosettaInstaller);
  }

  [Fact]
  public async Task Prepare_ArmMacWithoutRosetta_StillNotInstalledAfterTheInstaller_Fails() {
    // Arrange
    _probe.RosettaInstallerInstalls = false;

    // Act
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_armMac).Prepare("win-x64", Root, acceptRosettaLicense: true, Token));

    // Assert
    AssertNamesTheManualInstall(ex);
    Assert.Single(_probe.Runs, IsRosettaInstaller);
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Prepare_Linux_NeverInstallsRosetta() {
    // Arrange
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(_linux).Prepare("win-x64", Root, acceptRosettaLicense: true, Token);

    // Assert
    Assert.DoesNotContain(_probe.Runs, IsRosettaInstaller);
    _ui.Verify(u => u.Ask(It.IsAny<string>()), Times.Never);
  }

  private static bool IsRosettaInstaller(ProgramStartInfo start) =>
      start.FileName == "/usr/sbin/softwareupdate";

  /// The message of a failure to install Rosetta 2 names the command that installs it, and that
  /// it may need an administrator (protocol/lip-workspace.md, "Installing Rosetta 2").
  private static void AssertNamesTheManualInstall(SystemDependencyException ex) {
    Assert.Contains("'softwareupdate --install-rosetta --agree-to-license'", ex.Message);
    Assert.Contains("administrator", ex.Message);
  }

  [Theory]
  [InlineData("    Installed    REG_DWORD    0x1\r\n", true)]
  [InlineData("HKEY_LOCAL_MACHINE\\x\n\tInstalled\tREG_DWORD\t0x00000001\n", true)]
  [InlineData("    Installed    REG_DWORD    0x0\r\n", false)]
  [InlineData("    Installed    REG_SZ    0x1\r\n", false)]
  [InlineData("    Installed    REG_DWORD    0x10\r\n", false)]
  [InlineData("", false)]
  public void ReadsInstalled_ReadsTheDword(string output, bool expected) {
    Assert.Equal(expected, SystemDependencies.ReadsInstalled(output));
  }

  [Theory]
  [InlineData("ID=ubuntu\nID_LIKE=debian\n", "'apt-get install libgnutls30'")]
  [InlineData("ID=\"opensuse-leap\"\nID_LIKE=\"suse opensuse\"\n", "'zypper install libgnutls30'")]
  [InlineData("ID=\"almalinux\"\nID_LIKE=\"rhel centos fedora\"\n", "'dnf install gnutls'")]
  [InlineData("ID=amzn\nID_LIKE=\"fedora\"\n", "'dnf install gnutls'")]
  [InlineData("ID=arch\n", "'pacman -S gnutls'")]
  [InlineData(null, "package manager of the system")]
  public async Task Prepare_LinuxWithoutGnuTls_ThrowsBeforeDownloadingWine(
      string? osRelease, string advice) {
    // Arrange: protocol/lip-workspace.md, "System dependencies".
    _probe.Libraries.Clear();
    _probe.OsRelease = osRelease;

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Contains("libgnutls.so.30", ex.Message);
    Assert.DoesNotContain("libfontconfig.so.1", ex.Message);
    Assert.Contains(advice, ex.Message);
    Assert.Empty(_downloaded);
    Assert.Empty(_probe.Runs);
  }

  [Fact]
  public async Task Prepare_LinuxWithFreeTypeWithoutFontconfig_Throws() {
    // Arrange: Wine then starts its desktop process without end.
    _probe.Libraries.Add("libfreetype.so.6");
    _probe.OsRelease = "ID=debian\n";

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Contains("libfontconfig.so.1", ex.Message);
    Assert.DoesNotContain("libgnutls.so.30", ex.Message);
    Assert.Contains("'apt-get install libfontconfig1'", ex.Message);
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Prepare_LinuxWithoutEither_NamesBoth() {
    // Arrange
    _probe.Libraries.Clear();
    _probe.Libraries.Add("libfreetype.so.6");
    _probe.OsRelease = "ID=fedora\n";

    // Act & Assert
    SystemDependencyException ex = await Assert.ThrowsAsync<SystemDependencyException>(
        () => Create(_linux).Prepare("win-x64", Root, false, Token));
    Assert.Contains("'dnf install gnutls fontconfig'", ex.Message);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, true)]
  public async Task Prepare_LinuxWithGnuTlsAndFontconfigOrNoFreeType_InstallsWine(
      bool freeType, bool fontconfig) {
    // Arrange
    _probe.RegOutput = RegInstalled;
    if (freeType) {
      _probe.Libraries.Add("libfreetype.so.6");
    }

    if (fontconfig) {
      _probe.Libraries.Add("libfontconfig.so.1");
    }

    // Act
    await Create(_linux).Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.True(File.Exists(WineExecutable));
  }

  [Fact]
  public async Task Prepare_MacOsWithoutTheLibraries_InstallsWine() {
    // Arrange: only Linux is checked.
    _probe.Libraries.Clear();
    _probe.RegOutput = RegInstalled;

    // Act
    await Create(new SystemInfo(HostOs.MacOS, Architecture.X64))
        .Prepare("win-x64", Root, false, Token);

    // Assert
    Assert.True(File.Exists(WineExecutable));
  }

  /// A system whose answers the test sets, recording the programs run.
  private sealed class FakeProbe : ISystemProbe {
    public SystemInfo System { get; set; } = _linux;

    public bool RosettaInstalled { get; set; }

    public bool VcRuntimeInstalled { get; set; }

    public int RegExitCode { get; set; }

    public string RegOutput { get; set; } = "";

    public int AdministratorExitCode { get; set; }

    public bool AdministratorDenied { get; set; }

    public List<ProgramStartInfo> Runs { get; } = [];

    public List<(string, IReadOnlyList<string>, byte[])> AdministratorRuns { get; } = [];

    public bool IsRosettaInstalled() => RosettaInstalled;

    public bool IsVcRuntimeInstalled() => VcRuntimeInstalled;

    /// The shared libraries the system loads: by default GnuTLS, without FreeType.
    public HashSet<string> Libraries { get; } = ["libgnutls.so.30"];

    public string? OsRelease { get; set; }

    public bool CanLoadLibrary(string name) => Libraries.Contains(name);

    public string? ReadOsRelease() => OsRelease;

    /// The exit code and the output of softwareupdate, and whether it installs Rosetta 2 when it
    /// exits with 0.
    public int RosettaInstallerExitCode { get; set; }

    public string RosettaInstallerOutput { get; set; } = "";

    public string RosettaInstallerError { get; set; } = "";

    public bool RosettaInstallerInstalls { get; set; } = true;

    public Task<ProgramResult> Run(ProgramStartInfo start, CancellationToken cancellationToken) {
      Runs.Add(start);
      if (IsRosettaInstaller(start)) {
        if (RosettaInstallerExitCode == 0 && RosettaInstallerInstalls) {
          RosettaInstalled = true;
        }

        return Task.FromResult(
            new ProgramResult(RosettaInstallerExitCode, RosettaInstallerOutput) {
              Error = RosettaInstallerError,
            });
      }

      return Task.FromResult(start.Arguments[0] == "reg"
          ? new ProgramResult(RegExitCode, RegOutput)
          : new ProgramResult(0, ""));
    }

    public Task<int> RunAsAdministrator(
        string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) {
      if (AdministratorDenied) {
        throw new OperationCanceledException("denied");
      }

      AdministratorRuns.Add((fileName, arguments, File.ReadAllBytes(fileName)));
      return Task.FromResult(AdministratorExitCode);
    }
  }
}
