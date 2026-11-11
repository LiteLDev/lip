using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// An opened archive: a key naming it in messages and caches, its file tree, and, for a git
/// archive, the commit (lowercase hex) it is the tree of, which the state file records
/// (protocol/state.json.md). Source keys are '/'-separated relative file paths, as for every
/// ISource.
public record ArchiveHandle(string Key, ISource Source, string? Commit = null);

/// A version of a remote repository: a version tag "v" + version.
public record SpecVersion(SemVersion Version, string Tag);

/// Thrown when an archive or an asset cannot be obtained or is invalid (protocol/lip-package.md,
/// "Sources"). For resolution this means "no candidate" on that path, not a failure of the whole
/// operation.
public class ArchiveUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Obtains archives of sources and assets (protocol/lip-package.md, "Sources" and "Resolution" step
/// 1).
public interface IArchiveProvider {
  /// Opens the archive of a URL or a local path (a ZIP file or a directory). Throws
  /// ArchiveUnavailableException when the archive cannot be obtained or is invalid.
  public Task<ArchiveHandle> Open(Ref target);

  /// Lists the version tags of a remote repository, highest precedence first; equal precedence
  /// is ordered by tag name, lexicographically larger first. Other tags are ignored.
  public Task<IReadOnlyList<SpecVersion>> ListVersions(string spec);

  /// Opens the git archive of one version of a remote repository. Its key is "spec@version".
  public Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version);

  /// Resolves the ref of a remote repository to a commit id (protocol/lip-package.md, "Remote
  /// repositories"): the tag of a version, the head of a branch, the commit a tag points to
  /// (peeled), or the commit id of a rev itself. Throws ArchiveUnavailableException when the
  /// repository has no such ref.
  public Task<string> ResolveGitRef(string spec, GitRef gitRef);

  /// Opens the git archive of a commit of a remote repository. Its key is "spec@rev:commit".
  public Task<ArchiveHandle> OpenCommit(string spec, string commit);

  /// Downloads the ZIP archive of an asset from the first of its https URLs that can be obtained
  /// (protocol/lip-workspace.md, "Installing and uninstalling" step 2), cached under the key of
  /// that URL like the archives of URLs. Throws ArchiveUnavailableException when none can.
  public Task<ISource> OpenAsset(IReadOnlyList<string> urls);

  /// Forgets the cached download of the archive of a URL, so that the next Open downloads it
  /// again (protocol/lip-workspace.md, "Operations" "Updating").
  public Task Forget(string url);
}
