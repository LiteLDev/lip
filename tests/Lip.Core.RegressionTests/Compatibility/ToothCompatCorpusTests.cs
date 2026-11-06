using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Lip.Core.Entities;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Services;
using Moq;
using Semver;

namespace Lip.Core.RegressionTests.Compatibility;

/// Checks that tooth.json files of format 3 are read through protocol/package.md §5 and installed
/// like lip v3 did: ToothCompat.Derive must map the same merged variant PackageManifest.GetVariant
/// picks, converted as Section 5 converts it, and McmodInstaller must write exactly the files it
/// reports for the derived entry.
public class ToothCompatCorpusTests {
  private static readonly LipEnvironment _environment = new(McmodPlatform.Server, "win-x64");

  private static readonly GitRemoteClient _client = new(githubProxy: null);

  private static readonly IUserInteraction _userInteraction = CreateUserInteraction();

  private static readonly CacheService _cacheService = new(new FileSystem(), _userInteraction);

  private static readonly FileDownloader _fileDownloader = new(_userInteraction);

  private static readonly ArchiveProvider _archiveProvider = new(
      _client, _cacheService, new FileSystem(), _fileDownloader);

  /// The newest version of every LiteLDev package/variant in the corpus, plus every tenth
  /// other (lipr) package/variant. Entries with known upstream breakage are skipped.
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
            Skip = CorpusTests.KnownUpstreamBreakage.ContainsKey($"{e.Package}@{e.Version}")
                ? "upstream breakage"
                : null,
            TestDisplayName = $"{e.Package}@{e.Version}",
          });
    }
  }

  [WinX64Theory]
  [MemberData(nameof(Entries))]
  public async Task Derive_CorpusEntry_MapsGetVariant(string package, string version) {
    // Arrange
    Ref reference = Ref.Parse(package, allowFile: false);
    ArchiveHandle archive = await OpenArchive(
        reference.Target, SemVersion.Parse(version, SemVersionStyles.Strict));
    ToothDocument document = await new ArchiveReader().Read(archive);
    PackageManifest manifest = Assert.IsType<PackageManifest>(document.Legacy);
    PackageManifestVariant? expected;
    try {
      expected = manifest.GetVariant(reference.Variant, _environment.Runtime);
    }
    catch (KeyNotFoundException) {
      expected = null;
    }

    // Act
    ToothEntry? actual = document.ReadFor(_environment).SelectEntry(reference.Variant, _environment);

    // Assert
    if (expected is null || expected.Assets.Any(a => a.Type is not (
        PackageManifestAsset.AssetType.Self or PackageManifestAsset.AssetType.Zip))) {
      Assert.Null(actual);
      return;
    }

    Assert.NotNull(actual);
    Assert.True(actual.Derived);
    Assert.Equal(
        expected.Assets.Select(a => (a.Type.ToString(), string.Join(" ", a.Urls))),
        actual.Assets.Select(a => (a.Type.ToString(), string.Join(" ", a.Urls))));
    Assert.Equal(
        expected.Assets.Select(a => a.Placements
            .Select(ToothCompat.ConvertPlacement)
            .Where(p => ToothParser.IsValidPlacement(p.Src, p.Dest))
            .ToList()),
        actual.Assets.Select(a => a.Placements.Select(p => (p.Src, p.Dest)).ToList()));
    Assert.Equal(
        expected.Dependencies.Select(d => d.Key.ToString()),
        actual.Dependencies.Select(d => d.Source.ToString()));
    Assert.All(actual.Dependencies, d => Assert.True(d.ClientCompat));
    Assert.Equal(
        ToothCompat.PreserveFiles(expected.PreserveFiles.Select(GlobListJsonConverter.Format))
            .Where(p => ToothCompat.TryParseGlob(p) is not null),
        actual.PreserveFiles.Select(g => g.Pattern));
    Assert.Equal(
        ToothCompat.RemoveFiles(expected.RemoveFiles.Select(GlobListJsonConverter.Format))
            .Where(p => ToothCompat.TryParseGlob(p) is not null),
        actual.RemoveFiles.Select(g => g.Pattern));
    Assert.Equal(
        JsonSerializer.Serialize(ToothCompat.ConvertScripts(expected.Scripts)),
        JsonSerializer.Serialize(actual.Hooks));
  }

  [WinX64Theory]
  [InlineData("github.com/LiteLDev/LeviLamina")]
  [InlineData("github.com/GroupMountain/Cleaner")]
  public async Task Install_NewestVersion_WritesExactlyTheReportedFiles(string package) {
    // Arrange
    SpecVersion version = (await _archiveProvider.ListVersions(package))
        .First(v => !v.Version.IsPrerelease);
    ArchiveHandle archive = await _archiveProvider.OpenVersion(package, version);
    ToothDocument document = await new ArchiveReader().Read(archive);
    ToothEntry entry = document.ReadFor(_environment).SelectEntry("", _environment)!;
    FileSystem fs = new();
    McmodInstaller installer = new(fs, new CommandRunner(), _userInteraction);

    string root = Directory.CreateTempSubdirectory("lip-tooth-v4-").FullName;
    try {
      // Act
      PackagePlan plan = await installer.PlanInstall(
          Candidate.Create(
              new Package(document.Tooth, "", version.Version, entry) {
                Manifest = document.ReadFor(_environment).Json,
              },
              archive,
              Ref.Parse(package, allowFile: false),
              _archiveProvider),
          new InstallContext(
              root,
              _environment,
              new LockFile { Platform = "server", Runtime = _environment.Runtime, Packages = [] },
              DryRun: false,
              IgnoreScripts: true),
          new WorkspaceView(fs));
      using (WorkspaceTransaction transaction = new(
          fs, new LockStore(fs).GetJournalPath(root), plan.WrittenPaths)) {
        await installer.Apply(plan, transaction, CancellationToken.None);
        transaction.Commit();
        transaction.Complete();
      }

      IReadOnlyList<string> files = plan.OwnedFiles;

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
