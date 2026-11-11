namespace Lip.Core.Mcmod.Install;

/// Which package owns each file of the workspace while an operation is applied
/// (protocol/lip-workspace.md, "Installing and uninstalling"): the files of the installed packages,
/// less those of the packages uninstalled so far, and the files written so far. Paths are
/// workspace-relative, '/'-separated and by their long names (McmodInstaller.Owners); they are
/// compared ignoring case on Windows.
public sealed class FileOwners {
  private readonly Dictionary<string, string> _owners = new(WorkspaceLayout.PathComparer);

  /// The identity of the package owning the file, or null when no package owns it.
  public string? OwnerOf(string path) => _owners.GetValueOrDefault(path);

  /// Records that the package owns the file, unless a package owns it already.
  public void TryAdd(string path, string identity) => _owners.TryAdd(path, identity);

  /// Records that the package owns the file, as it has written it.
  public void Add(string path, string identity) => _owners[path] = identity;

  /// Records that the package owns no file any more, as it has been uninstalled.
  public void Release(string identity) {
    foreach (string path in _owners
        .Where(o => PackageNames.AreEqual(o.Value, identity))
        .Select(o => o.Key)
        .ToList()) {
      _owners.Remove(path);
    }
  }
}
