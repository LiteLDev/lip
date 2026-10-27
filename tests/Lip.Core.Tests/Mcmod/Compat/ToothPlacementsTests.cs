using DotNet.Globbing;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Semver;

namespace Lip.Core.Tests.Mcmod.Compat;

/// The v3 placement matching (formerly PlacementMatcher) that the tooth.json conversion keeps.
public class ToothPlacementsTests {
  private static AssetPlacement File(string src, string dst) => new("0", false, src, dst);

  private static AssetPlacement Dir(string src, string dst) => new("0", true, src, dst);

  private static List<(string Key, string TargetPath)> Match(
      IEnumerable<string> keys,
      params AssetPlacement[] placements) => [.. ToothPlacements.Match(keys, placements)];

  [Fact]
  public void Match_FileExact_PlacesAtDst() {
    List<(string, string)> result = Match(["a.txt", "b.txt"], File("a.txt", "plugins/x.txt"));

    Assert.Equal([("a.txt", "plugins/x.txt")], result);
  }

  [Fact]
  public void Match_FileExactNested_PlacesAtDst() {
    List<(string, string)> result = Match(["bin/sub/a.dll"], File("bin/sub/a.dll", "plugins/a/a.dll"));

    Assert.Equal([("bin/sub/a.dll", "plugins/a/a.dll")], result);
  }

  [Fact]
  public void Match_FileGlob_PlacesUnderDstWithFileName() {
    List<(string, string)> result = Match(
        ["a.dll", "b.pdb", "c.dll"],
        File("*.dll", "plugins/p"));

    Assert.Equal(
        [("a.dll", Path.Combine("plugins/p", "a.dll")), ("c.dll", Path.Combine("plugins/p", "c.dll"))],
        result);
  }

  [Fact]
  public void Match_FileGlobDoubleStar_FlattensNestedFiles() {
    List<(string, string)> result = Match(
        ["bin/a.dll", "bin/sub/b.dll", "other/c.dll"],
        File("bin/**", "plugins/p"));

    Assert.Equal(
        [("bin/a.dll", Path.Combine("plugins/p", "a.dll")), ("bin/sub/b.dll", Path.Combine("plugins/p", "b.dll"))],
        result);
  }

  [Fact]
  public void Match_FileNoMatch_ReturnsEmpty() {
    Assert.Empty(Match(["a.txt"], File("b.txt", "plugins/b.txt")));
  }

  [Fact]
  public void Match_Dir_PlacesRelativeToSrc() {
    List<(string, string)> result = Match(
        ["res/a.json", "res/sub/b.json", "resources/c.json", "d.json"],
        Dir("res", "plugins/p/res"));

    Assert.Equal(
        [
          ("res/a.json", Path.Combine("plugins/p/res", Path.GetRelativePath("res", "res/a.json"))),
          ("res/sub/b.json", Path.Combine("plugins/p/res", Path.GetRelativePath("res", "res/sub/b.json"))),
        ],
        result);
  }

  [Fact]
  public void Match_DirEmptySrc_MatchesEverything() {
    List<(string, string)> result = Match(["a.txt", "sub/b.txt"], Dir("", "plugins/p"));

    Assert.Equal(
        [
          ("a.txt", Path.Combine("plugins/p", "a.txt")),
          ("sub/b.txt", Path.Combine("plugins/p", Path.GetRelativePath(".", "sub/b.txt"))),
        ],
        result);
  }

  [Fact]
  public void Match_DirDotSrc_MatchesEverything() {
    Assert.Equal(Match(["a.txt", "sub/b.txt"], Dir("", "plugins/p")), Match(["a.txt", "sub/b.txt"], Dir(".", "plugins/p")));
  }

  [Fact]
  public void Match_MultiplePlacements_ReturnsAllTargetsInOrder() {
    List<(string, string)> result = Match(
        ["a.dll", "b.txt"],
        File("a.dll", "plugins/one/a.dll"),
        File("*.dll", "plugins/two"),
        Dir("", "plugins/all"));

    Assert.Equal(
        [
          ("a.dll", "plugins/one/a.dll"),
          ("a.dll", Path.Combine("plugins/two", "a.dll")),
          ("a.dll", Path.Combine("plugins/all", "a.dll")),
          ("b.txt", Path.Combine("plugins/all", "b.txt")),
        ],
        result);
  }

  [Fact]
  public void Match_InvalidGlobNeverParsedForExactMatchOrNoKeys() {
    // An exact match never parses the glob, matching the original behavior.
    Assert.Equal([("[", "x")], Match(["["], File("[", "x")));
    Assert.Empty(Match([], File("[", "x")));
  }

  [Fact]
  public void Match_BackslashKeys_MatchLegacy() {
    string[] keys = ["bin\\a.dll", "res\\sub\\b.json", "bin/c.dll"];
    AssetPlacement[] placements = [
      File("bin/**", "plugins/p"),
      File("bin\\a.dll", "plugins/exact.dll"),
      Dir("res", "plugins/res"),
      Dir("bin", "plugins/bin"),
    ];

    Assert.Equal(LegacyMatch(keys, placements), Match(keys, placements));
  }

  /// Compares against a verbatim copy of the matching logic previously inlined in
  /// PackageInstaller.InstallPackage, over every key and placement combination.
  [Fact]
  public void Match_EquivalentToLegacyInstallerLogic() {
    string[] keys = [
      "tooth.json",
      "README.md",
      "a.dll",
      "a.pdb",
      "bin/a.dll",
      "bin/sub/b.dll",
      "bin\\c.dll",
      "bin",
      "binary/x.dll",
      "res/data/cfg.json",
      "res\\data\\other.json",
      "res",
      "./res/dot.json",
      "..hidden/x",
      "docs/a b.md",
    ];
    AssetPlacement[] placements = [
      File("README.md", "plugins/p/README.md"),
      File("*.dll", "plugins/p"),
      File("**/*.dll", "plugins/q"),
      File("bin/**", "plugins/bin"),
      File("bin/*.dll", "plugins/bin1"),
      File("res/**/*.json", "plugins/json"),
      File("a.*", "plugins/a"),
      File("**", "plugins/all"),
      File("bin", "plugins/bin-file"),
      Dir("", "plugins/root"),
      Dir(".", "plugins/dot"),
      Dir("res", "plugins/res"),
      Dir("res/data", "plugins/data"),
      Dir("res/", "plugins/res-slash"),
      Dir("bin", "plugins/bin-dir"),
      Dir("missing", "plugins/missing"),
    ];

    // Each placement alone.
    foreach (AssetPlacement placement in placements) {
      Assert.Equal(LegacyMatch(keys, [placement]), Match(keys, placement));
    }

    // All placements together.
    Assert.Equal(LegacyMatch(keys, placements), Match(keys, placements));
  }

  [Fact]
  public void Expand_SeveralAssets_ConcretePlacementsByAssetThenKeyThenPlacement() {
    // Arrange
    string[] keys = ["0/a.dll", "0/b.dll", "1/x/c.txt", "2"];
    AssetPlacement[] placements = [
      new("0", false, "*.dll", "plugins/p"),
      new("0", false, "a.dll", "plugins/p/a2.dll"),
      new("1", true, "x", "plugins/x/./data"),
      new("2", false, "", "plugins/p/node.zip"),
    ];

    // Act
    List<Placement> expanded = [.. ToothPlacements.Expand(placements, keys)];

    // Assert
    Assert.Equal(
        [
          new Placement("0/a.dll", "plugins/p/a.dll"),
          new Placement("0/a.dll", "plugins/p/a2.dll"),
          new Placement("0/b.dll", "plugins/p/b.dll"),
          new Placement("1/x/c.txt", "plugins/x/data/c.txt"),
          new Placement("2", "plugins/p/node.zip"),
        ],
        expanded);
  }

  [Fact]
  public void Expand_Document_PrependsConcretePlacementsAndClearsAssetPlacements() {
    // Arrange
    ConcreteDocument document = new() {
      IsMarked = true,
      Name = "github.com/user/repo",
      Version = SemVersion.Parse("1.0.0"),
      Type = "files",
      AssetPlacements = [new("0", false, "a.dll", "a.dll")],
      Placements = [new Placement("0/b.dll", "b.dll")],
    };

    // Act
    ConcreteDocument expanded = ToothPlacements.Expand(document, ["0/a.dll", "0/b.dll"]);

    // Assert
    Assert.Empty(expanded.AssetPlacements);
    Assert.Equal(
        [new Placement("0/a.dll", "a.dll"), new Placement("0/b.dll", "b.dll")],
        expanded.Placements);
  }

  [Theory]
  [InlineData("plugins/p/a.dll", "plugins/p/a.dll")]
  [InlineData(@"plugins\p\a.dll", "plugins/p/a.dll")]
  [InlineData("./plugins//p/./a.dll", "plugins/p/a.dll")]
  [InlineData("plugins/x/../p/a.dll", "plugins/p/a.dll")]
  [InlineData("../a.dll", "../a.dll")]
  public void NormalizeTarget_Path_IsWorkspaceRelativeForm(string target, string expected) {
    // Act
    string normalized = ToothPlacements.NormalizeTarget(target);

    // Assert
    Assert.Equal(expected, normalized);
  }

  private static List<(string Key, string TargetPath)> LegacyMatch(
      IEnumerable<string> keys,
      IEnumerable<AssetPlacement> placements) {
    List<(string, string)> result = [];

    foreach (string key in keys) {
      List<string> targetLocations = [];
      foreach (AssetPlacement placement in placements) {
        if (!placement.IsDirectory) {
          if (placement.Src == key) {
            targetLocations.Add(placement.Dest);
          } else if (Glob.Parse(placement.Src).IsMatch(key)) {
            string targetPath = Path.Combine(
                placement.Dest,
                Path.GetFileName(key));
            targetLocations.Add(targetPath);
          }
        } else if (Path.GetRelativePath(string.IsNullOrEmpty(placement.Src) ? "." : placement.Src, key) is string relativePath
            && !relativePath.StartsWith("..")) {
          string targetPath = Path.Combine(
              placement.Dest,
              relativePath);
          targetLocations.Add(targetPath);
        }
      }

      foreach (string target in targetLocations) {
        result.Add((key, target));
      }
    }

    return result;
  }
}
