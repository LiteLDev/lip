using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Tests.Mcmod;
using Moq;
using Semver;

namespace Lip.Core.Tests.Infrastructure;

public class SelfUpdaterTests {
  private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\t" : "/t";
  private static readonly string _install = Path.Join(_root, "lip");
  private static readonly string _cache = Path.Join(_root, "cache");

  private readonly MockFileSystem _fs = new();
  private readonly FaultyFileSystem _faults;
  private readonly Mock<IFileDownloader> _downloader = new();
  private readonly Mock<IInstallerStarter> _starter = new();
  private readonly Dictionary<string, byte[]> _downloads = [];
  private readonly List<string> _downloaded = [];
  private readonly Mock<IGitRemoteClient> _git = new();
  private readonly List<GitRemoteRef> _tags = [];

  public SelfUpdaterTests() {
    _fs.AddDirectory(_install);
    _faults = new FaultyFileSystem(_fs);
    _git
        .Setup(g => g.ListRefs(It.IsAny<Url>(), It.IsAny<IEnumerable<string>>()))
        .ReturnsAsync(() => _tags);
    _downloader
        .Setup(d => d.DownloadFile(
            It.IsAny<Url>(), It.IsAny<IFileInfo>(), It.IsAny<Action<Url>?>()))
        .Returns((Url url, IFileInfo destination, Action<Url>? validate) => {
          _fs.File.WriteAllBytes(destination.FullName, Serve(url));
          validate?.Invoke(url);
          return Task.CompletedTask;
        });
    _downloader
        .Setup(d => d.Download(It.IsAny<Url>(), It.IsAny<Action<byte[], Url>?>(), It.IsAny<bool>()))
        .Returns((Url url, Action<byte[], Url>? validate, bool _) => {
          byte[] content = Serve(url);
          validate?.Invoke(content, url);
          return Task.FromResult(content);
        });
  }

  private byte[] Serve(Url url) {
    _downloaded.Add(url.ToString());
    return _downloads.TryGetValue(url.ToString(), out byte[]? content)
        ? content
        : throw new HttpRequestException($"404 {url}");
  }

  private SelfUpdater CreateUpdater(
      bool isWindows = true, IFileSystem? fileSystem = null) =>
      new(
          fileSystem ?? _fs,
          _downloader.Object,
          _git.Object,
          _starter.Object,
          new SelfUpdateHost(_install, _cache, "win-x64", isWindows, LinuxArchives: false));

  private static string AssetUrl(string tag, string name) =>
      $"https://github.com/LiteLDev/lip/releases/download/{tag}/{name}";

  /// A release: a version tag of the repository, whose assets serve the given contents.
  private void AddRelease(string tag, Dictionary<string, byte[]> assets) {
    foreach ((string name, byte[] content) in assets) {
      _downloads[AssetUrl(tag, name)] = content;
    }
    _tags.Add(new GitRemoteRef($"refs/tags/{tag}", new string('a', 40), null));
  }

  private static byte[] Zip(Dictionary<string, string> files) {
    using MemoryStream stream = new();
    using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach ((string name, string content) in files) {
        using StreamWriter writer = new(zip.CreateEntry(name).Open());
        writer.Write(content);
      }
    }
    return stream.ToArray();
  }

  private static LipRelease Release(string version) => new(SemVersion.Parse(version));

  [Fact]
  public void ReadKind_WithoutInstallJson_IsPortable() {
    Assert.Equal(InstallationKind.Portable, SelfUpdater.ReadKind(_fs, _install));
  }

  [Fact]
  public void ReadKind_InstallJsonOfMsi_IsMsi() {
    _fs.AddFile(Path.Join(_install, "install.json"), "{\"kind\": \"msi\", \"other\": 1}");

    Assert.Equal(InstallationKind.Msi, SelfUpdater.ReadKind(_fs, _install));
  }

  [Theory]
  [InlineData("{\"kind\": \"msi\", \"scope\": \"machine\"}", InstallationKind.Msi)]
  [InlineData("{\"kind\": \"msi\", \"scope\": \"user\"}", InstallationKind.MsiForUser)]
  public void ReadKind_InstallJsonOfMsiWithScope_IsMsiOfThatScope(
      string json, InstallationKind expected) {
    _fs.AddFile(Path.Join(_install, "install.json"), json);

    Assert.Equal(expected, SelfUpdater.ReadKind(_fs, _install));
  }

  [Theory]
  [InlineData("{\"kind\": \"msi\", \"scope\": \"everyone\"}")]
  [InlineData("{\"kind\": \"msi\", \"scope\": 1}")]
  [InlineData("{\"kind\": \"portable\"}")]
  [InlineData("{\"kind\": \"setup\"}")]
  [InlineData("{\"kind\": 1}")]
  [InlineData("[\"setup\"]")]
  [InlineData("{}")]
  [InlineData("not json")]
  public void ReadKind_InvalidInstallJson_Fails(string json) {
    _fs.AddFile(Path.Join(_install, "install.json"), json);

    Assert.Throws<SelfUpdateException>(() => SelfUpdater.ReadKind(_fs, _install));
  }

  [Fact]
  public void FindLatest_StableRunning_SkipsPrereleases() {
    LipRelease? latest = SelfUpdater.FindLatest(
        [Release("0.40.0"), Release("0.41.0-beta.3"), Release("0.39.0")],
        SemVersion.Parse("0.39.0"));

    Assert.Equal("0.40.0", latest?.Version.ToString());
  }

  [Fact]
  public void FindLatest_PrereleaseRunning_IncludesPrereleases() {
    LipRelease? latest = SelfUpdater.FindLatest(
        [Release("0.40.0"), Release("0.41.0-beta.3"), Release("0.41.0-beta.10")],
        SemVersion.Parse("0.41.0-beta.1"));

    Assert.Equal("0.41.0-beta.10", latest?.Version.ToString());
  }

  [Fact]
  public void FindLatest_OnlyPrereleases_IsNullForStableRunning() {
    Assert.Null(SelfUpdater.FindLatest([Release("1.0.0-rc.1")], SemVersion.Parse("0.40.0")));
  }

  [Fact]
  public void ParseReleases_KeepsOnlyVersionTags() {
    IReadOnlyList<LipRelease> releases = SelfUpdater.ParseReleases([
      new("refs/tags/v0.9.0", new string('a', 40), null),
      new("refs/tags/1.1.0", new string('b', 40), null),
      new("refs/tags/v1.2", new string('c', 40), null),
      new("refs/heads/v2.0.0", new string('d', 40), null),
      new("refs/tags/v1.0.0-rc.1", new string('e', 40), new string('f', 40)),
    ]);

    Assert.Equal(["0.9.0", "1.0.0-rc.1"], releases.Select(r => r.Version.ToString()));
  }

  [Fact]
  public async Task Run_ReadsTheVersionTagsOfTheRepositoryOfLip() {
    AddRelease("v1.0.0", new() { ["lip-1.0.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "1" }) });

    await CreateUpdater().Run(SemVersion.Parse("1.0.0"), true);

    _git.Verify(g => g.ListRefs(
        It.Is<Url>(u => u.ToString() == "https://github.com/LiteLDev/lip.git"),
        It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "refs/tags/v" }))));
  }

  [Fact]
  public async Task Run_NotNewer_IsUpToDateAndDownloadsNothing() {
    AddRelease("v1.0.0", new() { ["lip-1.0.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "1" }) });

    SelfUpdateResult result = await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    Assert.Equal(SelfUpdateOutcome.UpToDate, result.Outcome);
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Run_Check_ReportsNewerAndChangesNothing() {
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "1" }) });
    _fs.AddFile(Path.Join(_install, "install.json"), "invalid");

    SelfUpdateResult result = await CreateUpdater().Run(SemVersion.Parse("1.0.0"), true);

    Assert.Equal(SelfUpdateOutcome.Available, result.Outcome);
    Assert.Equal("1.1.0", result.Latest?.ToString());
    Assert.Empty(_downloaded);
    Assert.False(_fs.Directory.Exists(_cache));
  }

  [Fact]
  public async Task Run_Portable_ReplacesFilesAndKeepsOthers() {
    _fs.AddFile(Path.Join(_install, "lip.exe"), "old");
    _fs.AddFile(Path.Join(_install, "notes.txt"), "mine");
    AddRelease("v1.1.0", new() {
      ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "new", ["sub/data.txt"] = "data" }),
    });

    SelfUpdateResult result = await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    Assert.Equal(SelfUpdateOutcome.Replaced, result.Outcome);
    Assert.Equal("new", _fs.File.ReadAllText(Path.Join(_install, "lip.exe")));
    Assert.Equal("data", _fs.File.ReadAllText(Path.Join(_install, "sub", "data.txt")));
    Assert.Equal("mine", _fs.File.ReadAllText(Path.Join(_install, "notes.txt")));
    Assert.False(_fs.File.Exists(Path.Join(_install, "lip.exe.old")));
    Assert.False(_fs.Directory.Exists(Path.Join(_cache, "self-update")));
    _starter.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Run_PortableRunningFileCannotBeDeleted_KeepsItAsOld() {
    _fs.AddFile(Path.Join(_install, "lip.exe"), "old");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "new" }) });
    _faults.FailDelete = path => path.EndsWith("lip.exe.old", StringComparison.Ordinal);

    await CreateUpdater(fileSystem: _faults).Run(SemVersion.Parse("1.0.0"), false);

    Assert.Equal("new", _fs.File.ReadAllText(Path.Join(_install, "lip.exe")));
    Assert.Equal("old", _fs.File.ReadAllText(Path.Join(_install, "lip.exe.old")));
  }

  [Fact]
  public async Task Run_PortableWriteFails_RestoresTheFiles() {
    _fs.AddFile(Path.Join(_install, "a.txt"), "old a");
    _fs.AddFile(Path.Join(_install, "b.txt"), "old b");
    AddRelease("v1.1.0", new() {
      ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["a.txt"] = "new a", ["b.txt"] = "new b" }),
    });
    _faults.Fail = (operation, path) =>
        operation == "move" && path == Path.Join(_install, "b.txt.tmp");

    await Assert.ThrowsAsync<IOException>(
        () => CreateUpdater(fileSystem: _faults).Run(SemVersion.Parse("1.0.0"), false));

    Assert.Equal("old a", _fs.File.ReadAllText(Path.Join(_install, "a.txt")));
    Assert.Equal("old b", _fs.File.ReadAllText(Path.Join(_install, "b.txt")));
    Assert.False(_fs.File.Exists(Path.Join(_install, "a.txt.old")));
    Assert.False(_fs.File.Exists(Path.Join(_install, "b.txt.old")));
    Assert.False(_fs.File.Exists(Path.Join(_install, "b.txt.tmp")));
  }

  [Fact]
  public async Task Run_Msi_StartsMsiexecElevated() {
    _fs.AddFile(Path.Join(_install, "install.json"), "{\"kind\":\"msi\"}");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.msi"] = [4] });

    SelfUpdateResult result = await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    string msi = Path.Join(_cache, "self-update", "lip-1.1.0-win-x64.msi");
    Assert.Equal(SelfUpdateOutcome.InstallerStarted, result.Outcome);
    Assert.Equal([4], _fs.File.ReadAllBytes(msi));
    _starter.Verify(s => s.Start(
        "msiexec.exe", new[] { "/i", msi, "/qn", "/norestart" }, true));
  }

  [Fact]
  public async Task Run_MsiForTheUser_StartsMsiexecAsTheUser() {
    _fs.AddFile(Path.Join(_install, "install.json"), "{\"kind\":\"msi\",\"scope\":\"user\"}");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.msi"] = [4] });

    SelfUpdateResult result = await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    string msi = Path.Join(_cache, "self-update", "lip-1.1.0-win-x64.msi");
    Assert.Equal(SelfUpdateOutcome.InstallerStarted, result.Outcome);
    _starter.Verify(s => s.Start(
        "msiexec.exe", new[] { "/i", msi, "/qn", "/norestart" }, false));
  }

  [Fact]
  public async Task Run_InstallerKindOffWindows_Fails() {
    _fs.AddFile(Path.Join(_install, "install.json"), "{\"kind\":\"msi\"}");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.msi"] = [4] });

    await Assert.ThrowsAsync<SelfUpdateException>(
        () => CreateUpdater(isWindows: false).Run(SemVersion.Parse("1.0.0"), false));
    _starter.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Run_NoUpdateAsset_FailsWithoutChangingTheInstallation() {
    _fs.AddFile(Path.Join(_install, "lip.exe"), "old");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-linux-x64.tar.xz"] = [1] });

    await Assert.ThrowsAsync<HttpRequestException>(
        () => CreateUpdater().Run(SemVersion.Parse("1.0.0"), false));

    Assert.Equal("old", _fs.File.ReadAllText(Path.Join(_install, "lip.exe")));
    Assert.Equal([AssetUrl("v1.1.0", "lip-1.1.0-win-x64.zip")], _downloaded);
  }

  [Fact]
  public async Task Run_DownloadsOnlyTheUpdateAssetFromItsReleaseUrl() {
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "new" }) });

    await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    // protocol/lip-self-update.md, "Terms": through github_proxy, which the downloader applies.
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == AssetUrl("v1.1.0", "lip-1.1.0-win-x64.zip")),
        It.IsAny<IFileInfo>(), It.IsAny<Action<Url>?>()), Times.Once);
    _downloader.Verify(
        d => d.Download(It.IsAny<Url>(), It.IsAny<Action<byte[], Url>?>(), It.IsAny<bool>()),
        Times.Never);
  }

  [Fact]
  public void DeleteOldExecutable_DeletesOnlyTheOldExecutable() {
    _fs.AddFile(Path.Join(_install, "lip.exe"), "new");
    _fs.AddFile(Path.Join(_install, "lip.exe.old"), "old");
    _fs.AddFile(Path.Join(_install, "notes.txt.old"), "mine");

    SelfUpdater.DeleteOldExecutable(_fs, Path.Join(_install, "lip.exe"));

    Assert.False(_fs.File.Exists(Path.Join(_install, "lip.exe.old")));
    Assert.True(_fs.File.Exists(Path.Join(_install, "notes.txt.old")));
  }

  [Fact]
  public void DeleteOldExecutable_IgnoresAFileInUse() {
    _fs.AddFile(Path.Join(_install, "lip.exe.old"), "old");
    _faults.FailDelete = _ => true;

    SelfUpdater.DeleteOldExecutable(_faults, Path.Join(_install, "lip.exe"));

    Assert.True(_fs.File.Exists(Path.Join(_install, "lip.exe.old")));
  }

  [Fact]
  public void DeleteOldExecutable_WithoutOldExecutable_DoesNothing() {
    SelfUpdater.DeleteOldExecutable(_fs, Path.Join(_root, "missing", "lip.exe"));
    SelfUpdater.DeleteOldExecutable(_fs, null);
  }
}
