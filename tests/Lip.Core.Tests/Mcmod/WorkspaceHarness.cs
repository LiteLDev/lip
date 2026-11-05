using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Flurl;
using Lip.Core.Git;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Compat;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.Services;
using Moq;

namespace Lip.Core.Tests.Mcmod;

/// A workspace on a MockFileSystem, with the whole format 4 pipeline (real archive provider,
/// resolver, solver, installers and lock store) over a fake git remote whose repositories are
/// directories of the mock file system.
internal sealed class WorkspaceHarness {
  public WorkspaceHarness() {
    Root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";
    Fs = new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory: Root);
    Fs.AddDirectory(Root);
    // The service and the installer change the workspace through it; the archive provider and
    // the lock store use Fs directly.
    Faults = new FaultyFileSystem(Fs);
    Git = new FakeGitRemote();
    CommandRunner = new Mock<ICommandRunner>();
    Ui = new Mock<IUserInteraction>();
    Ui.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    Ui.Setup(u => u.PrintWarning(It.IsAny<string>()))
        .Callback((string message) => Warnings.Add(message))
        .Returns(Task.CompletedTask);

    Cache = new CacheService(Fs, Ui.Object);
    LockStore = new LockStore(Fs);
    Linker = new MockDirectoryLinker(Fs);
    SetUpDownloader();
    Restart();
  }

  /// Starts over with a new archive provider and service, as a new lip process does, so that
  /// tags added since are seen.
  [MemberNotNull(nameof(ArchiveProvider), nameof(Service))]
  public void Restart() {
    ArchiveProvider = new ArchiveProvider(Git, Cache, Fs, Downloader.Object);
    Service = new McmodService(
        Faults,
        Ui.Object,
        LockStore,
        ArchiveProvider,
        new ArchiveReader(),
        new McmodInstaller(Faults, CommandRunner.Object, Ui.Object),
        workspaceRoot: () => Root,
        directoryLinker: Linker);
  }

  /// Downloads: every URL added by AddDownload serves its bytes; any other fails.
  public Mock<IFileDownloader> Downloader { get; } = CreateDownloader();

  private readonly Dictionary<string, byte[]> _downloads = [];

  /// Makes a URL serve the given bytes (e.g. a ZIP file of an asset).
  public void AddDownload(string url, byte[] content) => _downloads[url] = content;

  private static Mock<IFileDownloader> CreateDownloader() => new();

  private void SetUpDownloader() {
    Downloader
        .Setup(d => d.DownloadFile(It.IsAny<Url>(), It.IsAny<System.IO.Abstractions.IFileInfo>()))
        .Returns((Url url, System.IO.Abstractions.IFileInfo destination) => {
          if (!_downloads.TryGetValue(url.ToString(), out byte[]? content)) {
            throw new HttpRequestException($"404 {url}");
          }

          Fs.Directory.CreateDirectory(destination.DirectoryName!);
          Fs.File.WriteAllBytes(destination.FullName, content);
          return Task.CompletedTask;
        });
  }

  public MockFileSystem Fs { get; }

  /// The file system of the service, which can be made to fail.
  public FaultyFileSystem Faults { get; }

  /// The journal of the workspace, next to its lock.
  public string JournalPath => LockStore.GetJournalPath(Root);

  public string Root { get; }

  public FakeGitRemote Git { get; }

  public Mock<ICommandRunner> CommandRunner { get; }

  public Mock<IUserInteraction> Ui { get; }

  public List<string> Warnings { get; } = [];

  public CacheService Cache { get; }

  public ArchiveProvider ArchiveProvider { get; private set; }

  public LockStore LockStore { get; }

  public MockDirectoryLinker Linker { get; }

  public McmodService Service { get; private set; }

  /// The absolute path of a workspace-relative '/'-separated path.
  public string Path(string relative) =>
      Fs.Path.Combine([Root, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

  public void AddFile(string relative, string content) =>
      Fs.AddFile(Path(relative), new MockFileData(content));

  /// Writes a ZIP file (e.g. an .mcmod) holding the given files; returns its absolute path.
  public string AddZip(string relative, Dictionary<string, string> files) {
    string path = Path(relative);
    Fs.AddFile(path, new MockFileData(Zip(files)));
    return path;
  }

  public bool Exists(string relative) => Fs.File.Exists(Path(relative));

  /// Every file of the mock file system with its content and attributes, and every directory
  /// and link in the workspace: what an operation that fails must leave as it was. Directories
  /// outside the workspace (lip's data directory) may be created.
  public SortedDictionary<string, string> Snapshot() {
    SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
    // Creating .lip and the workspace lock file in it is not a change (protocol/workspace.md §7).
    string lip = Fs.Path.Join(Root, LockStore.LipDirectory);
    foreach (string path in Fs.AllPaths.Where(
        p => p != lip && p != LockStore.GetMutexPath(Root))) {
      if (Fs.File.Exists(path)) {
        snapshot[path] = Convert.ToBase64String(Fs.File.ReadAllBytes(path))
            + $" ({Fs.File.GetAttributes(path)})";
      } else if (path.StartsWith(Root, StringComparison.Ordinal)) {
        snapshot[path] = Fs.DirectoryInfo.New(path).LinkTarget is string target
            ? $"<link to {target}>"
            : "<dir>";
      }
    }

    return snapshot;
  }

  /// The workspace file. Throws when there is none.
  public async Task<LockFile> Lock() =>
      await LockStore.Load(Root) ?? throw new InvalidOperationException("No workspace file.");

  /// Where the lock of the workspace lives, in .lip inside the workspace.
  public string LockPath => LockStore.GetLockPath(Root);

  /// Adds a tag of a spec repository, whose tree holds the given files.
  public void AddTag(string spec, string version, Dictionary<string, string> files) =>
      Git.AddTag(spec, $"v{version}", files);

  /// A tooth.json (format 3) with one unlabeled variant placing the file "file.dll" at
  /// "&lt;dest&gt;" and the given dependencies.
  public static string Tooth(
      string path,
      string version,
      string dest,
      string dependencies = "{}",
      string label = "") => $$"""
      {
        "format_version": 3,
        "format_uuid": "289f771f-2c9a-4d73-9f3f-8492495a924d",
        "tooth": "{{path}}",
        "version": "{{version}}",
        "variants": [
          {
            "label": "{{label}}",
            "dependencies": {{dependencies}},
            "assets": [
              {
                "type": "self",
                "placements": [ { "type": "file", "src": "file.dll", "dest": "{{dest}}" } ]
              }
            ]
          }
        ]
      }
      """;

  /// The runtime of the environment the tests install for.
  public const string Runtime = "win-x64";

  /// A tooth.json of format 4 whose variant "" has one entry for (server, Runtime) with the
  /// given members (e.g. "\"assets\": [...]"), comma-separated JSON members without braces.
  public static string Tooth4(string tooth, string version, string entry = "") => $$"""
      {
        "format_version": 4,
        "format_uuid": "f74a5fda-cb2a-4de8-959f-a1a76bad3102",
        "tooth": "{{tooth}}",
        "version": "{{version}}",
        "variants": {
          "": [ { "platform": "server", "runtime": "{{Runtime}}"{{(entry == "" ? "" : ", " + entry)}} } ]
        }
      }
      """;

  /// The member "assets" of an entry with one asset of type "self" placing the file "src" at
  /// "dest".
  public static string SelfAsset(string src, string dest) =>
      $$"""
      "assets": [ { "type": "self", "place": [ { "src": "{{src}}", "dest": "{{dest}}" } ] } ]
      """;

  /// The bytes of the ZIP file of an asset of type "mcmod": a mod manifest named name (with
  /// the given extra members) and the file "&lt;name&gt;.dll".
  public static byte[] Mcmod(string name, string extra = "") => Zip(new() {
    ["manifest.json"] = $$"""{ "name": "{{name}}", "entry": "{{name}}.dll", "type": "native"{{(extra == "" ? "" : ", " + extra)}} }""",
    [$"{name}.dll"] = $"{name} binary",
  });

  /// The bytes of a ZIP file holding the given files.
  public static byte[] Zip(Dictionary<string, string> files) =>
      ZipBytes(files.ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value)));

  /// The bytes of a ZIP file holding the given files, e.g. other ZIP files.
  public static byte[] ZipBytes(Dictionary<string, byte[]> files) {
    using MemoryStream memory = new();
    using (ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach ((string path, byte[] content) in files) {
        using Stream entry = zip.CreateEntry(path).Open();
        entry.Write(content);
      }
    }

    return memory.ToArray();
  }

  /// A git remote whose tags and branches point to in-memory trees.
  internal sealed class FakeGitRemote : IGitRemoteClient {
    private readonly Dictionary<string, Dictionary<string, string>> _refs = [];
    private readonly Dictionary<string, List<GitTreeEntry>> _trees = [];
    private readonly Dictionary<string, byte[]> _blobs = [];

    public List<string> ListedTrees { get; } = [];

    /// The repository URL and ref prefixes of every ListRefs call, e.g.
    /// "https://example.com/x.git refs/tags/v".
    public List<string> ListedRefs { get; } = [];

    /// Adds (or moves) a tag to a new commit holding the given files; returns the commit.
    public string AddTag(string spec, string tag, Dictionary<string, string> files) =>
        SetRef(spec, $"refs/tags/{tag}", files);

    /// Adds (or moves) a branch to a new commit holding the given files; returns the commit.
    public string AddBranch(string spec, string branch, Dictionary<string, string> files) =>
        SetRef(spec, $"refs/heads/{branch}", files);

    public Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) {
      List<string> prefixes = [.. refPrefixes];
      string repo = repoUrl.ToString();
      ListedRefs.Add($"{repo} {string.Join(" ", prefixes)}");
      IReadOnlyList<GitRemoteRef> refs = [
        .. _refs.GetValueOrDefault(repo, [])
            .Where(r => prefixes.Any(p => r.Key.StartsWith(p, StringComparison.Ordinal)))
            .Select(r => new GitRemoteRef(r.Key, r.Value, null)),
      ];
      return Task.FromResult(refs);
    }

    private string SetRef(string spec, string refName, Dictionary<string, string> files) {
      string repo = ArchiveProvider.GetRepoUrl(spec);
      List<GitTreeEntry> tree = [];
      foreach ((string path, string content) in files) {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        string oid = Convert.ToHexStringLower(SHA1.HashData(
            [.. Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"), .. bytes]));
        _blobs[oid] = bytes;
        tree.Add(new GitTreeEntry(path, 100644, oid));
      }

      string commit = Convert.ToHexStringLower(SHA1.HashData(
          Encoding.UTF8.GetBytes($"{repo}@{refName}#{_trees.Count}")));
      _trees[commit] = tree;
      if (!_refs.TryGetValue(repo, out Dictionary<string, string>? refs)) {
        refs = [];
        _refs[repo] = refs;
      }

      refs[refName] = commit;
      return commit;
    }

    public Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish) {
      ListedTrees.Add(commitish);
      return _trees.TryGetValue(commitish, out List<GitTreeEntry>? tree)
          ? Task.FromResult<IReadOnlyList<GitTreeEntry>>(tree)
          : throw new InvalidOperationException($"No commit {commitish}");
    }

    public async Task FetchBlobs(
        Url repoUrl, IReadOnlyCollection<string> blobObjectIds, Func<string, Stream, Task> onBlob) {
      foreach (string id in blobObjectIds) {
        await onBlob(id, new MemoryStream(_blobs[id]));
      }
    }
  }
}
