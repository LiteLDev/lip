using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Core.Tests.Mcmod.Install;

/// Installing and uninstalling in a workspace that is the root of a drive or of the file system:
/// every path in the workspace is below its root.
public class McmodInstallerRootTests {
  private const string Pack = "example.com/pack";

  private static readonly LipEnvironment _server = new(McmodSide.Server, "win-x64");

  private readonly MockFileSystem _fs = new();
  private readonly Mock<ICommandRunner> _commandRunner = new();
  private readonly Mock<IUserInteraction> _ui = new();
  private readonly Mock<IArchiveProvider> _provider = new();
  private readonly SoloInstaller _installer;

  public McmodInstallerRootTests() {
    _ui.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    _ui.Setup(u => u.PrintWarning(It.IsAny<string>())).Returns(Task.CompletedTask);
    _installer = new SoloInstaller(
        _fs, new McmodInstaller(_fs, _commandRunner.Object, _ui.Object));
  }

  /// The roots a workspace may be on the platform the tests run on.
  public static TheoryData<string> Roots() =>
      OperatingSystem.IsWindows() ? new TheoryData<string>(@"C:\", @"D:\") : new("/");

  private string Full(string root, string relative) =>
      _fs.Path.Combine(root, relative.Replace('/', _fs.Path.DirectorySeparatorChar));

  private static PackageEntry Entry(string members) {
    using JsonDocument json = JsonDocument.Parse(
        $$"""{ "side": "server", "platform": "win-x64", {{members}} }""");
    return PackageFileParser.ParseEntry(json.RootElement);
  }

  private Candidate Candidate(string members, Dictionary<string, string> files) {
    PackageEntry entry = Entry(members);
    Package package = new(Pack, "", SemVersion.Parse("1.0.0"), entry) {
      Manifest = Resolution.TestCandidates.Manifest(Pack, "1.0.0", "", entry),
    };
    ArchiveHandle archive = new($"{Pack}@1.0.0", new InMemorySource(files), new string('0', 40));
    return Lip.Core.Mcmod.Candidate.Create(
        package, archive, Ref.Parse(Pack, allowFile: false), _provider.Object);
  }

  private static InstallContext Context(string root) =>
      new(root, _server, new LockFile { Side = "server", Platform = "win-x64", Packages = [] }, false);

  private static LockedPackage Locked(Candidate candidate, IEnumerable<string> files) => new() {
    Manifest = candidate.Package.Manifest,
    Variant = candidate.Package.Variant,
    Explicit = true,
    Source = candidate.Source.ToString(),
    Commit = candidate.Archive.Commit,
    Files = [.. files],
  };

  private const string Placements = """
      "assets": [ { "type": "self", "place": [
        { "src": "bedrock_server.exe", "dest": "bedrock_server.exe" },
        { "src": "behavior_packs/", "dest": "behavior_packs/pack/" }
      ] } ],
      "purge": [ "logs/" ]
      """;

  private static readonly Dictionary<string, string> _files = new() {
    ["bedrock_server.exe"] = "exe",
    ["behavior_packs/manifest.json"] = "{}",
    ["behavior_packs/scripts/main.js"] = "js",
  };

  [Theory]
  [MemberData(nameof(Roots))]
  public async Task Install_WorkspaceIsARoot_WritesBelowIt(string root) {
    // Arrange
    _fs.AddDirectory(root);
    Candidate candidate = Candidate(Placements, _files);

    // Act
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context(root));

    // Assert
    Assert.Equal(
        ["bedrock_server.exe", "behavior_packs/pack/manifest.json",
          "behavior_packs/pack/scripts/main.js"],
        owned.Order(StringComparer.Ordinal));
    Assert.Equal("exe", _fs.File.ReadAllText(Full(root, "bedrock_server.exe")));
    Assert.Equal("js", _fs.File.ReadAllText(Full(root, "behavior_packs/pack/scripts/main.js")));
  }

  [Theory]
  [MemberData(nameof(Roots))]
  public async Task Uninstall_WorkspaceIsARoot_DeletesOwnedAndPurgedFilesAndPrunes(string root) {
    // Arrange
    _fs.AddDirectory(root);
    Candidate candidate = Candidate(Placements, _files);
    IReadOnlyList<string> owned = await _installer.Install(candidate, Context(root));
    _fs.AddFile(Full(root, "logs/server.log"), new MockFileData("log"));
    _fs.AddFile(Full(root, "behavior_packs/other/manifest.json"), new MockFileData("{}"));

    // Act
    await _installer.Uninstall(Locked(candidate, owned), Context(root));

    // Assert
    Assert.False(_fs.File.Exists(Full(root, "bedrock_server.exe")));
    Assert.False(_fs.Directory.Exists(Full(root, "behavior_packs/pack")));
    Assert.False(_fs.Directory.Exists(Full(root, "logs")));
    Assert.True(_fs.File.Exists(Full(root, "behavior_packs/other/manifest.json")));
    Assert.True(_fs.Directory.Exists(root));
  }

  /// The files of an archive, held in memory.
  private sealed class InMemorySource(Dictionary<string, string> files) : ISource {
    public IEnumerable<string> Keys => files.Keys;

    public Task<Stream> OpenRead(string key) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(files[key])));

    public Task Prefetch(IEnumerable<string> keys) => Task.CompletedTask;
  }
}
