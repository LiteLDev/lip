using System.IO.Abstractions;
using System.Text.RegularExpressions;

namespace Lip.Core.Mcmod;

/// Turns package arguments into requests (protocol/cli.md §3): a source with a range matching
/// every version.
///
/// Accepted forms:
/// - "https://..." [ "#" variant ]: a URL.
/// - a local path [ "#" variant ]: a path, absolute or relative to the current directory (the
///   workspace root), recognized when it starts with "./", "../", ".\", "..\", is "." or "..",
///   is rooted, or has no '@' and ends with ".zip" or names an existing file or directory. It
///   becomes the file: URI of its absolute path, naming a local file or a local directory.
/// - "repo" [ "#" variant ] [ "@" ref ]: a remote repository, with a ref (a version without a
///   leading "v", "branch:" name, "tag:" name or "rev:" full commit id) or without.
public static partial class PackageArguments {
  private const string _zipExtension = ".zip";

  public static PackageRequest Parse(
      string argument, string currentDirectory, IFileSystem fileSystem) {
    if (argument.StartsWith("https://", StringComparison.Ordinal)) {
      Ref url = ParseRef(argument, argument);
      return url.Kind == RefKind.Url
          ? new PackageRequest(url, null)
          : throw new ArgumentException(
              $"Invalid package '{argument}': not an https URL.", nameof(argument));
    }

    if (OtherSchemeRegex().IsMatch(argument)) {
      throw new ArgumentException(
          $"Invalid package '{argument}': only https:// URLs are sources.", nameof(argument));
    }

    if (LocalFileRef(argument, currentDirectory, fileSystem) is Ref file) {
      return fileSystem.File.Exists(file.LocalPath) || fileSystem.Directory.Exists(file.LocalPath)
          ? new PackageRequest(file, null)
          : throw new ArgumentException(
              $"Invalid package '{argument}': {file.LocalPath} does not exist.",
              nameof(argument));
    }

    return new PackageRequest(ParseSpec(argument, argument), null);
  }

  /// The source of an argument that names a local path (see the class remarks), whether or not
  /// it exists; null when the argument is no local path. A variant follows the last '#' unless
  /// the whole argument names an existing path, so that file names may contain '#'.
  public static Ref? LocalFileRef(
      string argument, string currentDirectory, IFileSystem fileSystem) {
    string path = argument;
    string variant = "";
    if (!Exists(path, currentDirectory, fileSystem)) {
      int hash = argument.LastIndexOf('#');
      if (hash >= 0 && Ref.IsValidVariant(argument[(hash + 1)..])) {
        path = argument[..hash];
        variant = argument[(hash + 1)..];
      }
    }

    bool isLocal = path.StartsWith("./", StringComparison.Ordinal)
        || path.StartsWith(".\\", StringComparison.Ordinal)
        || path.StartsWith("../", StringComparison.Ordinal)
        || path.StartsWith("..\\", StringComparison.Ordinal)
        || path is "." or ".."
        || IsRooted(path, fileSystem)
        || (!path.Contains('@')
            && (path.EndsWith(_zipExtension, StringComparison.OrdinalIgnoreCase)
                || Exists(path, currentDirectory, fileSystem)));
    if (!isLocal) {
      return null;
    }

    string full = Path.TrimEndingDirectorySeparator(
        fileSystem.Path.GetFullPath(fileSystem.Path.Combine(currentDirectory, path)));
    return Ref.FromFile(full, variant);
  }

  private static bool IsRooted(string path, IFileSystem fileSystem) {
    try {
      return fileSystem.Path.IsPathRooted(path);
    }
    catch (ArgumentException) {
      return false;
    }
  }

  private static bool Exists(string path, string currentDirectory, IFileSystem fileSystem) {
    if (path == "") {
      return false;
    }

    try {
      string full = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(currentDirectory, path));
      return fileSystem.File.Exists(full) || fileSystem.Directory.Exists(full);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException
        or PathTooLongException) {
      return false;
    }
  }

  private static Ref ParseSpec(string text, string argument) {
    Ref reference = ParseRef(text, argument);
    return reference.Kind == RefKind.Spec
        ? reference
        : throw new ArgumentException(
            $"Invalid package '{argument}': not a remote repository.", nameof(argument));
  }

  private static Ref ParseRef(string text, string argument) {
    return Ref.TryParse(text, out Ref? reference, out string? error)
        ? reference
        : throw new ArgumentException($"Invalid package '{argument}': {error}.", nameof(argument));
  }

  /// A URI scheme followed by "://".
  [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*://")]
  private static partial Regex OtherSchemeRegex();
}
