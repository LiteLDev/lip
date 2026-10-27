using System.Text.Json.Serialization;
using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// How the final archive of a package is identified in the lock (spec §8).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ZipArchiveIdentity), "zip")]
[JsonDerivedType(typeof(GitArchiveIdentity), "git")]
[JsonDerivedType(typeof(DirectoryArchiveIdentity), "directory")]
public abstract record ArchiveIdentity;

/// A ZIP archive (downloaded or nested), identified by the SHA-256 of its bytes (lowercase hex).
/// Url is where it was downloaded from, if anywhere.
public record ZipArchiveIdentity(
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("url")] string? Url) : ArchiveIdentity;

/// A git archive: a commit of a repository, and the path of the archive root inside it ("" for
/// the repository root, otherwise without leading or trailing '/').
public record GitArchiveIdentity(
    [property: JsonPropertyName("repo")] string RepoUrl,
    [property: JsonPropertyName("commit")] string Commit,
    [property: JsonPropertyName("path")] string Path) : ArchiveIdentity;

/// A local directory archive.
public record DirectoryArchiveIdentity(
    [property: JsonPropertyName("path")] string Path) : ArchiveIdentity;

/// An opened archive: its archive key (spec §5.2 table), its file tree, and its identity.
/// Source keys are '/'-separated relative file paths, as for every ISource.
public record ArchiveHandle(string Key, ISource Source, ArchiveIdentity Identity);

/// A version of a spec ref: a "v" + SemVer 2.0.0 tag.
public record SpecVersion(SemVersion Version, string Tag);

/// Thrown when an archive cannot be obtained or is invalid (spec §2). For resolution this
/// means "no candidate" on that path, not a failure of the whole operation.
public class ArchiveUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Obtains archives for ref targets (spec §3, §5.2 step 1).
public interface IArchiveProvider {
  /// The workspace root as a directory archive; its key is the absolute root path.
  public ArchiveHandle OpenWorkspace(string workspaceRoot);

  /// Opens a relative or url target. Relative targets are resolved inside baseArchive.
  /// Throws ArchiveUnavailableException when the archive cannot be obtained or is invalid.
  public Task<ArchiveHandle> Open(Ref target, ArchiveHandle baseArchive);

  /// Lists the "v" + SemVer tags of a spec target, highest precedence first; equal
  /// precedence is ordered by tag name, lexicographically larger first. Other tags are ignored.
  public Task<IReadOnlyList<SpecVersion>> ListVersions(string spec);

  /// Opens the git archive of one version of a spec target. Its key is "spec@version".
  public Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version);

  /// Re-opens an archive recorded in the lock and verifies its identity (spec §8).
  public Task<ArchiveHandle> OpenLocked(string key, ArchiveIdentity identity);
}
