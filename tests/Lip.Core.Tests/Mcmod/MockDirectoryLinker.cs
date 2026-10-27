using System.IO.Abstractions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Tests.Mcmod;

/// Links directories with symbolic links of the given file system, so that a MockFileSystem
/// records them (the real DirectoryLinker creates junctions on the real file system on Windows).
/// A MockFileSystem does not resolve links: a path through a link is not the path through its
/// target.
internal sealed class MockDirectoryLinker(IFileSystem fileSystem) : IDirectoryLinker {
  public List<(string Link, string Target)> Created { get; } = [];

  /// Makes CreateLink fail, as on a volume without link support.
  public bool Fail { get; set; }

  public void CreateLink(string linkPath, string targetPath) {
    if (Fail) {
      throw new IOException("links are not supported");
    }

    fileSystem.Directory.CreateSymbolicLink(linkPath, targetPath);
    Created.Add((linkPath, targetPath));
  }

  public string? GetLinkTarget(string path) => fileSystem.DirectoryInfo.New(path).LinkTarget;
}
