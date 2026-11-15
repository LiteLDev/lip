using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flurl;
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
  private readonly Mock<IElevatedStarter> _starter = new();
  private readonly Dictionary<string, byte[]> _downloads = [];
  private readonly List<string> _downloaded = [];
  private object[] _releases = [];

  public SelfUpdaterTests() {
    _fs.AddDirectory(_install);
    _faults = new FaultyFileSystem(_fs);
    _downloader
        .Setup(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()))
        .Returns((Url url, IFileInfo destination) => {
          _fs.File.WriteAllBytes(destination.FullName, Serve(url));
          return Task.CompletedTask;
        });
    _downloader
        .Setup(d => d.Download(It.IsAny<Url>(), It.IsAny<Stream>()))
        .Returns((Url url, Stream destination) => {
          destination.Write(Serve(url));
          return Task.CompletedTask;
        });
  }

  private byte[] Serve(Url url) {
    _downloaded.Add(url.ToString());
    return _downloads.TryGetValue(url.ToString(), out byte[]? content)
        ? content
        : throw new HttpRequestException($"404 {url}");
  }

  private SelfUpdater CreateUpdater(
      bool isWindows = true, string? githubProxy = null, IFileSystem? fileSystem = null) =>
      new(
          fileSystem ?? _fs,
          _downloader.Object,
          _starter.Object,
          new SelfUpdateHost(_install, _cache, "win-x64", isWindows, LinuxArchives: false),
          githubProxy is null ? null : Url.Parse(githubProxy),
          getReleases: _ => Task.FromResult(JsonSerializer.Serialize(_releases)));

  private static string AssetUrl(string tag, string name) =>
      $"https://github.com/LiteLDev/lip/releases/download/{tag}/{name}";

  /// A release whose assets serve the given contents, with a SHA256SUMS of them unless
  /// checksums gives its content.
  private object AddRelease(
      string tag, Dictionary<string, byte[]> assets, string? checksums = null, bool draft = false) {
    Dictionary<string, byte[]> all = new(assets) {
      ["SHA256SUMS"] = Encoding.UTF8.GetBytes(checksums ?? string.Concat(assets.Select(a =>
          $"{Convert.ToHexStringLower(SHA256.HashData(a.Value))}  {a.Key}\n"))),
    };
    foreach ((string name, byte[] content) in all) {
      _downloads[AssetUrl(tag, name)] = content;
    }
    object release = new {
      tag_name = tag,
      draft,
      assets = all.Keys.Select(name => new {
        name,
        browser_download_url = AssetUrl(tag, name),
      }).ToArray(),
    };
    _releases = [.. _releases, release];
    return release;
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

  private static LipRelease Release(string version) =>
      new(SemVersion.Parse(version), new Dictionary<string, Url>());

  [Fact]
  public void ReadKind_WithoutInstallJson_IsPortable() {
    Assert.Equal(InstallationKind.Portable, SelfUpdater.ReadKind(_fs, _install));
  }

  [Theory]
  [InlineData("{\"kind\": \"setup\"}", InstallationKind.Setup)]
  [InlineData("{\"kind\": \"msi\", \"other\": 1}", InstallationKind.Msi)]
  public void ReadKind_InstallJson_IsItsKind(string json, InstallationKind expected) {
    _fs.AddFile(Path.Join(_install, "install.json"), json);

    Assert.Equal(expected, SelfUpdater.ReadKind(_fs, _install));
  }

  [Theory]
  [InlineData("{\"kind\": \"portable\"}")]
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
  public void ParseReleases_IgnoresDraftsAndOtherTags() {
    string json = JsonSerializer.Serialize(new object[] {
      new { tag_name = "v1.0.0", draft = true, assets = Array.Empty<object>() },
      new { tag_name = "1.1.0", draft = false, assets = Array.Empty<object>() },
      new { tag_name = "v1.2", draft = false, assets = Array.Empty<object>() },
      new { tag_name = 3, draft = false },
      new { tag_name = "v0.9.0", draft = false, assets = Array.Empty<object>() },
    });

    IReadOnlyList<LipRelease> releases = SelfUpdater.ParseReleases(json);

    Assert.Equal(["0.9.0"], releases.Select(r => r.Version.ToString()));
  }

  [Theory]
  [InlineData("{0}  lip-1.0.0-win-x64.zip\n", true)]
  [InlineData("{0} *lip-1.0.0-win-x64.zip\r\n", true)]
  [InlineData("{0}  lip-1.0.0-win-x64.zip.extra\n", false)]
  [InlineData("{0} lip-1.0.0-win-x64.zip\n", false)]
  public void FindChecksum_ReadsSha256sumLines(string format, bool found) {
    string hex = new('A', 64);
    string? checksum = SelfUpdater.FindChecksum(
        "0000  other\n" + string.Format(format, hex), "lip-1.0.0-win-x64.zip");

    Assert.Equal(found ? new string('a', 64) : null, checksum);
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
  public async Task Run_ChecksumMismatch_FailsWithoutChangingTheInstallation() {
    _fs.AddFile(Path.Join(_install, "lip.exe"), "old");
    AddRelease(
        "v1.1.0",
        new() { ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "new" }) },
        checksums: $"{new string('0', 64)}  lip-1.1.0-win-x64.zip\n");

    await Assert.ThrowsAsync<SelfUpdateException>(
        () => CreateUpdater().Run(SemVersion.Parse("1.0.0"), false));

    Assert.Equal("old", _fs.File.ReadAllText(Path.Join(_install, "lip.exe")));
    Assert.False(_fs.File.Exists(Path.Join(_cache, "self-update", "lip-1.1.0-win-x64.zip")));
  }

  [Fact]
  public async Task Run_ChecksumMissing_Fails() {
    AddRelease(
        "v1.1.0",
        new() { ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "new" }) },
        checksums: "");

    await Assert.ThrowsAsync<SelfUpdateException>(
        () => CreateUpdater().Run(SemVersion.Parse("1.0.0"), false));
  }

  [Fact]
  public async Task Run_Setup_StartsTheInstallerElevated() {
    _fs.AddFile(Path.Join(_install, "install.json"), "{\"kind\":\"setup\"}");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64-setup.exe"] = [1, 2, 3] });

    SelfUpdateResult result = await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    string installer = Path.Join(_cache, "self-update", "lip-1.1.0-win-x64-setup.exe");
    Assert.Equal(SelfUpdateOutcome.InstallerStarted, result.Outcome);
    Assert.Equal([1, 2, 3], _fs.File.ReadAllBytes(installer));
    _starter.Verify(s => s.Start(installer, new[] { "/quiet", "/norestart" }));
  }

  [Fact]
  public async Task Run_Msi_StartsMsiexecElevated() {
    _fs.AddFile(Path.Join(_install, "install.json"), "{\"kind\":\"msi\"}");
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.msi"] = [4] });

    await CreateUpdater().Run(SemVersion.Parse("1.0.0"), false);

    string msi = Path.Join(_cache, "self-update", "lip-1.1.0-win-x64.msi");
    _starter.Verify(s => s.Start("msiexec.exe", new[] { "/i", msi, "/qn", "/norestart" }));
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
  public async Task Run_NoUpdateAsset_FailsBeforeDownloading() {
    AddRelease("v1.1.0", new() { ["lip-1.1.0-linux-x64.tar.xz"] = [1] });

    await Assert.ThrowsAsync<SelfUpdateException>(
        () => CreateUpdater().Run(SemVersion.Parse("1.0.0"), false));
    Assert.Empty(_downloaded);
  }

  [Fact]
  public async Task Run_GithubProxy_DownloadsTheUpdateThroughItThenDirectly() {
    AddRelease("v1.1.0", new() { ["lip-1.1.0-win-x64.zip"] = Zip(new() { ["lip.exe"] = "new" }) });

    await CreateUpdater(githubProxy: "https://proxy.example/").Run(
        SemVersion.Parse("1.0.0"), false);

    Assert.Equal(
        [
          AssetUrl("v1.1.0", "SHA256SUMS"),
          "https://proxy.example/LiteLDev/lip/releases/download/v1.1.0/lip-1.1.0-win-x64.zip",
          AssetUrl("v1.1.0", "lip-1.1.0-win-x64.zip"),
        ],
        _downloaded);
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
