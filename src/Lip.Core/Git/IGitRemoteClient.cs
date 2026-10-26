using Flurl;

namespace Lip.Core.Git;

public record GitRemoteRef(string Name, string ObjectId, string? PeeledObjectId);

public record GitTreeEntry(string Path, int Mode, string ObjectId);

public interface IGitRemoteClient {
  /// Lists refs whose names start with any of the given prefixes. Annotated tags carry PeeledObjectId.
  Task<IReadOnlyList<GitRemoteRef>> ListRefs(Url repoUrl, IEnumerable<string> refPrefixes);

  /// Recursively lists blob entries ('/'-separated paths relative to the root tree) of the given
  /// commit or tag object id without downloading any blob content. Gitlinks are excluded.
  Task<IReadOnlyList<GitTreeEntry>> ListTree(Url repoUrl, string commitish);

  /// Downloads the given blobs; onBlob is invoked once per object id with a readable content stream.
  Task FetchBlobs(Url repoUrl, IReadOnlyCollection<string> blobObjectIds, Func<string, Stream, Task> onBlob);
}
