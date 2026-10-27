using System.Security.Cryptography;
using System.Text;

namespace Lip.Core.Services;

/// Cache keys, named by the transport the data came over.
public static class CacheKeys {
  /// Anything downloaded over HTTP(S): "http-" + the lowercase hex SHA-256 of the URL. The URL is
  /// hashed because CacheService uses the escaped key as a file name, and URLs can be longer than
  /// file names may be.
  public static string Http(string url) =>
      $"http-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))}";

  /// A git blob, by its object id.
  public static string GitBlob(string objectId) => $"git-{objectId.ToLowerInvariant()}";
}
