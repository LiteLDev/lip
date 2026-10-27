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
    Fs = new MockFileSystem();
    Root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";
    Fs.AddDirectory(Root);
    Git = new FakeGitRemote();
    CommandRunner = new Mock<ICommandRunner>();
    Ui = new Mock<IUserInteraction>();
    Ui.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    Ui.Setup(u => u.PrintWarning(It.IsAny<string>()))
        .Callback((string message) => Warnings.Add(message))
        .Returns(Task.CompletedTask);

    Cache = new CacheService(Fs, Ui.Object);
    DocumentParser parser = new();
    ArchiveProvider = new ArchiveProvider(
        Git, Cache, Fs, new Mock<IFileDownloader>().Object);
    LockStore = new LockStore(Fs);
    Service = new McmodService(
        Fs,
        Ui.Object,
        LockStore,
        ArchiveProvider,
        new ArchiveReader(parser),
        new ToothCompat(),
        parser,
        [
          new McmodInstaller(Fs, CommandRunner.Object, parser, Ui.Object),
          new ToothInstaller(
              CommandRunner.Object, Fs, new Mock<ISourceService>().Object, Ui.Object),
        ],
        workspaceRoot: () => Root);
  }

  public MockFileSystem Fs { get; }

  public string Root { get; }

  public FakeGitRemote Git { get; }

  public Mock<ICommandRunner> CommandRunner { get; }

  public Mock<IUserInteraction> Ui { get; }

  public List<string> Warnings { get; } = [];

  public CacheService Cache { get; }

  public ArchiveProvider ArchiveProvider { get; }

  public LockStore LockStore { get; }

  public McmodService Service { get; }

  /// The absolute path of a workspace-relative '/'-separated path.
  public string Path(string relative) =>
      Fs.Path.Combine([Root, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

  public void AddFile(string relative, string content) =>
      Fs.AddFile(Path(relative), new MockFileData(content));

  public bool Exists(string relative) => Fs.File.Exists(Path(relative));

  public Task<LockFile> Lock() => LockStore.Load(Root);

  /// Where the lock of the workspace lives, outside the workspace.
  public string LockPath => LockStore.GetLockPath(Root);

  /// Adds a tag of a spec repository, whose tree holds the given files.
  public void AddTag(string spec, string version, Dictionary<string, string> files) =>
      Git.AddTag(spec, $"v{version}", files);

  /// A tooth.json (format 3) with one unlabeled variant placing the file "<name>.dll" at
  /// "<dest>" and the given dependencies.
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

  /// A marked concrete manifest.json.
  public static string Concrete(string name, string version, string type, string extra = "") =>
      $$"""
      {
        "formatVersion": 4,
        "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
        "name": "{{name}}",
        "version": "{{version}}",
        "type": "{{type}}"{{(extra == "" ? "" : ",\n" + extra)}}
      }
      """;

  /// The bytes of a ZIP file holding the given files.
  public static byte[] Zip(Dictionary<string, string> files) {
    using MemoryStream memory = new();
    using (ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach ((string path, string content) in files) {
        using Stream entry = zip.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
      }
    }

    return memory.ToArray();
  }

  /// A git remote whose tags point to in-memory trees.
  internal sealed class FakeGitRemote : IGitRemoteClient {
    private readonly Dictionary<string, List<(string Tag, string Commit)>> _tags = [];
    private readonly Dictionary<string, List<GitTreeEntry>> _trees = [];
    private readonly Dictionary<string, byte[]> _blobs = [];

    public List<string> ListedTrees { get; } = [];

    public void AddTag(string spec, string tag, Dictionary<string, string> files) {
      string repo = ArchiveProvider.GetRepoUrl(spec);
      string commit = Convert.ToHexStringLower(
          SHA1.HashData(Encoding.UTF8.GetBytes($"{repo}@{tag}")));
      List<GitTreeEntry> tree = [];
      foreach ((string path, string content) in files) {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        string oid = Convert.ToHexStringLower(SHA1.HashData(
            [.. Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"), .. bytes]));
        _blobs[oid] = bytes;
        tree.Add(new GitTreeEntry(path, 100644, oid));
      }

      _trees[commit] = tree;
      if (!_tags.TryGetValue(repo, out List<(string, string)>? tags)) {
        tags = [];
        _tags[repo] = tags;
      }

      tags.Add((tag, commit));
    }

    public Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes) {
      IReadOnlyList<GitRemoteRef> refs = [
        .. _tags.GetValueOrDefault(repoUrl.ToString(), [])
            .Select(t => new GitRemoteRef($"refs/tags/{t.Tag}", t.Commit, null)),
      ];
      return Task.FromResult(refs);
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
