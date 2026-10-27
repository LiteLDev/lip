using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.PackageRegistries;
using Lip.Core.Services;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.RegressionTests.Compatibility;

/// Checks that the v4 tooth.json compatibility layer (spec §9) selects and installs real tooth
/// packages exactly like the v3 code: ToothCompat.Select must pick the same merged variant as
/// PackageManifest.GetVariant, and ToothInstaller must write the same files as PackageInstaller.
public class ToothCompatCorpusTests {
  private static readonly LipEnvironment _environment = new(McmodPlatform.Server, "win-x64");

  private static readonly GitRemoteClient _client = new(githubProxy: null);

  private static readonly GitRemotePackageRegistry _registry = new(_client);

  private static readonly IUserInteraction _userInteraction = CreateUserInteraction();

  private static readonly CacheService _cacheService = new(new FileSystem(), _userInteraction);

  private static readonly SourceService _sourceService = new(
      new FileDownloader(_userInteraction),
      _client,
      _registry,
      _userInteraction,
      _cacheService,
      githubProxy: null);

  /// The newest version of every LiteLDev package/variant in the corpus, plus every tenth
  /// other (lipr) package/variant.
  public static IEnumerable<TheoryDataRow<string, string>> Entries {
    get {
      List<(string Package, string Version)> newest = [.. LoadNewestEntries()];
      List<(string Package, string Version)> liteLDev = [
        .. newest.Where(e => e.Package.StartsWith(
            "github.com/LiteLDev/", StringComparison.Ordinal)),
      ];
      List<(string Package, string Version)> others = [
        .. newest.Except(liteLDev).Where((_, i) => i % 10 == 0),
      ];

      return liteLDev.Concat(others).Select(
          e => new TheoryDataRow<string, string>(e.Package, e.Version) {
            TestDisplayName = $"{e.Package}@{e.Version}",
          });
    }
  }

  [WinX64Theory]
  [MemberData(nameof(Entries))]
  public async Task Select_CorpusEntry_MatchesGetVariant(string package, string version) {
    // Arrange
    PackageId packageId = PackageId.Parse(package);
    PackageSpec spec = new(packageId, SemVersion.Parse(version, SemVersionStyles.Strict));
    PackageManifest baselineManifest = await _registry.GetPackageManifest(spec);
    PackageManifestVariant? expected;
    try {
      expected = baselineManifest.GetVariant(packageId.Variant);
    }
    catch (KeyNotFoundException) {
      expected = null;
    }

    ArchiveHandle archive = await OpenArchive(spec);
    ToothArchiveDocument document = Assert.IsType<ToothArchiveDocument>(
        await new ArchiveReader(new Mock<IDocumentParser>().Object).Read(archive));

    // Act
    ToothPackage? actual = (ToothPackage?)new ToothCompat().Select(
        document, packageId.Variant, _environment);

    // Assert
    if (expected is null) {
      Assert.Null(actual);
      return;
    }

    Assert.NotNull(actual);
    Assert.Equal(baselineManifest.Path, actual.Identity);
    Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual.Variant));
    Assert.Equal(
        expected.Dependencies.Select(d => (d.Key.ToString(), d.Value.ToString())),
        actual.InstallEdges.Select(e => (
            Assert.Single(e.Refs).ToString(),
            ((SemVersionRangeConstraint)e.Version!).Range.ToString())));
  }

  [WinX64Theory]
  [InlineData("github.com/LiteLDev/LeviLamina")]
  [InlineData("github.com/GroupMountain/Cleaner")]
  public async Task Install_NewestVersion_WritesSameFilesAsPackageInstaller(string package) {
    // Arrange
    PackageId packageId = PackageId.Parse(package);
    SemVersion version = (await _registry.GetAvailableVersions(packageId))
        .Where(v => !v.IsPrerelease)
        .Max(SemVersion.PrecedenceComparer)!;
    PackageSpec spec = new(packageId, version);

    string root = Directory.CreateTempSubdirectory("lip-tooth-v4-").FullName;
    string baselineRoot = Directory.CreateTempSubdirectory("lip-tooth-v3-").FullName;
    try {
      // Install with the v3 PackageInstaller (relative to the working directory).
      List<string> baselineFiles = [];
      Mock<IWorkspaceService> workspace = new();
      workspace.Setup(w => w.GetInstalledPackages(IWorkspaceService.PackageScope.All))
          .ReturnsAsync([]);
      workspace.Setup(w => w.AddInstalledPackage(
              It.IsAny<PackageSpec>(),
              It.IsAny<PackageManifest>(),
              It.IsAny<IEnumerable<IFileInfo>>(),
              It.IsAny<bool>()))
          .Callback((PackageSpec _, PackageManifest _, IEnumerable<IFileInfo> files, bool _) =>
              baselineFiles.AddRange(files.Select(f => f.FullName)))
          .Returns(Task.CompletedTask);
      PackageInstaller baseline = new(
          new CommandRunner(),
          new FileSystem(),
          _userInteraction,
          _sourceService,
          workspace.Object);

      string previousDirectory = Directory.GetCurrentDirectory();
      Directory.SetCurrentDirectory(baselineRoot);
      try {
        await baseline.InstallPackage(
            new PackageArtifact(spec, await _sourceService.Get(spec)),
            dryRun: false,
            explicitInstall: true,
            ignoreScripts: true);
      }
      finally {
        Directory.SetCurrentDirectory(previousDirectory);
      }

      // Install with ToothInstaller.
      ArchiveHandle archive = await OpenArchive(spec);
      ToothArchiveDocument document = Assert.IsType<ToothArchiveDocument>(
          await new ArchiveReader(new Mock<IDocumentParser>().Object).Read(archive));
      IPackage toothPackage = new ToothCompat().Select(document, packageId.Variant, _environment)!;
      ToothInstaller installer = new(
          new CommandRunner(), new FileSystem(), _sourceService, _userInteraction);

      // Act
      IReadOnlyList<string> files = await installer.Install(
          new Candidate(toothPackage, archive, [new ChainHop(Ref.Parse(package), null)]),
          new InstallContext(
              root, _environment, new LockFile(), DryRun: false, IgnoreScripts: true));

      // Assert
      Assert.NotEmpty(files);
      Assert.Equal(
          baselineFiles
              .Select(f => Path.GetRelativePath(baselineRoot, f).Replace('\\', '/'))
              .Distinct()
              .Order(StringComparer.Ordinal),
          files.Order(StringComparer.Ordinal));
      Assert.Equal(Snapshot(baselineRoot), Snapshot(root));
    }
    finally {
      Directory.Delete(root, recursive: true);
      Directory.Delete(baselineRoot, recursive: true);
    }
  }

  /// Prints nothing, but runs the actions passed to RunWithProgress (file downloads).
  private static IUserInteraction CreateUserInteraction() {
    Mock<IUserInteraction> userInteraction = new();
    userInteraction.Setup(u => u.RunWithProgress(
            It.IsAny<string>(), It.IsAny<Func<IProgress<double>, Task>>()))
        .Returns((string _, Func<IProgress<double>, Task> action) =>
            action(new Progress<double>()));
    return userInteraction.Object;
  }

  private static async Task<ArchiveHandle> OpenArchive(PackageSpec spec) {
    string commit = await _registry.ResolveCommit(spec);
    string repoUrl = GitRemotePackageRegistry.GetRepoUrl(spec.Id).ToString();
    GitTreeSource source = await GitTreeSource.Create(_client, _cacheService, repoUrl, commit);
    return new ArchiveHandle(
        $"{spec.Id.Path}@{spec.Version}", source, new GitArchiveIdentity(repoUrl, commit, ""));
  }

  private static List<string> Snapshot(string root) => [
    .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(f => $"{Path.GetRelativePath(root, f).Replace('\\', '/')}:"
            + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))
        .Order(StringComparer.Ordinal),
  ];

  private static IEnumerable<(string Package, string Version)> LoadNewestEntries() {
    string path = Path.Join(AppContext.BaseDirectory, "Compatibility", "corpus.json");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

    return document.RootElement.GetProperty("entries").EnumerateArray()
        .Where(e => e.GetProperty("baseline_installable").GetBoolean()
            && e.GetProperty("git_tag").GetBoolean()
            && !e.GetProperty("risk").GetBoolean())
        .Select(e => (
            Package: e.GetProperty("package").GetString()!,
            Version: e.GetProperty("version").GetString()!))
        .GroupBy(e => e.Package)
        .Select(g => g.MaxBy(
            e => SemVersion.Parse(e.Version, SemVersionStyles.Strict),
            SemVersion.PrecedenceComparer))
        .OrderBy(e => e.Package, StringComparer.Ordinal)
        .ToList();
  }
}
