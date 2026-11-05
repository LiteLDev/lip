using System.Formats.Tar;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;

namespace Lip.Core.Tests.Mcmod.Archives;

/// Archives of sources (protocol/package.md §1, §2) and of assets (protocol/workspace.md §4 step
/// 2), and reopening them from the workspace file (protocol/workspace.md §5, §7.1).
public class ArchiveProviderTests {
  private const string _spec = "example.com/owner/repo";
  private const string _repoUrl = "https://example.com/owner/repo.git";
  private const string _commit = "0123456789abcdef0123456789abcdef01234567";
  private const string _url = "https://example.com/releases/pkg.zip";

  private static readonly string _workspace =
      Path.Combine(Path.GetTempPath(), "lip-archive-provider-tests", "workspace");

  private readonly MockFileSystem _fileSystem = new();
  private readonly Mock<IGitRemoteClient> _git = new();
  private readonly Mock<IFileDownloader> _downloader = new();
  private readonly Dictionary<string, byte[]> _blobs = [];
  private readonly Dictionary<string, byte[]> _downloads = [];
  private readonly CacheService _cache;

  public ArchiveProviderTests() {
    _cache = new(_fileSystem, new Mock<IUserInteraction>().Object);

    _git.Setup(c => c.FetchBlobs(
            It.IsAny<Url>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<Func<string, Stream, Task>>()))
        .Returns(async (Url _, IReadOnlyCollection<string> oids,
            Func<string, Stream, Task> onBlob) => {
              foreach (string oid in oids) {
                using MemoryStream stream = new(_blobs[oid]);
                await onBlob(oid, stream);
              }
            });

    _downloader.Setup(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()))
        .Returns((Url url, IFileInfo destination) => {
          if (!_downloads.TryGetValue(url.ToString(), out byte[]? content)) {
            throw new HttpRequestException($"HTTP 404: {url}");
          }

          _fileSystem.File.WriteAllBytes(destination.FullName, content);
          return Task.CompletedTask;
        });
  }

  private ArchiveProvider CreateProvider(Url? githubProxy = null) =>
      new(_git.Object, _cache, _fileSystem, _downloader.Object, githubProxy);

  private string AddBlob(string content) => AddBlob(Encoding.UTF8.GetBytes(content));

  private string AddBlob(byte[] content) {
    string oid = Convert.ToHexStringLower(
        SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {content.Length}\0"), .. content]));
    _blobs[oid] = content;
    return oid;
  }

  private void SetupTree(string commit, params GitTreeEntry[] entries) {
    _git.Setup(c => c.ListTree(It.Is<Url>(u => u.ToString() == _repoUrl), commit))
        .ReturnsAsync(entries);
  }

  private void SetupTags(params GitRemoteRef[] refs) {
    _git.Setup(c => c.ListRefs(
            It.Is<Url>(u => u.ToString() == _repoUrl), It.IsAny<IEnumerable<string>>()))
        .ReturnsAsync(refs);
  }

  private static byte[] Zip(params (string Name, string? Content)[] entries) =>
      Zip([.. entries.Select(e => (e.Name, e.Content, 0))]);

  private static byte[] Zip(
      params (string Name, string? Content, int ExternalAttributes)[] entries) {
    using MemoryStream memory = new();
    using (ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach ((string name, string? content, int attributes) in entries) {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        entry.ExternalAttributes = attributes;
        if (content is not null) {
          using Stream stream = entry.Open();
          stream.Write(Encoding.UTF8.GetBytes(content));
        }
      }
    }
    return memory.ToArray();
  }

  private static string Sha256(byte[] content) =>
      Convert.ToHexStringLower(SHA256.HashData(content));

  private static async Task<string> Read(ArchiveHandle archive, string key) =>
      await Read(archive.Source, key);

  private static async Task<string> Read(ISource source, string key) {
    using StreamReader reader = new(await source.OpenRead(key));
    return await reader.ReadToEndAsync();
  }

  private string[] CachedFiles() =>
      [.. _fileSystem.Directory.GetFiles(LipDirectories.Cache).Select(Path.GetFileName)!];

  // Local files and directories (protocol/package.md §2.3).

  [Fact]
  public async Task Open_LocalFile_ReturnsZipArchiveKeyedByFileUri() {
    // Arrange
    byte[] zip = Zip(("tooth.json", "local"), ("sub/a.txt", "sub"));
    string path = Path.Combine(_workspace, "pkg.zip");
    _fileSystem.AddFile(path, new MockFileData(zip));
    Ref file = Ref.FromFile(path, "lua");

    // Act
    ArchiveHandle archive = await CreateProvider().Open(file);

    // Assert
    Assert.Equal(file.Target, archive.Key);
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), file.Target), archive.Identity);
    Assert.IsType<ZipSource>(archive.Source);
    Assert.Equal("local", await Read(archive, "tooth.json"));
    Assert.Equal("sub", await Read(archive, "sub/a.txt"));
    _downloader.VerifyNoOtherCalls();
    Assert.False(_fileSystem.Directory.Exists(LipDirectories.Cache));
  }

  [Fact]
  public async Task Open_LocalFileOfAnyName_IsAZipArchiveByContent() {
    // Arrange
    byte[] zip = Zip(("tooth.json", "{}"));
    string path = Path.Combine(_workspace, "pkg.bin");
    _fileSystem.AddFile(path, new MockFileData(zip));

    // Act
    ArchiveHandle archive = await CreateProvider().Open(Ref.FromFile(path));

    // Assert
    Assert.Equal(["tooth.json"], archive.Source.Keys);
  }

  [Fact]
  public async Task Open_LocalFileNotAZip_ThrowsArchiveUnavailable() {
    // Arrange
    string path = Path.Combine(_workspace, "pkg.zip");
    _fileSystem.AddFile(path, new MockFileData("not a zip"));

    // Act & Assert
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.FromFile(path)));
    Assert.Contains("not a ZIP archive", ex.Message);
  }

  [Fact]
  public async Task Open_LocalFileMissing_ThrowsArchiveUnavailable() {
    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.FromFile(Path.Combine(_workspace, "missing.zip"))));
  }

  [Fact]
  public async Task Open_LocalDirectory_ReturnsDirectoryArchiveOfItsAbsolutePath() {
    // Arrange
    string directory = Path.Combine(_workspace, "pkg");
    _fileSystem.AddFile(Path.Combine(directory, "tooth.json"), new MockFileData("{}"));
    _fileSystem.AddFile(Path.Combine(directory, "lib", "a.dll"), new MockFileData("a"));
    Ref source = Ref.FromFile(directory + Path.DirectorySeparatorChar);

    // Act
    ArchiveHandle archive = await CreateProvider().Open(source);

    // Assert
    Assert.Equal(source.Target, archive.Key);
    Assert.Equal(new DirectoryArchiveIdentity(directory), archive.Identity);
    Assert.IsType<DirectorySource>(archive.Source);
    Assert.Equal(["lib/a.dll", "tooth.json"], archive.Source.Keys);
    Assert.Equal("a", await Read(archive, "lib/a.dll"));
    _downloader.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Open_LocalDirectory_ReadsItWhereItIs() {
    // Arrange
    string directory = Path.Combine(_workspace, "pkg");
    string file = Path.Combine(directory, "tooth.json");
    _fileSystem.AddFile(file, new MockFileData("v1"));
    ArchiveHandle archive = await CreateProvider().Open(Ref.FromFile(directory));

    // Act
    _fileSystem.File.WriteAllText(file, "v2");

    // Assert
    Assert.Equal("v2", await Read(archive, "tooth.json"));
  }

  [Fact]
  public async Task Open_RemoteRepository_Throws() {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(() => CreateProvider().Open(Ref.Parse(_spec)));
  }

  [Fact]
  public async Task OpenLocked_LocalFile_VerifiesContent() {
    // Arrange
    string path = Path.Combine(_workspace, "pkg.zip");
    byte[] zip = Zip(("tooth.json", "v1"));
    _fileSystem.AddFile(path, new MockFileData(zip));
    Ref file = Ref.FromFile(path);
    ZipArchiveIdentity identity = new(Sha256(zip), file.Target);
    ArchiveProvider provider = CreateProvider();

    // Act
    ArchiveHandle locked = await provider.OpenLocked(file.Target, identity);
    string content = await Read(locked, "tooth.json");
    _fileSystem.File.WriteAllBytes(path, Zip(("tooth.json", "v2")));

    // Assert
    Assert.Equal(identity, locked.Identity);
    Assert.Equal("v1", content);
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => provider.OpenLocked(file.Target, identity));
    Assert.Contains(identity.Sha256, ex.Message);
    _downloader.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task OpenLocked_LocalFileGone_ThrowsArchiveUnavailable() {
    // Arrange
    Ref file = Ref.FromFile(Path.Combine(_workspace, "gone.zip"));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        file.Target, new ZipArchiveIdentity(new string('0', 64), file.Target)));
  }

  [Fact]
  public async Task OpenLocked_Directory_OpensItsPath() {
    // Arrange
    string directory = Path.Combine(_workspace, "pkg");
    _fileSystem.AddFile(Path.Combine(directory, "tooth.json"), new MockFileData("{}"));

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(
        "k", new DirectoryArchiveIdentity(directory));

    // Assert
    Assert.Equal("k", archive.Key);
    Assert.Equal(new DirectoryArchiveIdentity(directory), archive.Identity);
    Assert.Equal(["tooth.json"], archive.Source.Keys);
  }

  [Fact]
  public async Task OpenLocked_DirectoryGone_ThrowsArchiveUnavailable() {
    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        "k", new DirectoryArchiveIdentity(Path.Combine(_workspace, "gone"))));
  }

  // Remote repositories (protocol/package.md §2.1).

  [Theory]
  [InlineData("branch:main", "refs/heads/main")]
  [InlineData("tag:release-1", "refs/tags/release-1")]
  [InlineData("1.0.0", "refs/tags/v1.0.0")]
  public async Task ResolveGitRef_VersionBranchOrTag_ReturnsExactRefCommitPeeled(
      string gitRef, string refName) {
    // Arrange: a longer name sharing the prefix, and an annotated tag object.
    string other = new('1', 40);
    string tagObject = new('2', 40);
    SetupTags(
        new GitRemoteRef(refName + "-old", other, null),
        new GitRemoteRef(refName, tagObject, _commit));

    // Act
    string commit = await CreateProvider().ResolveGitRef(_spec, GitRef.Parse(gitRef));

    // Assert
    Assert.Equal(_commit, commit);
    _git.Verify(c => c.ListRefs(
        It.Is<Url>(u => u.ToString() == _repoUrl),
        It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { refName }))));
  }

  [Fact]
  public async Task ResolveGitRef_MissingBranch_ThrowsArchiveUnavailable() {
    // Arrange
    SetupTags();

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().ResolveGitRef(_spec, GitRef.Parse("branch:gone")));
  }

  [Fact]
  public async Task ResolveGitRef_ListRefsFails_ThrowsArchiveUnavailable() {
    // Arrange
    _git.Setup(c => c.ListRefs(It.IsAny<Url>(), It.IsAny<IEnumerable<string>>()))
        .ThrowsAsync(new HttpRequestException("network"));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().ResolveGitRef(_spec, GitRef.Parse("branch:main")));
  }

  [Fact]
  public async Task ResolveGitRef_Rev_ReturnsCommitWithoutNetwork() {
    // Act
    string commit = await CreateProvider().ResolveGitRef(
        _spec, GitRef.Parse($"rev:{_commit.ToUpperInvariant()}"));

    // Assert
    Assert.Equal(_commit, commit);
    _git.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task OpenCommit_Commit_ReturnsGitArchiveKeyedByRev() {
    // Arrange
    SetupTree(_commit, new GitTreeEntry("tooth.json", 100644, AddBlob("{}")));

    // Act
    ArchiveHandle archive = await CreateProvider().OpenCommit(_spec, _commit.ToUpperInvariant());

    // Assert
    Assert.Equal($"{_spec}@rev:{_commit}", archive.Key);
    Assert.Equal(new GitArchiveIdentity(_repoUrl, _commit), archive.Identity);
    Assert.Equal("{}", await Read(archive, "tooth.json"));
  }

  [Fact]
  public async Task ListVersions_MixedTags_ReturnsStrictSemVerTagsByPrecedence() {
    // Arrange
    SetupTags(
        new("refs/tags/v1.0.0", "a1", null),
        new("refs/tags/v1.2", "a2", null),
        new("refs/tags/V2.0.0", "a3", null),
        new("refs/tags/2.0.0", "a4", null),
        new("refs/tags/v2.0.0-rc.1", "a5", null),
        new("refs/tags/v2.0.0+b", "a6", null),
        new("refs/tags/v2.0.0+a", "a7", null),
        new("refs/tags/v01.0.0", "a8", null),
        new("refs/tags/v1.7.0.1", "a9", null),
        new("refs/tags/vlatest", "a10", null));

    // Act
    IReadOnlyList<SpecVersion> versions = await CreateProvider().ListVersions(_spec);

    // Assert
    Assert.Equal(
        ["v2.0.0+b", "v2.0.0+a", "v2.0.0-rc.1", "v1.0.0"],
        versions.Select(v => v.Tag));
    Assert.Equal("2.0.0+b", versions[0].Version.ToString());
  }

  [Fact]
  public async Task ListVersions_CalledTwice_ListsRefsOnce() {
    // Arrange
    SetupTags(new GitRemoteRef("refs/tags/v1.0.0", "a1", null));
    ArchiveProvider provider = CreateProvider();

    // Act
    await provider.ListVersions(_spec);
    await provider.ListVersions(_spec);

    // Assert
    _git.Verify(
        c => c.ListRefs(It.IsAny<Url>(), It.IsAny<IEnumerable<string>>()), Times.Once);
  }

  [Fact]
  public async Task ListVersions_ListRefsFails_ThrowsArchiveUnavailableAndRetriesLater() {
    // Arrange
    _git.SetupSequence(c => c.ListRefs(It.IsAny<Url>(), It.IsAny<IEnumerable<string>>()))
        .ThrowsAsync(new HttpRequestException("network"))
        .ReturnsAsync([new GitRemoteRef("refs/tags/v1.0.0", "a1", null)]);
    ArchiveProvider provider = CreateProvider();

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => provider.ListVersions(_spec));
    Assert.Single(await provider.ListVersions(_spec));
  }

  [Fact]
  public async Task OpenVersion_AnnotatedTag_OpensPeeledCommit() {
    // Arrange
    SetupTags(new GitRemoteRef("refs/tags/v1.0.0", "tagobject", _commit));
    SetupTree(_commit, new GitTreeEntry("tooth.json", 100644, AddBlob("{}")));
    ArchiveProvider provider = CreateProvider();
    SpecVersion version = (await provider.ListVersions(_spec)).Single();

    // Act
    ArchiveHandle archive = await provider.OpenVersion(_spec, version);

    // Assert
    Assert.Equal($"{_spec}@1.0.0", archive.Key);
    Assert.Equal(new GitArchiveIdentity(_repoUrl, _commit), archive.Identity);
    Assert.Equal("{}", await Read(archive, "tooth.json"));
  }

  [Fact]
  public async Task OpenVersion_UnknownTag_ThrowsArchiveUnavailable() {
    // Arrange
    SetupTags(new GitRemoteRef("refs/tags/v1.0.0", _commit, null));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenVersion(
        _spec, new SpecVersion(Semver.SemVersion.Parse("2.0.0"), "v2.0.0")));
  }

  [Fact]
  public async Task OpenLocked_Git_OnlyRegularFilesArePartOfTheArchive() {
    // Arrange
    SetupTree(_commit,
        new GitTreeEntry("tooth.json", 100644, AddBlob("{}")),
        new GitTreeEntry("run.sh", 100755, AddBlob("echo")),
        new GitTreeEntry("link", 120000, AddBlob("tooth.json")));

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(
        $"{_spec}@1.0.0", new GitArchiveIdentity(_repoUrl, _commit));

    // Assert
    Assert.Equal(["run.sh", "tooth.json"], archive.Source.Keys.Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task OpenLocked_GitCommitUnavailable_ThrowsArchiveUnavailable() {
    // Arrange
    _git.Setup(c => c.ListTree(It.IsAny<Url>(), It.IsAny<string>()))
        .ThrowsAsync(new GitProtocolException("not our ref"));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        "k", new GitArchiveIdentity(_repoUrl, _commit)));
  }

  // URLs (protocol/package.md §2.2) and ZIP validation (protocol/package.md §1).

  [Theory]
  [InlineData("../evil.txt", 0)]
  [InlineData("a/../../evil.txt", 0)]
  [InlineData("/abs.txt", 0)]
  [InlineData("C:/evil.txt", 0)]
  [InlineData("a\\b.txt", 0)]
  [InlineData("link", unchecked((int)0xA1FF0000))]
  public async Task Open_ZipWithInvalidEntry_ThrowsArchiveUnavailable(string name, int attributes) {
    // Arrange
    _downloads[_url] = Zip(("tooth.json", "{}", 0), (name, "x", attributes));

    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.Parse(_url)));

    // Assert
    Assert.Contains("not a valid archive", ex.Message);
  }

  [Theory]
  [InlineData("../evil.txt", 0)]
  [InlineData("a\\b.txt", 0)]
  [InlineData("link", unchecked((int)0xA1FF0000))]
  public async Task Open_LocalZipWithInvalidEntry_ThrowsArchiveUnavailable(
      string name, int attributes) {
    // Arrange
    string path = Path.Combine(_workspace, "pkg.zip");
    _fileSystem.AddFile(path, new MockFileData(Zip(("tooth.json", "{}", 0), (name, "x", attributes))));

    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.FromFile(path)));

    // Assert
    Assert.Contains("not a valid archive", ex.Message);
  }

  [Fact]
  public async Task Open_ZipWithUnixRegularFiles_Succeeds() {
    // Arrange
    _downloads[_url] = Zip(
        ("tooth.json", "{}", unchecked((int)0x81A40000)), ("dir/", null, 0x41ED0000));

    // Act
    ArchiveHandle archive = await CreateProvider().Open(Ref.Parse(_url));

    // Assert
    Assert.Equal(["tooth.json"], archive.Source.Keys);
  }

  [Fact]
  public async Task Open_Url_DownloadsOnceAndIdentifiesBySha256() {
    // Arrange
    byte[] zip = Zip(("tooth.json", "{}"));
    _downloads[_url] = zip;

    // Act
    ArchiveHandle first = await CreateProvider().Open(Ref.Parse(_url + "#variant"));
    ArchiveHandle second = await CreateProvider().Open(Ref.Parse(_url));

    // Assert
    Assert.Equal(_url, first.Key);
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), _url), first.Identity);
    Assert.Equal(first.Identity, second.Identity);
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task Open_Url_CachesOnlyUnderHttpKeyOfUrl() {
    // Arrange
    _downloads[_url] = Zip(("tooth.json", "{}"));

    // Act
    await CreateProvider().Open(Ref.Parse(_url));

    // Assert
    Assert.Equal([CacheKeys.Http(_url)], CachedFiles());
    Assert.Empty(_fileSystem.Directory.GetFiles(LipDirectories.Staging));
  }

  [Fact]
  public async Task Open_UrlNotAZip_ThrowsArchiveUnavailableAndCachesNothing() {
    // Arrange
    _downloads[_url] = Encoding.UTF8.GetBytes("<html>not found</html>");

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.Parse(_url)));
    Assert.Empty(CachedFiles());
  }

  [Fact]
  public async Task Open_CachedUrlCorrupted_Redownloads() {
    // Arrange
    byte[] zip = Zip(("tooth.json", "{}"));
    _downloads[_url] = zip;
    _fileSystem.AddFile(
        Path.Combine(LipDirectories.Cache, CacheKeys.Http(_url)), new MockFileData("corrupted"));

    // Act
    ArchiveHandle archive = await CreateProvider().Open(Ref.Parse(_url));

    // Assert
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), _url), archive.Identity);
    Assert.Equal("{}", await Read(archive, "tooth.json"));
  }

  [Fact]
  public async Task Open_GitHubUrlWithProxy_TriesProxyThenFallsBack() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/pkg.zip";
    _downloads[githubUrl] = Zip(("tooth.json", "{}"));

    // Act
    ArchiveHandle archive = await CreateProvider(new Url("https://proxy.example.com"))
        .Open(Ref.Parse(githubUrl));

    // Assert
    Assert.Equal(githubUrl, archive.Key);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() ==
            "https://proxy.example.com/owner/repo/releases/download/v1.0.0/pkg.zip"),
        It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task Open_GitHubUrlProxyReturnsNonZip_FallsBackToOriginal() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/pkg.zip";
    byte[] zip = Zip(("tooth.json", "{}"));
    _downloads["https://proxy.example.com/owner/repo/releases/download/v1.0.0/pkg.zip"] =
        Encoding.UTF8.GetBytes("<html>rate limited</html>");
    _downloads[githubUrl] = zip;

    // Act
    ArchiveHandle archive = await CreateProvider(new Url("https://proxy.example.com"))
        .Open(Ref.Parse(githubUrl));

    // Assert
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), githubUrl), archive.Identity);
  }

  [Fact]
  public async Task Open_UrlLongerThanFileNames_DownloadsOnce() {
    // Arrange
    string url = _url + "?token=" + new string('x', 400);
    _downloads[url] = Zip(("tooth.json", "{}"));

    // Act
    ArchiveHandle first = await CreateProvider().Open(Ref.Parse(url));
    ArchiveHandle second = await CreateProvider().Open(Ref.Parse(url));

    // Assert
    Assert.Equal(first.Identity, second.Identity);
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task Open_UrlDownloadFails_ThrowsArchiveUnavailable() {
    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.Parse(_url)));

    // Assert
    Assert.Contains("download failed", ex.Message);
  }

  [Fact]
  public async Task OpenLocked_CachedZipCorrupted_RedownloadsAndReplacesIt() {
    // Arrange
    byte[] zip = Zip(("tooth.json", "{}"));
    _downloads[_url] = zip;
    ZipArchiveIdentity identity = new(Sha256(zip), _url);
    await CreateProvider().OpenLocked(_url, identity);
    IFileInfo cached = (await _cache.TryGetFile(CacheKeys.Http(_url)))!;
    _fileSystem.File.WriteAllText(cached.FullName, "corrupted");

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(_url, identity);

    // Assert
    Assert.Equal("{}", await Read(archive, "tooth.json"));
    Assert.Equal(zip, _fileSystem.File.ReadAllBytes(cached.FullName));
  }

  [Fact]
  public async Task OpenLocked_CachedZipOtherContent_RedownloadsAndVerifiesHash() {
    // Arrange: the cached copy is a valid ZIP, but not the one the workspace file records.
    byte[] zip = Zip(("tooth.json", "locked"));
    _downloads[_url] = zip;
    _fileSystem.AddFile(
        Path.Combine(LipDirectories.Cache, CacheKeys.Http(_url)),
        new MockFileData(Zip(("tooth.json", "other"))));

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(
        _url, new ZipArchiveIdentity(Sha256(zip), _url));

    // Assert
    Assert.Equal("locked", await Read(archive, "tooth.json"));
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenLocked_ZipNotCached_RedownloadsAndVerifiesHash() {
    // Arrange
    byte[] zip = Zip(("tooth.json", "{}"));
    _downloads[_url] = zip;

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(
        _url, new ZipArchiveIdentity(Sha256(zip).ToUpperInvariant(), _url));

    // Assert
    Assert.Equal("{}", await Read(archive, "tooth.json"));
  }

  [Fact]
  public async Task OpenLocked_ZipHashMismatch_ThrowsArchiveUnavailable() {
    // Arrange
    _downloads[_url] = Zip(("tooth.json", "changed"));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        _url, new ZipArchiveIdentity(new string('0', 64), _url)));
  }

  // Assets (protocol/workspace.md §4 step 2).

  [Fact]
  public async Task OpenAsset_Zip_ReturnsFilesAndIdentifier() {
    // Arrange
    byte[] zip = Zip(("a.dll", "a"), ("lang/en.json", "{}"));
    _downloads[_url] = zip;

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([_url], AssetFormat.Zip);

    // Assert
    Assert.Equal(new AssetIdentity(_url, Sha256(zip)), asset.Identity);
    Assert.Equal(["a.dll", "lang/en.json"], asset.Source.Keys.Order(StringComparer.Ordinal));
    Assert.Equal("a", await Read(asset.Source, "a.dll"));
  }

  [Theory]
  [InlineData("dir\\a.dll", 0)]
  [InlineData("../a.dll", 0)]
  [InlineData("link", unchecked((int)0xA1FF0000))]
  public async Task OpenAsset_ZipWithInvalidEntry_ThrowsArchiveUnavailable(
      string name, int attributes) {
    // Arrange
    _downloads[_url] = Zip((name, "a", attributes), ("b.txt", "b", 0));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([_url], AssetFormat.Zip));
  }

  [Fact]
  public async Task OpenAsset_ZipNotAZip_TriesTheNextUrl() {
    // Arrange
    const string mirror = "https://mirror.example.com/pkg.zip";
    byte[] zip = Zip(("a.txt", "a"));
    _downloads[_url] = Encoding.UTF8.GetBytes("<html>gone</html>");
    _downloads[mirror] = zip;

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([_url, mirror], AssetFormat.Zip);

    // Assert
    Assert.Equal(new AssetIdentity(mirror, Sha256(zip)), asset.Identity);
    Assert.Equal([CacheKeys.Http(mirror)], CachedFiles());
  }

  [Theory]
  [InlineData("http://example.com/pkg.zip")]
  [InlineData("ftp://example.com/pkg.zip")]
  public async Task OpenAsset_ZipNotHttps_IsNotDownloaded(string url) {
    // Arrange
    _downloads[url] = Zip(("a.txt", "a"));

    // Act & Assert
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([url], AssetFormat.Zip));
    Assert.Contains("not an https URL", ex.Message);
    _downloader.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task OpenAsset_ZipFirstUrlNotHttps_UsesTheNext() {
    // Arrange
    const string http = "http://example.com/pkg.zip";
    byte[] zip = Zip(("a.txt", "a"));
    _downloads[http] = Zip(("other.txt", "x"));
    _downloads[_url] = zip;

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([http, _url], AssetFormat.Zip);

    // Assert
    Assert.Equal(new AssetIdentity(_url, Sha256(zip)), asset.Identity);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == http), It.IsAny<IFileInfo>()), Times.Never);
  }

  [Fact]
  public async Task OpenAsset_LegacyArchive_IsNotValidatedLikeZipArchives() {
    // Arrange: format 3 assets are read as lip v0.37.0 read them.
    const string http = "http://example.com/pkg.zip";
    _downloads[http] = Zip(("dir\\a.dll", "a"), ("b.txt", "b"));

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([http], AssetFormat.LegacyArchive);

    // Assert
    Assert.Equal(["dir\\a.dll", "b.txt"], asset.Source.Keys);
    Assert.Equal("a", await Read(asset.Source, "dir\\a.dll"));
  }

  [Fact]
  public async Task OpenAsset_LegacyTgz_OpensItsFiles() {
    // Arrange
    using MemoryStream tgz = new();
    using (GZipStream gzip = new(tgz, CompressionMode.Compress, leaveOpen: true))
    using (TarWriter tar = new(gzip)) {
      TarEntry entry = new UstarTarEntry(TarEntryType.RegularFile, "bdsdown") {
        DataStream = new MemoryStream(Encoding.UTF8.GetBytes("elf")),
      };
      await tar.WriteEntryAsync(entry, TestContext.Current.CancellationToken);
    }

    _downloads[_url] = tgz.ToArray();

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([_url], AssetFormat.LegacyArchive);

    // Assert
    Assert.Equal(["bdsdown"], asset.Source.Keys);
    Assert.Equal("elf", await Read(asset.Source, "bdsdown"));
  }

  [Fact]
  public async Task OpenAsset_TgzAsZip_ThrowsArchiveUnavailable() {
    // Arrange
    using MemoryStream tgz = new();
    using (GZipStream gzip = new(tgz, CompressionMode.Compress, leaveOpen: true))
    using (TarWriter tar = new(gzip)) {
      TarEntry entry = new UstarTarEntry(TarEntryType.RegularFile, "a") {
        DataStream = new MemoryStream(Encoding.UTF8.GetBytes("a")),
      };
      await tar.WriteEntryAsync(entry, TestContext.Current.CancellationToken);
    }

    _downloads[_url] = tgz.ToArray();

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([_url], AssetFormat.Zip));
  }

  [Fact]
  public async Task OpenAsset_LegacyFile_IsOneFileWithEmptyKey() {
    // Arrange
    byte[] content = Encoding.UTF8.GetBytes("not a zip");
    _downloads[_url] = content;

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([_url], AssetFormat.LegacyFile);

    // Assert
    Assert.Equal([""], asset.Source.Keys);
    Assert.Equal("not a zip", await Read(asset.Source, ""));
    Assert.Equal(new AssetIdentity(_url, Sha256(content)), asset.Identity);
  }

  [Fact]
  public async Task OpenAsset_FirstUrlFails_UsesNextAndCachesUnderItsKey() {
    // Arrange
    const string mirror = "https://mirror.example.com/pkg.zip";
    _downloads[mirror] = Zip(("a.txt", "a"));

    // Act
    OpenedAsset first = await CreateProvider().OpenAsset([_url, mirror], AssetFormat.Zip);
    OpenedAsset second = await CreateProvider().OpenAsset([mirror], AssetFormat.Zip);

    // Assert
    Assert.Equal(mirror, first.Identity.Url);
    Assert.Equal(["a.txt"], first.Source.Keys);
    Assert.Equal(["a.txt"], second.Source.Keys);
    Assert.Equal([CacheKeys.Http(mirror)], CachedFiles());
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == mirror), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenAsset_FirstUrlWorks_DoesNotTryTheOthers() {
    // Arrange
    const string mirror = "https://mirror.example.com/pkg.zip";
    _downloads[_url] = Zip(("a.txt", "a"));
    _downloads[mirror] = Zip(("b.txt", "b"));

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([_url, mirror], AssetFormat.Zip);

    // Assert
    Assert.Equal(_url, asset.Identity.Url);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == mirror), It.IsAny<IFileInfo>()), Times.Never);
  }

  [Fact]
  public async Task OpenAsset_GitHubUrlWithProxy_TriesProxyThenFallsBack() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/a.zip";
    _downloads[githubUrl] = Zip(("a.txt", "a"));

    // Act
    OpenedAsset asset = await CreateProvider(new Url("https://proxy.example.com"))
        .OpenAsset([githubUrl], AssetFormat.Zip);

    // Assert
    Assert.Equal(["a.txt"], asset.Source.Keys);
    Assert.Equal(githubUrl, asset.Identity.Url);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() ==
            "https://proxy.example.com/owner/repo/releases/download/v1.0.0/a.zip"),
        It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenAsset_GitHubProxyReturnsNonArchive_FallsBackToOriginal() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/a.zip";
    _downloads["https://proxy.example.com/owner/repo/releases/download/v1.0.0/a.zip"] =
        Encoding.UTF8.GetBytes("<html>error</html>");
    _downloads[githubUrl] = Zip(("a.txt", "a"));

    // Act
    OpenedAsset asset = await CreateProvider(new Url("https://proxy.example.com"))
        .OpenAsset([githubUrl], AssetFormat.Zip);

    // Assert
    Assert.Equal(["a.txt"], asset.Source.Keys);
  }

  [Fact]
  public async Task OpenAsset_CachedArchiveCorrupted_Redownloads() {
    // Arrange
    _downloads[_url] = Zip(("a.txt", "a"));
    _fileSystem.AddFile(
        Path.Combine(LipDirectories.Cache, CacheKeys.Http(_url)), new MockFileData("corrupted"));

    // Act
    OpenedAsset asset = await CreateProvider().OpenAsset([_url], AssetFormat.Zip);

    // Assert
    Assert.Equal(["a.txt"], asset.Source.Keys);
  }

  [Fact]
  public async Task OpenAsset_NonGitHubUrlWithProxy_DoesNotRewrite() {
    // Arrange
    _downloads[_url] = Zip(("a.txt", "a"));

    // Act
    await CreateProvider(new Url("https://proxy.example.com")).OpenAsset([_url], AssetFormat.Zip);

    // Assert
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == _url), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenAsset_EveryUrlFails_ThrowsArchiveUnavailable() {
    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([_url], AssetFormat.Zip));
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([], AssetFormat.Zip));
  }

  [Fact]
  public async Task OpenLockedAsset_Cached_ReturnsItWithoutDownloading() {
    // Arrange
    byte[] zip = Zip(("a.txt", "a"));
    _downloads[_url] = zip;
    await CreateProvider().OpenAsset([_url], AssetFormat.Zip);

    // Act
    ISource source = await CreateProvider().OpenLockedAsset(
        new AssetIdentity(_url, Sha256(zip).ToUpperInvariant()), AssetFormat.Zip);

    // Assert
    Assert.Equal("a", await Read(source, "a.txt"));
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenLockedAsset_CachedOtherContent_RedownloadsAndVerifiesHash() {
    // Arrange
    byte[] zip = Zip(("a.txt", "locked"));
    _downloads[_url] = zip;
    _fileSystem.AddFile(
        Path.Combine(LipDirectories.Cache, CacheKeys.Http(_url)),
        new MockFileData(Zip(("a.txt", "other"))));

    // Act
    ISource source = await CreateProvider().OpenLockedAsset(
        new AssetIdentity(_url, Sha256(zip)), AssetFormat.Zip);

    // Assert
    Assert.Equal("locked", await Read(source, "a.txt"));
  }

  [Fact]
  public async Task OpenLockedAsset_UrlServesOtherContent_ThrowsArchiveUnavailable() {
    // Arrange
    _downloads[_url] = Zip(("a.txt", "changed"));

    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenLockedAsset(
            new AssetIdentity(_url, new string('0', 64)), AssetFormat.Zip));

    // Assert
    Assert.Contains(new string('0', 64), ex.Message);
  }

  [Fact]
  public async Task OpenLockedAsset_LegacyFile_VerifiesHash() {
    // Arrange
    byte[] content = Encoding.UTF8.GetBytes("binary");
    _downloads[_url] = content;

    // Act
    ISource source = await CreateProvider().OpenLockedAsset(
        new AssetIdentity(_url, Sha256(content)), AssetFormat.LegacyFile);

    // Assert
    Assert.Equal("binary", await Read(source, ""));
  }
}
