using System.IO.Abstractions;
using Semver;

namespace Lip.Core.Mcmod;

/// Turns command line package arguments into requests (spec §6: (ref, range) pairs that are inside
/// no archive).
///
/// Accepted forms:
/// - "https://..." [ "#" variant ]: a url ref to a ZIP archive.
/// - a local ZIP file (.mcmod or .zip) [ "#" variant ]: a path, absolute or relative to the
///   current directory, recognized when it starts with "./", "../", ".\", "..\", is rooted, ends
///   with ".mcmod" or ".zip", or names an existing file or directory. It becomes a file ref.
///   Directories are rejected: lip does not install from a plain directory.
/// - "path" [ "#" variant ] [ "@" version ]: a spec ref; version is a SemVer 2.0.0 version
///   (without a leading "v") for exactly that version, or a git ref ("branch:" name, "tag:" name,
///   "rev:" full commit id) pinning the spec ref; without it, any version (the newest wins).
public static class PackageArguments {
  private static readonly string[] _zipExtensions = [".mcmod", ".zip"];

  public static PackageRequest Parse(string argument, IFileSystem fileSystem) {
    if (argument.StartsWith("https://", StringComparison.Ordinal)) {
      Ref url = ParseRef(argument, argument);
      return url.Kind == RefKind.Url
          ? new PackageRequest(url, null)
          : throw new ArgumentException(
              $"Invalid package '{argument}': not an https URL.", nameof(argument));
    }

    if (TryParseLocal(argument, fileSystem) is PackageRequest local) {
      return local;
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

  /// A request for a local file, or null when the argument does not name a local path.
  private static PackageRequest? TryParseLocal(string argument, IFileSystem fileSystem) {
    string path = argument;
    string variant = "";
    if (!Exists(path, fileSystem)) {
      // A variant follows the last '#', so that file names may contain '#'.
      int hash = argument.LastIndexOf('#');
      if (hash >= 0 && Ref.IsValidVariant(argument[(hash + 1)..])) {
        path = argument[..hash];
        variant = argument[(hash + 1)..];
      }
    }

    if (!LooksLocal(path, fileSystem)) {
      return null;
    }

    string full = fileSystem.Path.GetFullPath(path);
    if (fileSystem.Directory.Exists(full)) {
      throw new ArgumentException(
          $"Invalid package '{argument}': {full} is a directory. lip installs a local package "
          + "only from a .mcmod or .zip file.",
          nameof(argument));
    }

    if (!fileSystem.File.Exists(full)) {
      throw new ArgumentException(
          $"Invalid package '{argument}': {full} does not exist.", nameof(argument));
    }

    return new PackageRequest(Ref.FromFile(full, variant), null);
  }

  private static bool LooksLocal(string path, IFileSystem fileSystem) {
    return path.StartsWith("./", StringComparison.Ordinal)
        || path.StartsWith(".\\", StringComparison.Ordinal)
        || path.StartsWith("../", StringComparison.Ordinal)
        || path.StartsWith("..\\", StringComparison.Ordinal)
        || path is "." or ".."
        || fileSystem.Path.IsPathRooted(path)
        || _zipExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
        || (!path.Contains('@') && Exists(path, fileSystem));
  }

  private static bool Exists(string path, IFileSystem fileSystem) {
    if (path == "") {
      return false;
    }

    try {
      string full = fileSystem.Path.GetFullPath(path);
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
