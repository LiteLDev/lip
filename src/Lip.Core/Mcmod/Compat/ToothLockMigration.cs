using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Resolution;
using Semver;

namespace Lip.Core.Mcmod.Compat;

/// The tooth_lock.json of lip v0.x at the root of a workspace (protocol/compatibility.md §3).
/// A workspace without a workspace file whose tooth_lock.json is one Section 3 reads (Read) has
/// the workspace file Section 3 derives from it for its environment (Derive). A workspace
/// operation writes that workspace file, then deletes tooth_lock.json (LockStore.CommitStaged).
public static class ToothLockMigration {
  public const string FileName = "tooth_lock.json";

  /// A member of "packages": its "manifest" (a tooth.json), "variant", "locked" and "files".
  public record Member(JsonElement Manifest, string Variant, bool Locked, IReadOnlyList<string> Files);

  /// The members of &lt;root&gt;/tooth_lock.json, when it is a JSON object with format_version 3
  /// and an array "packages" of objects with "manifest" (a tooth.json of format 1 to 3, a JSON
  /// object with format_version 1, 2 or 3), "variant" (a string), "locked" (a boolean) and
  /// "files" (an array of strings). Null otherwise, also when it is missing or unreadable.
  public static IReadOnlyList<Member>? Read(IFileSystem fs, string root) {
    string path = fs.Path.Join(root, FileName);
    if (!fs.File.Exists(path)) {
      return null;
    }

    try {
      using JsonDocument json = PackageFileParser.ParseJson(fs.File.ReadAllBytes(path), FileName);
      IReadOnlyList<Member>? members = Members(json.RootElement);
      RunTrace.Write("migration", members is null
          ? $"{path} is not a tooth_lock.json of format 3; it is not migrated"
          : $"{path} lists {members.Count} packages to migrate");
      return members;
    }
    catch (Exception ex) when (ex is PackageFormatException or IOException
        or UnauthorizedAccessException or InvalidOperationException) {
      RunTrace.Write("migration", $"{path} cannot be read, so it is not migrated: {ex.Message}");
      return null;
    }
  }

  private static List<Member>? Members(JsonElement root) {
    if (ToothCompat.FormatVersion(root) != 3
        || !root.TryGetProperty("packages", out JsonElement packages)
        || packages.ValueKind != JsonValueKind.Array) {
      return null;
    }

    List<Member> members = [];
    foreach (JsonElement package in packages.EnumerateArray()) {
      if (package.ValueKind != JsonValueKind.Object
          || !package.TryGetProperty("manifest", out JsonElement manifest)
          || ToothCompat.FormatVersion(manifest) is not (1 or 2 or 3)
          || !package.TryGetProperty("variant", out JsonElement variant)
          || variant.ValueKind != JsonValueKind.String
          || !package.TryGetProperty("locked", out JsonElement locked)
          || locked.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
          || !package.TryGetProperty("files", out JsonElement files)
          || files.ValueKind != JsonValueKind.Array
          || files.EnumerateArray().Any(f => f.ValueKind != JsonValueKind.String)) {
        return null;
      }

      members.Add(new Member(
          manifest.Clone(),
          variant.GetString()!,
          locked.GetBoolean(),
          [.. files.EnumerateArray().Select(f => f.GetString()!)]));
    }

    return members;
  }

  /// The workspace file protocol/compatibility.md §3 derives from the members of a
  /// tooth_lock.json for an environment: for each member whose manifest protocol/compatibility.md
  /// §2 reads, for the environment, with a variant "variant", in installation order, a package
  /// with that manifest, its variant, "locked" as explicit, the source "&lt;tooth&gt;[#variant]",
  /// the commit commitOf gives for the tooth path and the version (the commit of the tag
  /// "v&lt;version&gt;"; it fails when that is null), and the files below the workspace,
  /// workspace-relative and '/'-separated. Without commitOf (for reading only), no commit is
  /// recorded.
  public static async Task<LockFile> Derive(
      IReadOnlyList<Member> members,
      IFileSystem fs,
      string root,
      LipEnvironment environment,
      Func<string, SemVersion, Task<string?>>? commitOf) {
    List<(LockedPackage Locked, PackageEntry Entry, SemVersion Version)> packages = [];
    foreach (Member member in members) {
      if (member.Variant != "" && !Ref.IsValidVariant(member.Variant)) {
        RunTrace.Write(
            "migration", $"left out a package with the invalid variant '{member.Variant}'");
        continue;
      }

      PackageFile packageFile;
      try {
        packageFile = ToothCompat.Derive(ToothCompat.ReadToothJson(member.Manifest), environment);
      }
      catch (PackageFormatException ex) {
        RunTrace.Write(
            "migration", $"left out a package whose manifest cannot be read: {ex.Message}");
        continue;
      }

      if (packageFile.SelectEntry(member.Variant, environment) is not PackageEntry entry) {
        RunTrace.Write("migration", $"left out {packageFile.Id} {packageFile.Version}: it has no"
            + $" variant '{member.Variant}' for {environment.Platform.ToSpecString()}");
        continue;
      }

      string? commit = null;
      if (commitOf is not null) {
        commit = (await commitOf(packageFile.Id, packageFile.Version))?.ToLowerInvariant()
            ?? throw new InvalidOperationException(
                $"lip cannot convert {fs.Path.Join(root, FileName)}, written by lip v0.x, to the"
                + $" workspace file: the commit of the tag v{packageFile.Version} of"
                + $" {packageFile.Id} cannot be found. The tag may have been deleted, or the"
                + " repository cannot be reached. Check your network connection and GitHub proxy,"
                + " then try again.");
      }

      packages.Add((
          new LockedPackage {
            Manifest = packageFile.Json,
            Variant = member.Variant,
            Explicit = member.Locked,
            Source = Package.GetIdentity(packageFile.Id, member.Variant),
            Commit = commit,
            Files = [.. member.Files.Select(f => RelativePath(fs, root, f)).OfType<string>()],
          },
          entry,
          packageFile.Version));
    }

    // Installation order (protocol/workspace.md §5): a package after the packages that satisfy
    // its dependencies.
    packages = [
      .. packages
          .OrderBy(p => PackageNames.Fold(p.Locked.Identity), StringComparer.Ordinal)
          .ThenBy(p => p.Version, SemVersion.SortOrderComparer),
    ];
    IReadOnlyList<int> order = GraphSolver.InstallOrder(packages.Count, i =>
        packages[i].Entry.Dependencies.SelectMany(d => Enumerable.Range(0, packages.Count)
            .Where(j => PackageNames.AreEqual(
                    packages[j].Locked.Identity,
                    Package.GetIdentity(d.Source.Target, d.Source.Variant))
                && d.Range.IsSatisfiedBy(packages[j].Version))));

    return new LockFile {
      Platform = environment.Platform.ToSpecString(),
      Runtime = environment.Runtime,
      Packages = [.. order.Select(i => packages[i].Locked)],
    };
  }

  /// A recorded path as the path relative to the workspace it names, '/'-separated, when it is
  /// below the workspace (relative to the workspace when not absolute); null otherwise.
  private static string? RelativePath(IFileSystem fs, string root, string file) {
    string full;
    try {
      full = fs.Path.GetFullPath(fs.Path.IsPathRooted(file) ? file : fs.Path.Join(root, file));
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException
        or PathTooLongException) {
      return null;
    }

    string relative = fs.Path.GetRelativePath(root, full);
    return relative == "." || relative == ".." || fs.Path.IsPathRooted(relative)
        || relative.StartsWith(".." + fs.Path.DirectorySeparatorChar, StringComparison.Ordinal)
        ? null
        : relative.Replace(fs.Path.DirectorySeparatorChar, '/');
  }
}
