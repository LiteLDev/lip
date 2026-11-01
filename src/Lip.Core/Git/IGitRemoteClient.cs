using Flurl;

namespace Lip.Core.Git;

public record GitRemoteRef(string Name, string ObjectId, string? PeeledObjectId);

public record GitTreeEntry(string Path, int Mode, string ObjectId) {
  /// Whether the entry is a regular (possibly executable) file rather than a symlink. Mode holds
  /// git's octal mode digits read as a decimal number, e.g. 100644, 100755 or 120000.
  public bool IsRegularFile => Mode is 100644 or 100755 or 100664;
}

public interface IGitRemoteClient {
  /// Lists refs whose names start with any of the given prefixes. Annotated tags carry PeeledObjectId.
  Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes);

  /// Recursively lists blob entries ('/'-separated paths relative to the root tree) of the given
  /// commit or tag object id without downloading any blob content. Gitlinks are excluded.
  Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish);

  /// The URL of the repository that answers for repoUrl: repoUrl itself, or where it redirects
  /// (e.g. GitHub redirects the old path of a repository that was transferred or renamed).
  Task<Url> GetRepositoryUrl(Url repoUrl);

  /// Downloads the given blobs; onBlob is invoked once per object id with a readable content stream.
  Task FetchBlobs(Url repoUrl, IReadOnlyCollection<string> blobObjectIds, Func<string, Stream, Task> onBlob);
}
