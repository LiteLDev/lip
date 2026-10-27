using System.IO.Abstractions;
using Semver;

namespace Lip.Core.Mcmod;

/// Turns command line package arguments into requests (spec §6: (ref, range) pairs that are inside
/// no archive).
///
/// Accepted forms:
/// - "https://..." [ "#" variant ]: a url ref to a ZIP archive.
/// - a local ZIP file (.mcmod or .zip) [ "#" variant ]: a path, absolute or relative to the
///   current directory (the workspace root), recognized when it starts with "./", "../", ".\",
///   "..\", is rooted, or has no '@' and ends with ".mcmod" or ".zip" or names an existing file
///   or directory. It becomes a file ref. Directories are rejected: lip does not install from a
///   plain directory.
/// - "path" [ "#" variant ] [ "@" version ]: a spec ref; version is a SemVer 2.0.0 version
///   (without a leading "v") for exactly that version, or a git ref ("branch:" name, "tag:" name,
///   "rev:" full commit id) pinning the spec ref; without it, any version (the newest wins).
public static class PackageArguments {
  private static readonly string[] _zipExtensions = [".mcmod", ".zip"];

  public static PackageRequest Parse(
      string argument, string currentDirectory, IFileSystem fileSystem) {
    if (argument.StartsWith("https://", StringComparison.Ordinal)) {
      Ref url = ParseRef(argument, argument);
      return url.Kind == RefKind.Url
          ? new PackageRequest(url, null)
          : throw new ArgumentException(
              $"Invalid package '{argument}': not an https URL.", nameof(argument));
    }

    if (LocalFileRef(argument, currentDirectory, fileSystem) is Ref file) {
      if (fileSystem.Directory.Exists(file.LocalPath)) {
        throw new ArgumentException(
            $"Invalid package '{argument}': {file.LocalPath} is a directory. lip installs a "
            + "local package only from a .mcmod or .zip file.",
            nameof(argument));
      }

      return fileSystem.File.Exists(file.LocalPath)
          ? new PackageRequest(file, null)
          : throw new ArgumentException(
              $"Invalid package '{argument}': {file.LocalPath} does not exist.",
              nameof(argument));
    }

    int at = argument.IndexOf('@');
    if (at < 0) {
      return new PackageRequest(ParseSpec(argument, argument), null);
    }

    string versionText = argument[(at + 1)..];
    if (SemVersion.TryParse(versionText, SemVersionStyles.Strict, out SemVersion? version)) {
      return new PackageRequest(
          ParseSpec(argument[..at], argument), new ExactVersionConstraint(version));
    }

    if (!versionText.Contains(':')) {
      throw new ArgumentException(
          $"Invalid package '{argument}': '{versionText}' is neither a SemVer 2.0.0 version nor "
          + "a git ref (branch:<name>, tag:<name> or rev:<commit>).",
          nameof(argument));
    }

    return new PackageRequest(ParseSpec(argument, argument), null);
  }

  /// The file ref of an argument that names a local path (see the class remarks), whether or not
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
            && (_zipExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
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
            $"Invalid package '{argument}': not a package path.", nameof(argument));
  }

  private static Ref ParseRef(string text, string argument) {
    return Ref.TryParse(text, out Ref? reference, out string? error)
        ? reference
        : throw new ArgumentException($"Invalid package '{argument}': {error}.", nameof(argument));
  }
}
