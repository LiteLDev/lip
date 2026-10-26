using System.Diagnostics;
using DotNet.Globbing;
using Lip.Core.Entities;
using Lip.Core.Services;

namespace Lip.Core.Tests.Services;

public class PlacementMatcherTests {
  private static PackageManifestAssetPlacement File(string src, string dst) => new() {
    Type = PackageManifestAssetPlacement.PlacementType.File,
    Src = src,
    Dst = dst,
  };

  private static PackageManifestAssetPlacement Dir(string src, string dst) => new() {
    Type = PackageManifestAssetPlacement.PlacementType.Directory,
    Src = src,
    Dst = dst,
  };

  private static List<(string Key, string TargetPath)> Match(
      IEnumerable<string> keys,
      params PackageManifestAssetPlacement[] placements) => [.. PlacementMatcher.Match(keys, placements)];

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
    PackageManifestAssetPlacement[] placements = [
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
    PackageManifestAssetPlacement[] placements = [
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
    foreach (PackageManifestAssetPlacement placement in placements) {
      Assert.Equal(LegacyMatch(keys, [placement]), Match(keys, placement));
    }

    // All placements together.
    Assert.Equal(LegacyMatch(keys, placements), Match(keys, placements));
  }

  private static List<(string Key, string TargetPath)> LegacyMatch(
      IEnumerable<string> keys,
      IEnumerable<PackageManifestAssetPlacement> placements) {
    List<(string, string)> result = [];

    foreach (string key in keys) {
      List<string> targetLocations = [];
      foreach (PackageManifestAssetPlacement placement in placements) {
        switch (placement.Type) {
          case PackageManifestAssetPlacement.PlacementType.File:
            if (placement.Src == key) {
              targetLocations.Add(placement.Dst);
            } else if (Glob.Parse(placement.Src).IsMatch(key)) {
              string targetPath = Path.Combine(
                  placement.Dst,
                  Path.GetFileName(key));
              targetLocations.Add(targetPath);
            }

            break;

          case PackageManifestAssetPlacement.PlacementType.Directory:
            if (Path.GetRelativePath(string.IsNullOrEmpty(placement.Src) ? "." : placement.Src, key) is string relativePath
                && !relativePath.StartsWith("..")) {
              string targetPath = Path.Combine(
                  placement.Dst,
                  relativePath);
              targetLocations.Add(targetPath);
            }

            break;

          default:
            throw new UnreachableException();
        }
      }

      foreach (string target in targetLocations) {
        result.Add((key, target));
      }
    }

    return result;
  }
}
