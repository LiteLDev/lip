using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;

namespace Lip.Core.Mcmod;

/// Turns package arguments into requests (protocol/lip.md, "Package arguments"): a source with a
/// range matching every version.
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

  /// The accepted forms, for messages.
  public const string AcceptedForms = "A package is written as a remote repository such as"
      + " github.com/owner/repo (optionally followed by #<variant>, then by @<version>,"
      + " @branch:<name>, @tag:<name> or @rev:<full commit id>), an https:// URL of a ZIP"
      + " archive, or the path of a local ZIP file or directory (e.g. ./my-package.zip).";

  /// Throws ArgumentException when the argument is none of the accepted forms, or names a local
  /// path that does not exist.
  public static PackageRequest Parse(
      string argument, string currentDirectory, IFileSystem fileSystem) {
    if (argument.StartsWith("https://", StringComparison.Ordinal)) {
      Ref url = ParseRef(argument, argument);
      if (url.Kind != RefKind.Url) {
        throw Invalid(argument, "it is not an https:// URL");
      }

      RunTrace.Write("parse", $"Package argument '{argument}' is the URL {url}.");
      return new PackageRequest(url, null);
    }

    if (OtherSchemeRegex().IsMatch(argument)) {
      throw Invalid(argument, SchemeReason(argument[..argument.IndexOf(':')]));
    }

    if (LocalFileRef(argument, currentDirectory, fileSystem) is Ref file) {
      if (!fileSystem.File.Exists(file.LocalPath)
          && !fileSystem.Directory.Exists(file.LocalPath)) {
        throw new ArgumentException(
            $"Cannot find the local package '{argument}': {file.LocalPath} does not exist. Check"
            + " the path (a relative path is relative to the workspace root"
            + $" {currentDirectory}). {AcceptedForms}");
      }

      RunTrace.Write("parse", $"Package argument '{argument}' is the local path"
          + $" {file.LocalPath}{(file.Variant == "" ? "" : $" with variant '{file.Variant}'")}.");
      return new PackageRequest(file, null);
    }

    if (argument.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) {
      throw Invalid(argument, SchemeReason("file"));
    }

    Ref spec = ParseSpec(argument, argument);
    RunTrace.Write("parse", $"Package argument '{argument}' is the remote repository"
        + $" {spec.Target}{(spec.Variant == "" ? "" : $" with variant '{spec.Variant}'")}"
        + $"{(spec.GitRef is null ? "" : $" at ref {spec.GitRef}")}.");
    return new PackageRequest(spec, null);
  }

  /// Why an argument with a URI scheme other than https is no package.
  private static string SchemeReason(string scheme) =>
      scheme.Equals("file", StringComparison.OrdinalIgnoreCase)
          ? "a local package is written as its path (e.g. ./my-package.zip), not as a file: URI"
          : scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
              ? "lip downloads only over https://, so use the https:// URL of the archive"
              : $"lip cannot download from {scheme}:// URLs, only from https:// URLs";

  private static ArgumentException Invalid(string argument, string reason) =>
      new($"'{argument}' is not a valid package: {reason}. {AcceptedForms}");

  /// The source of an argument that names a local path (see the class remarks), whether or not
  /// it exists; null when the argument is no local path. A variant follows the last '#' unless
  /// the whole argument names an existing path, so that file names may contain '#'.
  private static Ref? LocalFileRef(
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
        : throw Invalid(argument, "it is not a remote repository");
  }

  private static Ref ParseRef(string text, string argument) {
    return Ref.TryParse(text, out Ref? reference, out string? error)
        ? reference
        : throw Invalid(argument, error);
  }

  /// A URI scheme followed by "://".
  [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*://")]
  private static partial Regex OtherSchemeRegex();
}
