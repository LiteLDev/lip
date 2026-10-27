using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Services;
using Moq;
using Semver;

namespace Lip.Core.RegressionTests.Compatibility;

/// Checks that the v4 tooth.json compatibility layer (spec §9) selects and installs real tooth
/// packages like lip v3: ToothCompat.Select must pick the same merged variant as
/// PackageManifest.GetVariant, and ToothInstaller must write exactly the files it reports.
public class ToothCompatCorpusTests {
  private static readonly LipEnvironment _environment = new(McmodPlatform.Server, "win-x64");

  private static readonly GitRemoteClient _client = new(githubProxy: null);

  private static readonly IUserInteraction _userInteraction = CreateUserInteraction();

  private static readonly CacheService _cacheService = new(new FileSystem(), _userInteraction);

  private static readonly FileDownloader _fileDownloader = new(_userInteraction);

  private static readonly ArchiveProvider _archiveProvider = new(
      _client, _cacheService, new FileSystem(), _fileDownloader);

  private static readonly SourceService _sourceService = new(
      _fileDownloader, _cacheService, githubProxy: null);

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
    Ref reference = Ref.Parse(package);
    ArchiveHandle archive = await OpenArchive(
        reference.Target, SemVersion.Parse(version, SemVersionStyles.Strict));
    ToothArchiveDocument document = Assert.IsType<ToothArchiveDocument>(
        await new ArchiveReader(new Mock<IDocumentParser>().Object).Read(archive));
    PackageManifestVariant? expected;
    try {
      expected = document.Manifest.GetVariant(reference.Variant, _environment.Rid);
    }
    catch (KeyNotFoundException) {
      expected = null;
    }

    // Act
    ToothPackage? actual = (ToothPackage?)new ToothCompat().Select(
        document, reference.Variant, _environment);

    // Assert
    if (expected is null) {
      Assert.Null(actual);
      return;
    }

    Assert.NotNull(actual);
    Assert.Equal(document.Manifest.Path, actual.Identity);
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
  public async Task Install_NewestVersion_WritesExactlyTheReportedFiles(string package) {
    // Arrange
    SpecVersion version = (await _archiveProvider.ListVersions(package))
        .First(v => !v.Version.IsPrerelease);
    ArchiveHandle archive = await _archiveProvider.OpenVersion(package, version);
    ToothArchiveDocument document = Assert.IsType<ToothArchiveDocument>(
        await new ArchiveReader(new Mock<IDocumentParser>().Object).Read(archive));
    IPackage toothPackage = new ToothCompat().Select(document, "", _environment)!;
    ToothInstaller installer = new(
        new CommandRunner(), new FileSystem(), _sourceService, _userInteraction);

    string root = Directory.CreateTempSubdirectory("lip-tooth-v4-").FullName;
    try {
      // Act
      IReadOnlyList<string> files = await installer.Install(
          new Candidate(toothPackage, archive, [new ChainHop(Ref.Parse(package), null)]),
          new InstallContext(
              root, _environment, new LockFile(), DryRun: false, IgnoreScripts: true));

      // Assert
      Assert.NotEmpty(files);
      Assert.Equal(
          files.Order(StringComparer.Ordinal),
          Snapshot(root).Select(f => f.Split(':')[0]));
    }
    finally {
      Directory.Delete(root, recursive: true);
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

  private static async Task<ArchiveHandle> OpenArchive(string spec, SemVersion version) {
    SpecVersion tag = (await _archiveProvider.ListVersions(spec))
        .First(v => v.Version.ComparePrecedenceTo(version) == 0);
    return await _archiveProvider.OpenVersion(spec, tag);
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
