using System.Text.Json.Serialization;
using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// The archive identifier of a package (protocol/workspace.md §7.1), which identifies the content
/// of its archive.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ZipArchiveIdentity), "zip")]
[JsonDerivedType(typeof(GitArchiveIdentity), "git")]
[JsonDerivedType(typeof(DirectoryArchiveIdentity), "directory")]
public abstract record ArchiveIdentity {
  /// A string equal for two identifiers exactly when they identify the same content.
  [JsonIgnore]
  public abstract string Canonical { get; }
}

/// A ZIP archive, identified by the SHA-256 of its bytes (lowercase hex). Url is the URL it was
/// downloaded from, without its fragment, or the file: URI of the absolute path of a local file.
public record ZipArchiveIdentity(
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("url")] string Url) : ArchiveIdentity {
  [JsonIgnore]
  public override string Canonical => $"zip {Sha256.ToLowerInvariant()}";
}

/// A git archive: a commit (lowercase hex) of the repository at RepoUrl, https://&lt;repo&gt;.git.
public record GitArchiveIdentity(
    [property: JsonPropertyName("repo")] string RepoUrl,
    [property: JsonPropertyName("commit")] string Commit) : ArchiveIdentity {
  [JsonIgnore]
  public override string Canonical =>
      $"git {PackageNames.Fold(RepoUrl)} {Commit.ToLowerInvariant()}";
}

/// A directory archive on the local file system, identified by its absolute path.
public record DirectoryArchiveIdentity(
    [property: JsonPropertyName("path")] string Path) : ArchiveIdentity {
  [JsonIgnore]
  public override string Canonical => $"directory {Path}";
}

/// The asset identifier of an asset other than "self" (protocol/workspace.md §7.1): the URL it was
/// downloaded from and the SHA-256 of the file (lowercase hex).
public record AssetIdentity(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256);

/// An opened archive: a key naming it in messages and caches, its file tree, and its identity.
/// Source keys are '/'-separated relative file paths, as for every ISource.
public record ArchiveHandle(string Key, ISource Source, ArchiveIdentity Identity);

/// A downloaded asset: its files, and its identifier.
public record OpenedAsset(ISource Source, AssetIdentity Identity);

/// What a downloaded asset must be.
public enum AssetFormat {
  /// A ZIP archive (protocol/package.md §1): the assets of type "zip" and "mcmod".
  Zip,

  /// A ZIP, tar or gzipped tar archive, told apart by content, whose files are not checked: the
  /// assets of type "zip", "tar" and "tgz" of format 3, as lip v0.37.0 read them.
  LegacyArchive,

  /// A single file with the key "": the assets of type "uncompressed" of format 3.
  LegacyFile,
}

/// A version of a remote repository: a version tag "v" + version.
public record SpecVersion(SemVersion Version, string Tag);

/// Thrown when an archive or an asset cannot be obtained or is invalid (protocol/package.md §2).
/// For resolution this means "no candidate" on that path, not a failure of the whole operation.
public class ArchiveUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// Obtains archives of sources and assets (protocol/package.md §2, §4 step 1).
public interface IArchiveProvider {
  /// Opens the archive of a URL or a local path (a ZIP file or a directory). Throws
  /// ArchiveUnavailableException when the archive cannot be obtained or is invalid.
  public Task<ArchiveHandle> Open(Ref target);

  /// Lists the version tags of a remote repository, highest precedence first; equal precedence
  /// is ordered by tag name, lexicographically larger first. Other tags are ignored.
  public Task<IReadOnlyList<SpecVersion>> ListVersions(string spec);

  /// Opens the git archive of one version of a remote repository. Its key is "spec@version".
  public Task<ArchiveHandle> OpenVersion(string spec, SpecVersion version);

  /// Resolves the ref of a remote repository to a commit id (protocol/package.md §2.1): the tag
  /// of a version, the head of a branch, the commit a tag points to (peeled), or the commit id of
  /// a rev itself. Throws ArchiveUnavailableException when the repository has no such ref.
  public Task<string> ResolveGitRef(string spec, GitRef gitRef);

  /// Opens the git archive of a commit of a remote repository. Its key is "spec@rev:commit".
  public Task<ArchiveHandle> OpenCommit(string spec, string commit);

  /// Opens the archive an archive identifier recorded in the workspace file identifies, and
  /// verifies its identity (protocol/workspace.md §5, "Reinstalling").
  public Task<ArchiveHandle> OpenLocked(string key, ArchiveIdentity identity);

  /// Downloads an asset from the first of its URLs that can be obtained (protocol/workspace.md §4
  /// step 2), cached under the key of that URL like the archives of URLs. Throws
  /// ArchiveUnavailableException when none can.
  public Task<OpenedAsset> OpenAsset(IReadOnlyList<string> urls, AssetFormat format);

  /// Opens an asset an asset identifier recorded in the workspace file identifies, and verifies
  /// its SHA-256.
  public Task<ISource> OpenLockedAsset(AssetIdentity identity, AssetFormat format);
}
