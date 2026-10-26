using System.Security.Cryptography;
using System.Text;

namespace Lip.Core.Git;

public enum GitObjectType {
  Commit = 1,
  Tree = 2,
  Blob = 3,
  Tag = 4,
}

public record GitObject(string ObjectId, GitObjectType Type, byte[] Content) {
  public static string ComputeObjectId(GitObjectType type, ReadOnlySpan<byte> content) {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    hash.AppendData(Encoding.ASCII.GetBytes($"{GetTypeName(type)} {content.Length}\0"));
    hash.AppendData(content);
    return Convert.ToHexStringLower(hash.GetHashAndReset());
  }

  public static string GetTypeName(GitObjectType type) {
    return type switch {
      GitObjectType.Commit => "commit",
      GitObjectType.Tree => "tree",
      GitObjectType.Blob => "blob",
      GitObjectType.Tag => "tag",
      _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
  }

  /// Returns the tree object id of a commit.
  public static string ParseCommitTree(byte[] content) {
    string? tree = ReadHeader(content, "tree");
    return tree is not null && IsObjectId(tree)
        ? tree
        : throw new GitProtocolException("Malformed commit object: missing tree.");
  }

  /// Returns the target object id of an annotated tag.
  public static string ParseTagTarget(byte[] content) {
    string? target = ReadHeader(content, "object");
    return target is not null && IsObjectId(target)
        ? target
        : throw new GitProtocolException("Malformed tag object: missing target.");
  }

  /// Parses tree entries. Mode is the octal mode digits read as a decimal number, e.g. 100644.
  public static IEnumerable<(int Mode, string Name, string ObjectId)> ParseTree(byte[] content) {
    int position = 0;
    while (position < content.Length) {
      int space = Array.IndexOf(content, (byte)' ', position);
      int nul = space < 0 ? -1 : Array.IndexOf(content, (byte)0, space + 1);
      if (space < 0 || nul < 0 || nul + 21 > content.Length) {
        throw new GitProtocolException("Malformed tree object.");
      }

      string modeText = Encoding.ASCII.GetString(content, position, space - position);
      if (modeText.Length == 0 || modeText.Length > 7 || !modeText.All(c => c is >= '0' and <= '7')) {
        throw new GitProtocolException($"Malformed tree entry mode '{modeText}'.");
      }
      string name = Encoding.UTF8.GetString(content, space + 1, nul - space - 1);
      string objectId = Convert.ToHexStringLower(content, nul + 1, 20);
      position = nul + 21;

      yield return (int.Parse(modeText), name, objectId);
    }
  }

  public static bool IsObjectId(string value) {
    return value.Length == 40 && value.All(char.IsAsciiHexDigit);
  }

  private static string? ReadHeader(byte[] content, string key) {
    // Headers end at the first empty line; only the header block is decoded.
    int end = content.AsSpan().IndexOf("\n\n"u8);
    string headers = Encoding.UTF8.GetString(content, 0, end < 0 ? content.Length : end);
    foreach (string line in headers.Split('\n')) {
      if (line.StartsWith(key + " ", StringComparison.Ordinal)) {
        return line[(key.Length + 1)..].Trim();
      }
    }
    return null;
  }
}
