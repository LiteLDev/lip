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

public class ArchiveProviderTests {
  private const string _spec = "example.com/owner/repo";
  private const string _repoUrl = "https://example.com/owner/repo.git";
  private const string _commit = "0123456789abcdef0123456789abcdef01234567";
  private const string _url = "https://example.com/releases/pkg.mcmod";

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

  private static async Task<string> Read(ArchiveHandle archive, string key) {
    using StreamReader reader = new(await archive.Source.OpenRead(key));
    return await reader.ReadToEndAsync();
  }

  private ArchiveHandle OpenGitBase(ArchiveProvider provider) =>
      provider.OpenLocked($"{_spec}@1.0.0", new GitArchiveIdentity(_repoUrl, _commit, ""))
          .GetAwaiter().GetResult();

  [Fact]
  public void OpenWorkspace_ExistingDirectory_ReturnsDirectoryArchive() {
    // Arrange
    _fileSystem.AddFile(Path.Combine(_workspace, "manifest.json"), new MockFileData("{}"));

    // Act
    ArchiveHandle archive =
        CreateProvider().OpenWorkspace(_workspace + Path.DirectorySeparatorChar);

    // Assert
    Assert.Equal(_workspace, archive.Key);
    Assert.Equal(new DirectoryArchiveIdentity(_workspace), archive.Identity);
    Assert.Equal(["manifest.json"], archive.Source.Keys);
  }

  [Fact]
  public void OpenWorkspace_MissingDirectory_ThrowsArchiveUnavailable() {
    // Act & Assert
    Assert.Throws<ArchiveUnavailableException>(() => CreateProvider().OpenWorkspace(_workspace));
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
    SetupTree(_commit, new GitTreeEntry("manifest.json", 100644, AddBlob("{}")));
    ArchiveProvider provider = CreateProvider();
    SpecVersion version = (await provider.ListVersions(_spec)).Single();

    // Act
    ArchiveHandle archive = await provider.OpenVersion(_spec, version);

    // Assert
    Assert.Equal($"{_spec}@1.0.0", archive.Key);
    Assert.Equal(new GitArchiveIdentity(_repoUrl, _commit, ""), archive.Identity);
    Assert.Equal("{}", await Read(archive, "manifest.json"));
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
  public void Open_SymlinkInGitTree_IsNotPartOfArchive() {
    // Arrange
    SetupTree(_commit,
        new GitTreeEntry("manifest.json", 100644, AddBlob("{}")),
        new GitTreeEntry("link", 120000, AddBlob("manifest.json")));

    // Act
    ArchiveHandle archive = OpenGitBase(CreateProvider());

    // Assert
    Assert.Equal(["manifest.json"], archive.Source.Keys);
  }

  [Fact]
  public async Task Open_RelativeDirectoryInGit_OpensGitSubdirectory() {
    // Arrange
    SetupTree(_commit,
        new GitTreeEntry("manifest.json", 100644, AddBlob("root")),
        new GitTreeEntry("lua/manifest.json", 100644, AddBlob("lua")),
        new GitTreeEntry("lua/sub/a.txt", 100644, AddBlob("a")));
    ArchiveProvider provider = CreateProvider();
    ArchiveHandle baseArchive = OpenGitBase(provider);

    // Act
    ArchiveHandle archive = await provider.Open(Ref.Parse("./lua/#x"), baseArchive);
    ArchiveHandle nested = await provider.Open(Ref.Parse("./sub/"), archive);

    // Assert
    Assert.Equal($"{_spec}@1.0.0!lua/", archive.Key);
    Assert.Equal(new GitArchiveIdentity(_repoUrl, _commit, "lua"), archive.Identity);
    Assert.Equal("lua", await Read(archive, "manifest.json"));
    Assert.Equal($"{_spec}@1.0.0!lua/!sub/", nested.Key);
    Assert.Equal(new GitArchiveIdentity(_repoUrl, _commit, "lua/sub"), nested.Identity);
    Assert.Equal(["a.txt"], nested.Source.Keys);
  }

  [Fact]
  public async Task Open_RelativeDirectoryMissing_ThrowsArchiveUnavailable() {
    // Arrange
    SetupTree(_commit, new GitTreeEntry("manifest.json", 100644, AddBlob("{}")));
    ArchiveProvider provider = CreateProvider();

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => provider.Open(Ref.Parse("./missing/"), OpenGitBase(provider)));
  }

  [Fact]
  public async Task Open_RelativeZipInGit_OpensZipByContentWithoutCachingIt() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "inner"), ("dir/", null));
    string zipOid = AddBlob(zip);
    SetupTree(_commit,
        new GitTreeEntry("manifest.json", 100644, AddBlob("root")),
        new GitTreeEntry("dist/pkg.bin", 100644, zipOid));
    ArchiveProvider provider = CreateProvider();

    // Act
    ArchiveHandle archive = await provider.Open(Ref.Parse("./dist/pkg.bin"), OpenGitBase(provider));

    // Assert
    Assert.Equal($"{_spec}@1.0.0!dist/pkg.bin", archive.Key);
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), null), archive.Identity);
    Assert.Equal("inner", await Read(archive, "manifest.json"));
    Assert.Equal(
        [CacheKeys.GitBlob(zipOid)],
        _fileSystem.Directory.GetFiles(LipDirectories.Cache).Select(Path.GetFileName));
  }

  [Fact]
  public async Task OpenLocked_NestedZip_ThrowsArchiveUnavailableToReopenItsParent() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "inner"));
    SetupTree(_commit, new GitTreeEntry("pkg.mcmod", 100644, AddBlob(zip)));
    ArchiveProvider provider = CreateProvider();
    ArchiveHandle archive = await provider.Open(Ref.Parse("./pkg.mcmod"), OpenGitBase(provider));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => provider.OpenLocked(archive.Key, archive.Identity));
  }

  [Fact]
  public async Task Open_RelativeFileNotZip_ThrowsArchiveUnavailable() {
    // Arrange
    SetupTree(_commit, new GitTreeEntry("pkg.mcmod", 100644, AddBlob("not a zip")));
    ArchiveProvider provider = CreateProvider();

    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => provider.Open(Ref.Parse("./pkg.mcmod"), OpenGitBase(provider)));

    // Assert
    Assert.Contains("not a ZIP archive", ex.Message);
  }

  [Theory]
  [InlineData("missing.mcmod", "does not exist")]
  [InlineData("lua", "is a directory")]
  public async Task Open_RelativeFileUnavailable_ThrowsWithReason(string path, string reason) {
    // Arrange
    SetupTree(_commit, new GitTreeEntry("lua/manifest.json", 100644, AddBlob("{}")));
    ArchiveProvider provider = CreateProvider();

    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => provider.Open(Ref.Parse("./" + path), OpenGitBase(provider)));

    // Assert
    Assert.Contains(reason, ex.Message);
  }

  [Fact]
  public async Task Open_RelativeDirectoryInZip_OpensPrefixViewAndReopensLocked() {
    // Arrange
    byte[] zip = Zip(
        ("manifest.json", "root"), ("a/manifest.json", "a"), ("a/b/manifest.json", "b"));
    _downloads[_url] = zip;
    ArchiveProvider provider = CreateProvider();
    ArchiveHandle baseArchive = await provider.Open(Ref.Parse(_url), null!);

    // Act
    ArchiveHandle a = await provider.Open(Ref.Parse("./a/"), baseArchive);
    ArchiveHandle b = await provider.Open(Ref.Parse("./b/"), a);
    ArchiveHandle locked = await provider.OpenLocked(b.Key, b.Identity);

    // Assert
    Assert.Equal($"{_url}!a/!b/", b.Key);
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), _url) { Path = "a/b" }, b.Identity);
    Assert.Equal(["b/manifest.json", "manifest.json"], a.Source.Keys.Order(StringComparer.Ordinal));
    Assert.Equal("b", await Read(b, "manifest.json"));
    Assert.Equal("b", await Read(locked, "manifest.json"));
  }

  [Fact]
  public async Task Open_RelativeInDirectory_OpensDirectoryAndZip() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "zipped"));
    _fileSystem.AddFile(Path.Combine(_workspace, "manifest.json"), new MockFileData("root"));
    _fileSystem.AddFile(Path.Combine(_workspace, "sub", "manifest.json"), new MockFileData("sub"));
    _fileSystem.AddFile(Path.Combine(_workspace, "sub", "pkg.zip"), new MockFileData(zip));
    ArchiveProvider provider = CreateProvider();
    ArchiveHandle workspace = provider.OpenWorkspace(_workspace);

    // Act
    ArchiveHandle sub = await provider.Open(Ref.Parse("./sub/"), workspace);
    ArchiveHandle nested = await provider.Open(Ref.Parse("./pkg.zip"), sub);

    // Assert
    Assert.Equal(new DirectoryArchiveIdentity(Path.Combine(_workspace, "sub")), sub.Identity);
    Assert.Equal($"{_workspace}!sub/", sub.Key);
    Assert.Equal("sub", await Read(sub, "manifest.json"));
    Assert.Equal($"{_workspace}!sub/!pkg.zip", nested.Key);
    Assert.Equal("zipped", await Read(nested, "manifest.json"));
  }

  [Theory]
  [InlineData("../evil.txt", 0)]
  [InlineData("a/../../evil.txt", 0)]
  [InlineData("/abs.txt", 0)]
  [InlineData("C:/evil.txt", 0)]
  [InlineData("a\\b.txt", 0)]
  [InlineData("link", unchecked((int)0xA1FF0000))]
  public async Task Open_ZipWithInvalidEntry_ThrowsArchiveUnavailable(string name, int attributes) {
    // Arrange
    _downloads[_url] = Zip(("manifest.json", "{}", 0), (name, "x", attributes));

    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.Parse(_url), null!));

    // Assert
    Assert.Contains("not a valid archive", ex.Message);
  }

  [Fact]
  public async Task Open_ZipWithUnixRegularFiles_Succeeds() {
    // Arrange
    _downloads[_url] = Zip(
        ("manifest.json", "{}", unchecked((int)0x81A40000)), ("dir/", null, 0x41ED0000));

    // Act
    ArchiveHandle archive = await CreateProvider().Open(Ref.Parse(_url), null!);

    // Assert
    Assert.Equal(["manifest.json"], archive.Source.Keys);
  }

  [Fact]
  public async Task Open_Url_DownloadsOnceAndIdentifiesBySha256() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "{}"));
    _downloads[_url] = zip;

    // Act
    ArchiveHandle first = await CreateProvider().Open(Ref.Parse(_url + "#variant"), null!);
    ArchiveHandle second = await CreateProvider().Open(Ref.Parse(_url), null!);

    // Assert
    Assert.Equal(_url, first.Key);
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), _url), first.Identity);
    Assert.Equal(first.Identity, second.Identity);
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task Open_Url_CachesOnlyUnderHttpKeyOfUrl() {
    // Arrange
    _downloads[_url] = Zip(("manifest.json", "{}"));

    // Act
    await CreateProvider().Open(Ref.Parse(_url), null!);

    // Assert
    Assert.Equal(
        [CacheKeys.Http(_url)],
        _fileSystem.Directory.GetFiles(LipDirectories.Cache).Select(Path.GetFileName));
    Assert.Empty(_fileSystem.Directory.GetFiles(LipDirectories.Staging));
  }

  [Fact]
  public async Task Open_CachedUrlCorrupted_Redownloads() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "{}"));
    _downloads[_url] = zip;
    _fileSystem.AddFile(
        Path.Combine(LipDirectories.Cache, CacheKeys.Http(_url)), new MockFileData("corrupted"));

    // Act
    ArchiveHandle archive = await CreateProvider().Open(Ref.Parse(_url), null!);

    // Assert
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), _url), archive.Identity);
    Assert.Equal("{}", await Read(archive, "manifest.json"));
  }

  [Fact]
  public async Task Open_GitHubUrlWithProxy_TriesProxyThenFallsBack() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/pkg.mcmod";
    _downloads[githubUrl] = Zip(("manifest.json", "{}"));

    // Act
    ArchiveHandle archive = await CreateProvider(new Url("https://proxy.example.com"))
        .Open(Ref.Parse(githubUrl), null!);

    // Assert
    Assert.Equal(githubUrl, archive.Key);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() ==
            "https://proxy.example.com/owner/repo/releases/download/v1.0.0/pkg.mcmod"),
        It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task Open_GitHubUrlProxyReturnsNonZip_FallsBackToOriginal() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/pkg.mcmod";
    byte[] zip = Zip(("manifest.json", "{}"));
    _downloads["https://proxy.example.com/owner/repo/releases/download/v1.0.0/pkg.mcmod"] =
        Encoding.UTF8.GetBytes("<html>rate limited</html>");
    _downloads[githubUrl] = zip;

    // Act
    ArchiveHandle archive = await CreateProvider(new Url("https://proxy.example.com"))
        .Open(Ref.Parse(githubUrl), null!);

    // Assert
    Assert.Equal(new ZipArchiveIdentity(Sha256(zip), githubUrl), archive.Identity);
  }

  [Fact]
  public async Task Open_UrlLongerThanFileNames_DownloadsOnce() {
    // Arrange
    string url = _url + "?token=" + new string('x', 400);
    _downloads[url] = Zip(("manifest.json", "{}"));

    // Act
    ArchiveHandle first = await CreateProvider().Open(Ref.Parse(url), null!);
    ArchiveHandle second = await CreateProvider().Open(Ref.Parse(url), null!);

    // Assert
    Assert.Equal(first.Identity, second.Identity);
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenLocked_CachedZipCorrupted_RedownloadsAndReplacesIt() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "{}"));
    _downloads[_url] = zip;
    ZipArchiveIdentity identity = new(Sha256(zip), _url);
    await CreateProvider().OpenLocked(_url, identity);
    IFileInfo cached = (await _cache.TryGetFile(CacheKeys.Http(_url)))!;
    _fileSystem.File.WriteAllText(cached.FullName, "corrupted");

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(_url, identity);

    // Assert
    Assert.Equal("{}", await Read(archive, "manifest.json"));
    Assert.Equal(zip, _fileSystem.File.ReadAllBytes(cached.FullName));
  }

  [Fact]
  public async Task OpenLocked_CachedZipOtherContent_RedownloadsAndVerifiesHash() {
    // Arrange: the cached copy is a valid ZIP, but not the one the lock records.
    byte[] zip = Zip(("manifest.json", "locked"));
    _downloads[_url] = zip;
    _fileSystem.AddFile(
        Path.Combine(LipDirectories.Cache, CacheKeys.Http(_url)),
        new MockFileData(Zip(("manifest.json", "other"))));

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(
        _url, new ZipArchiveIdentity(Sha256(zip), _url));

    // Assert
    Assert.Equal("locked", await Read(archive, "manifest.json"));
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenLocked_GitPathMissing_ThrowsArchiveUnavailable() {
    // Arrange
    SetupTree(_commit, new GitTreeEntry("manifest.json", 100644, AddBlob("{}")));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        "k", new GitArchiveIdentity(_repoUrl, _commit, "missing")));
  }

  [Fact]
  public async Task Open_UrlDownloadFails_ThrowsArchiveUnavailable() {
    // Act
    ArchiveUnavailableException ex = await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().Open(Ref.Parse(_url), null!));

    // Assert
    Assert.Contains("download failed", ex.Message);
  }

  [Fact]
  public async Task OpenLocked_ZipNotCached_RedownloadsAndVerifiesHash() {
    // Arrange
    byte[] zip = Zip(("manifest.json", "{}"));
    _downloads[_url] = zip;

    // Act
    ArchiveHandle archive = await CreateProvider().OpenLocked(
        _url, new ZipArchiveIdentity(Sha256(zip).ToUpperInvariant(), _url));

    // Assert
    Assert.Equal("{}", await Read(archive, "manifest.json"));
  }

  [Fact]
  public async Task OpenLocked_ZipHashMismatch_ThrowsArchiveUnavailable() {
    // Arrange
    _downloads[_url] = Zip(("manifest.json", "changed"));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        _url, new ZipArchiveIdentity(new string('0', 64), _url)));
  }

  [Fact]
  public async Task OpenLocked_ZipNotCachedWithoutUrl_ThrowsArchiveUnavailable() {
    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        "x!pkg.mcmod", new ZipArchiveIdentity(new string('0', 64), null)));
  }

  [Fact]
  public async Task OpenLocked_DirectoryMissing_ThrowsArchiveUnavailable() {
    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        _workspace, new DirectoryArchiveIdentity(_workspace)));
  }

  [Fact]
  public async Task OpenLocked_GitCommitUnavailable_ThrowsArchiveUnavailable() {
    // Arrange
    _git.Setup(c => c.ListTree(It.IsAny<Url>(), It.IsAny<string>()))
        .ThrowsAsync(new GitProtocolException("not our ref"));

    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(() => CreateProvider().OpenLocked(
        "k", new GitArchiveIdentity(_repoUrl, _commit, "")));
  }

  [Fact]
  public async Task OpenAsset_Zip_IsNotValidatedLikeUrlArchives() {
    // Arrange
    _downloads[_url] = Zip(("dir\\a.dll", "a"), ("b.txt", "b"));

    // Act
    ISource source = await CreateProvider().OpenAsset([_url], isArchive: true);

    // Assert
    Assert.Equal(["dir\\a.dll", "b.txt"], source.Keys);
    Assert.Equal("a", await Read(source, "dir\\a.dll"));
  }

  [Fact]
  public async Task OpenAsset_Tgz_OpensItsFiles() {
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
    ISource source = await CreateProvider().OpenAsset([_url], isArchive: true);

    // Assert
    Assert.Equal(["bdsdown"], source.Keys);
    Assert.Equal("elf", await Read(source, "bdsdown"));
  }

  [Fact]
  public async Task OpenAsset_Uncompressed_IsOneFileWithEmptyKey() {
    // Arrange
    _downloads[_url] = Encoding.UTF8.GetBytes("not a zip");

    // Act
    ISource source = await CreateProvider().OpenAsset([_url], isArchive: false);

    // Assert
    Assert.Equal([""], source.Keys);
    Assert.Equal("not a zip", await Read(source, ""));
  }

  [Fact]
  public async Task OpenAsset_FirstUrlFails_UsesNextAndCachesUnderItsKey() {
    // Arrange
    const string mirror = "https://mirror.example.com/pkg.zip";
    _downloads[mirror] = Zip(("a.txt", "a"));

    // Act
    ISource first = await CreateProvider().OpenAsset([_url, mirror], isArchive: true);
    ISource second = await CreateProvider().OpenAsset([mirror], isArchive: true);

    // Assert
    Assert.Equal(["a.txt"], first.Keys);
    Assert.Equal(["a.txt"], second.Keys);
    Assert.Equal(
        [CacheKeys.Http(mirror)],
        _fileSystem.Directory.GetFiles(LipDirectories.Cache).Select(Path.GetFileName));
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == mirror), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenAsset_GitHubUrlWithProxy_TriesProxyThenFallsBack() {
    // Arrange
    const string githubUrl = "https://github.com/owner/repo/releases/download/v1.0.0/a.zip";
    _downloads[githubUrl] = Zip(("a.txt", "a"));

    // Act
    ISource source = await CreateProvider(new Url("https://proxy.example.com"))
        .OpenAsset([githubUrl], isArchive: true);

    // Assert
    Assert.Equal(["a.txt"], source.Keys);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() ==
            "https://proxy.example.com/owner/repo/releases/download/v1.0.0/a.zip"),
        It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenAsset_NonGitHubUrlWithProxy_DoesNotRewrite() {
    // Arrange
    _downloads[_url] = Zip(("a.txt", "a"));

    // Act
    await CreateProvider(new Url("https://proxy.example.com")).OpenAsset([_url], isArchive: true);

    // Assert
    _downloader.Verify(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<IFileInfo>()), Times.Once);
    _downloader.Verify(d => d.DownloadFile(
        It.Is<Url>(u => u.ToString() == _url), It.IsAny<IFileInfo>()), Times.Once);
  }

  [Fact]
  public async Task OpenAsset_EveryUrlFails_ThrowsArchiveUnavailable() {
    // Act & Assert
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([_url], isArchive: true));
    await Assert.ThrowsAsync<ArchiveUnavailableException>(
        () => CreateProvider().OpenAsset([], isArchive: true));
  }

  private static async Task<string> Read(ISource source, string key) {
    using StreamReader reader = new(await source.OpenRead(key));
    return await reader.ReadToEndAsync();
  }
}
