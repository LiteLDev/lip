using System.Text.Json;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;

namespace Lip.Core.Tests.Mcmod;

/// End-to-end tests of installing packages with a package file (protocol/lip-workspace.md,
/// "Installing and uninstalling").
public class PackageInstallTests {
  private static readonly OperationOptions _options = new(Platform: WorkspaceHarness.Platform);

  [Fact]
  public async Task Install_SelfAsset_PlacesFilesAndRecordsWorkspaceFile() {
    WorkspaceHarness h = new();
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          "example.com/a", "1.0.0", WorkspaceHarness.SelfAsset("bin/", "tools/a/")),
      ["bin/a.exe"] = "a",
      ["bin/lib/b.dll"] = "b",
    });

    await h.Service.Install(["example.com/a"], _options);

    Assert.Equal("a", h.Fs.File.ReadAllText(h.Path("tools/a/a.exe")));
    Assert.Equal("b", h.Fs.File.ReadAllText(h.Path("tools/a/lib/b.dll")));
    LockFile lockFile = await h.Lock();
    Assert.Equal(4, lockFile.Format);
    Assert.Equal("server", lockFile.Side);
    Assert.Equal(WorkspaceHarness.Platform, lockFile.Platform);
    LockedPackage package = Assert.Single(lockFile.Packages);
    Assert.Equal("example.com/a", package.Id);
    Assert.Equal("", package.Variant);
    Assert.Equal("1.0.0", package.Version);
    Assert.True(package.Explicit);
    Assert.Equal("example.com/a", package.Source);
    Assert.Matches("^[0-9a-f]{40}$", package.Commit);
    Assert.Equal(["tools/a/a.exe", "tools/a/lib/b.dll"], package.Files);
    Assert.Equal(4, package.Manifest.GetProperty("format").GetInt32());

    using JsonDocument document = JsonFile.Parse(h.Fs.File.ReadAllBytes(h.LockPath));
    Assert.Equal(
        ["format", "side", "platform", "packages"],
        document.RootElement.EnumerateObject().Select(p => p.Name));
    Assert.Equal(
        ["manifest", "variant", "explicit", "ref", "commit", "files"],
        document.RootElement.GetProperty("packages")[0].EnumerateObject().Select(p => p.Name));
  }

  [Fact]
  public async Task Install_McmodWithModDependency_InstallsBothAsTheyAre() {
    WorkspaceHarness h = new();
    h.AddDownload("https://example.com/base.mcmod", WorkspaceHarness.Mcmod("Base"));
    h.AddDownload(
        "https://example.com/top.mcmod",
        WorkspaceHarness.Mcmod("Top", """ "dependencies": [ { "name": "Other" } ] """));
    h.AddTag("example.com/base", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml("example.com/base", "1.0.0",
          """ "assets": [ { "type": "mcmod", "urls": [ "https://example.com/base.mcmod" ] } ] """),
    });
    h.AddTag("example.com/top", "2.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml("example.com/top", "2.0.0",
          """
          "deps": { "example.com/base": "^1.0.0" },
          "assets": [ { "type": "mcmod", "urls": [ "https://example.com/top.mcmod" ] } ]
          """),
    });

    await h.Service.Install(["example.com/top"], _options);

    // The mod directories are named by the "name" of manifest.json, which is written unchanged.
    Assert.Equal("Top binary", h.Fs.File.ReadAllText(h.Path("mods/Top/Top.dll")));
    using JsonDocument top = JsonDocument.Parse(
        h.Fs.File.ReadAllText(h.Path("mods/Top/manifest.json")));
    Assert.Equal(
        "Other", top.RootElement.GetProperty("dependencies")[0].GetProperty("name").GetString());
    Assert.True(h.Exists("mods/Base/Base.dll"));

    LockFile lockFile = await h.Lock();
    Assert.Equal(["example.com/base", "example.com/top"], lockFile.Packages.Select(p => p.Id));
  }

  [Fact]
  public async Task Install_McmodWithoutManifestName_FailsEarly() {
    WorkspaceHarness h = new();
    h.AddDownload(
        "https://example.com/top.mcmod",
        WorkspaceHarness.Zip(new() { ["manifest.json"] = """{ "entry": "x.dll" }""" }));
    h.AddTag("example.com/top", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml("example.com/top", "1.0.0",
          """ "assets": [ { "type": "mcmod", "urls": [ "https://example.com/top.mcmod" ] } ] """),
    });

    await Assert.ThrowsAnyAsync<Exception>(
        () => h.Service.Install(["example.com/top"], _options));

    Assert.False(h.Fs.Directory.Exists(h.Path("mods")));
    Assert.False(h.Fs.File.Exists(h.LockPath));
  }

  [Fact]
  public async Task Install_TwoModsOfOneModName_Fails() {
    WorkspaceHarness h = new();
    h.AddDownload("https://example.com/x.mcmod", WorkspaceHarness.Mcmod("Same"));
    h.AddDownload("https://example.com/y.mcmod", WorkspaceHarness.Mcmod("same"));
    foreach ((string id, string url) in new[] {
      ("example.com/x", "https://example.com/x.mcmod"),
      ("example.com/y", "https://example.com/y.mcmod"),
    }) {
      h.AddTag(id, "1.0.0", new() {
        ["lip.toml"] = WorkspaceHarness.LipToml(id, "1.0.0",
            $$""" "assets": [ { "type": "mcmod", "urls": [ "{{url}}" ] } ] """),
      });
    }

    await h.Service.Install(["example.com/x"], _options);
    await Assert.ThrowsAsync<DependencyResolutionException>(
        () => h.Service.Install(["example.com/y"], _options));
  }

  [Fact]
  public async Task Install_ToothJson_IsReadAsPackageFileAndRecordsTheDerivedOne() {
    WorkspaceHarness h = new();
    h.AddTag("example.com/old", "1.0.0", new() {
      ["tooth.json"] = WorkspaceHarness.Tooth("example.com/old", "1.0.0", "plugins/old/old.dll"),
      ["file.dll"] = "old",
    });

    await h.Service.Install(["example.com/old"], _options);

    Assert.Equal("old", h.Fs.File.ReadAllText(h.Path("plugins/old/old.dll")));
    LockedPackage package = Assert.Single((await h.Lock()).Packages);
    Assert.Equal(4, package.Manifest.GetProperty("format").GetInt32());
    JsonElement entry = package.Manifest.GetProperty("variants").GetProperty("")[0];
    Assert.Equal("server", entry.GetProperty("side").GetString());
    Assert.Equal(WorkspaceHarness.Platform, entry.GetProperty("platform").GetString());

    await h.Service.Uninstall(["example.com/old"], _options);
    Assert.False(h.Exists("plugins/old/old.dll"));
    Assert.Empty((await h.Lock()).Packages);
  }

  [Fact]
  public async Task Install_VersionDiffersFromTag_NoCandidate() {
    WorkspaceHarness h = new();
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml("example.com/a", "1.0.1"),
    });

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => h.Service.Install(["example.com/a"], _options));
  }

  [Fact]
  public async Task Install_PackageIdDiffersFromRepository_NoCandidate() {
    WorkspaceHarness h = new();
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml("example.com/b", "1.0.0"),
    });

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => h.Service.Install(["example.com/a"], _options));
  }

  [Fact]
  public async Task Update_NewTag_ReplacesPackage() {
    WorkspaceHarness h = new();
    h.AddTag("example.com/a", "1.0.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          "example.com/a", "1.0.0", WorkspaceHarness.SelfAsset("a.txt", "a.txt")),
      ["a.txt"] = "1",
    });
    await h.Service.Install(["example.com/a"], _options);
    h.AddTag("example.com/a", "1.1.0", new() {
      ["lip.toml"] = WorkspaceHarness.LipToml(
          "example.com/a", "1.1.0", WorkspaceHarness.SelfAsset("a.txt", "a.txt")),
      ["a.txt"] = "2",
    });
    h.Restart();

    await h.Service.Update([], _options);

    Assert.Equal("2", h.Fs.File.ReadAllText(h.Path("a.txt")));
    Assert.Equal("1.1.0", Assert.Single((await h.Lock()).Packages).Version);
  }

  [Fact]
  public async Task Install_LocalDirectory_RecordsFileUriAndNoCommit() {
    WorkspaceHarness h = new();
    string directory = h.Fs.Path.Combine(h.Root, "src");
    h.Fs.AddFile(h.Fs.Path.Combine(directory, "lip.toml"), new(WorkspaceHarness.LipToml(
        "example.com/local", "0.1.0", WorkspaceHarness.SelfAsset("x.txt", "out/x.txt"))));
    h.Fs.AddFile(h.Fs.Path.Combine(directory, "x.txt"), new("x"));

    await h.Service.Install(["./src"], _options);

    Assert.Equal("x", h.Fs.File.ReadAllText(h.Path("out/x.txt")));
    LockedPackage package = Assert.Single((await h.Lock()).Packages);
    Assert.Equal(Ref.FromFile(directory).ToString(), package.Source);
    Assert.StartsWith("file:", package.Source);
    Assert.Null(package.Commit);
  }
}
