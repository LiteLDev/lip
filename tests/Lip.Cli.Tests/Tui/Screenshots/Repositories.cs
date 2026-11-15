using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lip.Core.Mcmod;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// The repositories of the mods of Catalogue, as the archives of a world: with them, lip can
/// plan and carry out an install or an update in a scenario, and says what it says of a real
/// one. Each version of a mod is a package whose files go to mods/&lt;name&gt;/, with the
/// dependencies that the catalogue names. Nothing of it is on the disk or the network.
public static class Repositories {
  /// Where the archives are: a directory that no folder browser of a scenario shows.
  private static readonly string _store =
      World.PathOf(@"C:\Users\Steve\AppData\Local\lip\Cache\archives");

  /// Gives the archives of the world the repositories of every mod of the catalogue, with the
  /// branch main of each at its newest version.
  public static World WithRepositories(this World world) {
    foreach (CatalogueMod mod in Catalogue.Mods) {
      Add(world, mod);
    }

    // LeviLamina, which every mod depends on: lip keeps the one that is installed.
    world.Fs.AddFile(
        Path.Join(_store, "LeviLamina", World.LoaderVersion, "lip.toml"),
        new MockFileData($$"""
            format = 4
            id = "{{World.Loader}}"
            version = "{{World.LoaderVersion}}"

            [[variants.""]]
            side = "server"
            platform = "win-x64"
            deps = { "{{World.Game}}" = "{{World.GameVersion}}" }

            """));
    world.Archives.Setup(a => a.ListVersions(World.Loader)).ReturnsAsync([
      new SpecVersion(SemVersion.Parse(World.LoaderVersion), "v" + World.LoaderVersion),
    ]);
    world.Archives.Setup(a => a.OpenVersion(World.Loader, It.IsAny<SpecVersion>()))
        .ReturnsAsync(() => new ArchiveHandle(
            $"{World.Loader}@v{World.LoaderVersion}",
            new DirectorySource(
                world.Fs, Path.Join(_store, "LeviLamina", World.LoaderVersion)),
            World.Commit));

    return Reconcile(world);
  }

  /// Makes the state file of the selected workspace say what lip would have written of the
  /// mods of the catalogue, so that lip plans for them what it would plan for a real server:
  /// each has the dependencies of its package file, the files that the state file names
  /// exist, and a mod that was installed at a version is of the commit of that version, by a
  /// source that names its variant.
  private static World Reconcile(World world) {
    string path = Path.Join(world.Workspace, ".lip", "state.json");
    if (!world.Fs.FileExists(path)) {
      return world;
    }

    JsonNode state = JsonNode.Parse(world.Fs.File.ReadAllText(path))!;
    foreach (JsonNode? package in state["packages"]!.AsArray()) {
      foreach (JsonNode? file in package!["files"]!.AsArray()) {
        string full = Path.Join(world.Workspace, file!.GetValue<string>());
        if (!world.Fs.FileExists(full)) {
          world.Fs.AddFile(full, new MockFileData("{}"));
        }
      }

      string variant = package["variant"]!.GetValue<string>();
      string id = package["manifest"]!["id"]!.GetValue<string>();
      string version = package["manifest"]!["version"]!.GetValue<string>();
      if (Catalogue.Mods.FirstOrDefault(m => m.Id == id) is not { } mod) {
        continue;
      }

      JsonObject deps = [];
      foreach ((string dependency, string range) in Deps(mod, oldest: version == mod.Versions[^1])) {
        deps[dependency] = range;
      }

      package["manifest"]!["variants"]![variant]![0]!["deps"] = deps;
      if (package["ref"]!.GetValue<string>() == $"{id}@{version}") {
        package["commit"] = CommitOf(id, version);
        if (variant.Length > 0) {
          package["ref"] = $"{id}#{variant}@{version}";
        }
      }
    }

    world.Fs.AddFile(path, new MockFileData(state.ToJsonString()));
    return world;
  }

  /// A local package in a folder of the world: a lip.toml and the files of a mod, which
  /// lip install reads from the path.
  public static World LocalPackage(
      this World world, string folder, string id, string version, params string[] files) {
    string name = id[(id.LastIndexOf('/') + 1)..];
    Write(world.Fs, folder, Manifest(id, version, [""], Deps(null, false), name), name, files);
    world.Archives.Setup(a => a.Open(It.Is<Ref>(
            r => r.Kind == RefKind.File && Same(r.LocalPath, folder))))
        .ReturnsAsync(() => new ArchiveHandle(folder, new DirectorySource(world.Fs, folder)));
    return world;
  }

  /// A local package in a .zip file of the world, which lip install reads from the path.
  public static World ZipPackage(
      this World world, string file, string id, string version, params string[] files) {
    string name = id[(id.LastIndexOf('/') + 1)..];
    string folder = Path.Join(_store, "zips", name);
    Write(world.Fs, folder, Manifest(id, version, [""], Deps(null, false), name), name, files);
    world.Fs.AddFile(file, new MockFileData("PK"));
    world.Archives.Setup(a => a.Open(It.Is<Ref>(
            r => r.Kind == RefKind.File && Same(r.LocalPath, file))))
        .ReturnsAsync(() => new ArchiveHandle(file, new DirectorySource(world.Fs, folder)));
    return world;
  }

  /// A package behind a URL: lip install downloads it, and lip update downloads it again.
  public static World UrlPackage(
      this World world, string url, string id, string version, params string[] files) {
    string name = id[(id.LastIndexOf('/') + 1)..];
    string folder = Path.Join(_store, "urls", name);
    Write(world.Fs, folder, Manifest(id, version, [""], Deps(null, false), name), name, files);
    world.Archives.Setup(a => a.Open(It.Is<Ref>(r => r.Kind == RefKind.Url && r.Target == url)))
        .ReturnsAsync(() => new ArchiveHandle(url, new DirectorySource(world.Fs, folder)));
    return world;
  }

  /// Makes every request of the archives fail as a network that does not reach GitHub fails.
  public static World WithoutNetwork(this World world) {
    ArchiveUnavailableException Failure(string what) => new(
        $"Could not fetch {what}: the connection to github.com timed out.",
        new HttpRequestException(
            "A connection attempt failed because the connected party did not properly respond"
                + " after a period of time. (github.com:443)"));
    world.Archives.Setup(a => a.ListVersions(It.IsAny<string>()))
        .Returns((string spec) => Task.FromException<IReadOnlyList<SpecVersion>>(
            Failure($"the tags of {spec}")));
    world.Archives.Setup(a => a.ResolveGitRef(It.IsAny<string>(), It.IsAny<GitRef>()))
        .Returns((string spec, GitRef gitRef) => Task.FromException<string>(
            Failure($"{spec}@{gitRef}")));
    return world;
  }

  /// Adds a mod that the user installed to the state file of the selected workspace, which
  /// the world has already (World.InstallServer), with the source given: "id@branch:main", a
  /// URL, or a local path (Ref.FromFile(path).ToString()). commit: that of a repository.
  public static World InstallFrom(
      this World world, string id, string version, string source, string? commit = null,
      params string[] files) {
    string path = Path.Join(world.Workspace, ".lip", "state.json");
    JsonNode state = JsonNode.Parse(world.Fs.File.ReadAllText(path))!;
    JsonObject package = new() {
      ["manifest"] = new JsonObject {
        ["format"] = 4,
        ["id"] = id,
        ["version"] = version,
        ["variants"] = new JsonObject {
          [""] = new JsonArray(new JsonObject { ["side"] = "server", ["platform"] = "win-x64" }),
        },
      },
      ["variant"] = "",
      ["explicit"] = true,
      ["ref"] = source,
    };
    if (commit is not null) {
      package["commit"] = commit;
    }

    package["files"] = new JsonArray([.. files.Select(file => JsonValue.Create(file))]);
    state["packages"]!.AsArray().Add(package);
    world.Fs.AddFile(path, new MockFileData(state.ToJsonString()));
    return Reconcile(world);
  }

  /// The commit of a version of a repository: the same in every run.
  public static string CommitOf(string id, string version) =>
      Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes($"{id}@{version}")));

  private static void Add(World world, CatalogueMod mod) {
    string[] variants = mod.Variants ?? [""];
    Dictionary<string, string> folders = [];
    for (int i = 0; i < mod.Versions.Length; i++) {
      string version = mod.Versions[i];
      string folder = Path.Join(_store, mod.Name, version);
      folders[version] = folder;
      Write(
          world.Fs, folder,
          Manifest(
              mod.Id, version, variants, Deps(mod, oldest: i == mod.Versions.Length - 1),
              mod.Name),
          mod.Name, ["manifest.json", $"{mod.Name}.dll", $"{mod.Name}.pdb"]);
    }

    string id = mod.Id.ToLowerInvariant();
    ArchiveHandle Handle(string version) => new(
        $"{mod.Id}@v{version}", new DirectorySource(world.Fs, folders[version]),
        CommitOf(mod.Id, version));
    ArchiveUnavailableException Missing(string what) =>
        new($"{mod.Id} has no {what}.");

    world.Archives.Setup(a => a.ListVersions(It.Is<string>(spec => spec.ToLower() == id))).ReturnsAsync([
      .. mod.Versions.Select(v => new SpecVersion(SemVersion.Parse(v), "v" + v)),
    ]);
    world.Archives.Setup(a => a.OpenVersion(It.Is<string>(spec => spec.ToLower() == id), It.IsAny<SpecVersion>()))
        .Returns((string _, SpecVersion version) =>
            folders.ContainsKey(version.Version.ToString())
                ? Task.FromResult(Handle(version.Version.ToString()))
                : Task.FromException<ArchiveHandle>(Missing($"version {version.Version}")));
    // A version names its tag, the branch main is at the newest version, and a commit is
    // that of a version.
    world.Archives.Setup(a => a.ResolveGitRef(It.Is<string>(spec => spec.ToLower() == id), It.IsAny<GitRef>()))
        .Returns((string _, GitRef gitRef) => gitRef switch {
          { Kind: GitRefKind.Version } when folders.ContainsKey(gitRef.Name) =>
              Task.FromResult(CommitOf(mod.Id, gitRef.Name)),
          { Kind: GitRefKind.Branch, Name: "main" } =>
              Task.FromResult(CommitOf(mod.Id, mod.Versions[0])),
          { Kind: GitRefKind.Rev } when mod.Versions.Any(
              v => CommitOf(mod.Id, v) == gitRef.Name) => Task.FromResult(gitRef.Name),
          _ => Task.FromException<string>(Missing(gitRef.ToString())),
        });
    world.Archives.Setup(a => a.OpenCommit(It.Is<string>(spec => spec.ToLower() == id), It.IsAny<string>()))
        .Returns((string _, string commit) =>
            mod.Versions.FirstOrDefault(v => CommitOf(mod.Id, v) == commit) is { } version
                ? Task.FromResult(Handle(version))
                : Task.FromException<ArchiveHandle>(Missing($"commit {commit}")));
  }

  private static bool Same(string a, string b) =>
      string.Equals(
          Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
          StringComparison.OrdinalIgnoreCase);

  /// The dependencies of a version of a mod, as Catalogue gives them to the catalogue.
  private static Dictionary<string, string> Deps(CatalogueMod? mod, bool oldest) {
    Dictionary<string, string> deps = new() { [World.Loader] = oldest ? "26.50.*" : "26.60.*" };
    foreach (string id in mod?.Depends ?? []) {
      deps[id] = ">=" + Catalogue.Mods.First(m => m.Id == id).Versions[^1];
    }

    return deps;
  }

  private static string Manifest(
      string id, string version, string[] variants, Dictionary<string, string> deps,
      string name) {
    StringBuilder toml = new();
    toml.Append(
        System.Globalization.CultureInfo.InvariantCulture,
        $"format = 4\nid = \"{id}\"\nversion = \"{version}\"\n");
    foreach (string variant in variants) {
      toml.Append(
          System.Globalization.CultureInfo.InvariantCulture,
          $"\n[[variants.\"{variant}\"]]\nside = \"server\"\nplatform = \"win-x64\"\n");
      toml.Append(
          System.Globalization.CultureInfo.InvariantCulture,
          $"deps = {{ {string.Join(", ", deps.Select(d => $"\"{d.Key}\" = \"{d.Value}\""))} }}\n");
      toml.Append(
          System.Globalization.CultureInfo.InvariantCulture,
          $"assets = [ {{ type = \"self\", place = [ {{ src = \"{name}/\","
              + $" dest = \"mods/{name}/\" }} ] }} ]\n");
    }

    return toml.ToString();
  }

  private static void Write(
      MockFileSystem fs, string folder, string manifest, string name, string[] files) {
    fs.AddFile(Path.Join(folder, "lip.toml"), new MockFileData(manifest));
    foreach (string file in files.Length == 0 ? ["manifest.json"] : files) {
      fs.AddFile(Path.Join(folder, name, file), new MockFileData($"{name} {file}"));
    }
  }
}
