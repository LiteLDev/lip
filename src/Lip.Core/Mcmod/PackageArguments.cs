using System.IO.Abstractions;
using Semver;

namespace Lip.Core.Mcmod;

/// Turns command line package arguments into requests (spec §6: (ref, range) pairs whose base is
/// the workspace root).
///
/// Accepted forms:
/// - "https://..." [ "#" variant ]: a url ref.
/// - a local file or directory [ "#" variant ]: anything starting with "./", "../", ".\", "..\"
///   or rooted, or any other argument naming an existing file or directory. Inside the workspace
///   it becomes a relative ref of the workspace root ("./dir/" or "./file.mcmod"); outside it, a
///   relative ref of the directory containing it, opened as a directory archive.
/// - "path" [ "#" variant ] [ "@" version ]: a spec ref; with a version, exactly that version
///   (SemVer 2.0.0, without a leading "v"), otherwise any version (the newest wins).
public static class PackageArguments {
  public static PackageRequest Parse(
      string argument,
      string workspaceRoot,
      IFileSystem fileSystem,
      IArchiveProvider archiveProvider) {
    ArchiveHandle workspace = archiveProvider.OpenWorkspace(workspaceRoot);

    if (argument.StartsWith("https://", StringComparison.Ordinal)) {
      return new PackageRequest(ParseRef(argument, argument), null, workspace);
    }

    (string path, string variant) = SplitVariant(argument);
    if (IsLocalPath(path, workspaceRoot, fileSystem)) {
      return ParseLocal(argument, path, variant, workspaceRoot, fileSystem, archiveProvider);
    }

    int at = argument.LastIndexOf('@');
    if (at < 0) {
      return new PackageRequest(ParseSpec(argument, argument), null, workspace);
    }

    string versionText = argument[(at + 1)..];
    if (!SemVersion.TryParse(versionText, SemVersionStyles.Strict, out SemVersion? version)) {
      throw new ArgumentException(
          $"Invalid package '{argument}': '{versionText}' is not a SemVer 2.0.0 version.",
          nameof(argument));
    }

    return new PackageRequest(
        ParseSpec(argument[..at], argument), new ExactVersionConstraint(version), workspace);
  }

  private static bool IsLocalPath(string path, string workspaceRoot, IFileSystem fileSystem) {
    if (path.StartsWith("./", StringComparison.Ordinal)
        || path.StartsWith(".\\", StringComparison.Ordinal)
        || path.StartsWith("../", StringComparison.Ordinal)
        || path.StartsWith("..\\", StringComparison.Ordinal)
        || fileSystem.Path.IsPathRooted(path)) {
      return true;
    }

    if (path == "" || path.Contains('@')) {
      return false;
    }

    string full = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(workspaceRoot, path));
    return fileSystem.File.Exists(full) || fileSystem.Directory.Exists(full);
  }

  private static PackageRequest ParseLocal(
      string argument,
      string path,
      string variant,
      string workspaceRoot,
      IFileSystem fileSystem,
      IArchiveProvider archiveProvider) {
    if (variant.Contains('@')) {
      throw new ArgumentException(
          $"Invalid package '{argument}': a local package has no versions to choose from.",
          nameof(argument));
    }

    string root = Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(workspaceRoot));
    string full = Path.TrimEndingDirectorySeparator(
        fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, path)));

    bool isDirectory = fileSystem.Directory.Exists(full);
    if (!isDirectory && !fileSystem.File.Exists(full)) {
      throw new ArgumentException(
          $"Invalid package '{argument}': {full} does not exist.", nameof(argument));
    }

    string relative = fileSystem.Path.GetRelativePath(root, full);
    ArchiveHandle baseArchive;
    if (relative != "." && relative != ".." && !fileSystem.Path.IsPathRooted(relative)
        && !relative.StartsWith(".." + fileSystem.Path.DirectorySeparatorChar)
        && !relative.StartsWith("../")) {
      baseArchive = archiveProvider.OpenWorkspace(root);
    } else {
      string parent = fileSystem.Path.GetDirectoryName(full)
          ?? throw new ArgumentException(
              $"Invalid package '{argument}': {full} has no parent directory.",
              nameof(argument));
      baseArchive = archiveProvider.OpenWorkspace(parent);
      relative = fileSystem.Path.GetFileName(full);
    }

    string target = "./" + relative.Replace('\\', '/') + (isDirectory ? "/" : "");
    return new PackageRequest(
        ParseRef(variant == "" ? target : $"{target}#{variant}", argument), null, baseArchive);
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

  private static (string Path, string Variant) SplitVariant(string argument) {
    int hash = argument.IndexOf('#');
    if (hash < 0) {
      return (argument, "");
    }

    return (argument[..hash], argument[(hash + 1)..]);
  }
}
