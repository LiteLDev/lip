using System.IO.Abstractions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Migration.PackageManifests;

namespace Lip.Core.Mcmod.Compat;

/// Migrates a package repository to a manifest.json of format bf45b1 (lip migrate, specs/cli.md
/// §5.8): from the tooth.json at its root (ToothMigrator), which is then deleted, or else from the
/// LeviLamina manifest in its manifest.json (LeviLaminaMigrator), which is rewritten.
///
/// - A repository with both tooth.json and manifest.json, with neither, or whose manifest.json
///   already has the format key, is a failure, and nothing is changed.
/// - manifest.json is written, with an indentation of two spaces, through the hidden temporary
///   file .manifest.json.tmp and a rename; tooth.json is deleted only after that succeeded.
/// - For a meta document, the package document of each target i is written first to
///   targets/i/manifest.json; a repository that already has targets is then a failure.
/// - Questions are asked through IUserInteraction.Ask before anything is written.
///
/// The notes are the migrator's, those of ToothMigrator.DroppedFields, and one for each written
/// manifest.json that is not valid.
public static class RepositoryMigrator {
  public const string TempFileName = ".manifest.json.tmp";

  /// The directory the package documents of the targets of a meta document are written to.
  public const string TargetsDirectoryName = "targets";

  private static readonly JsonDocumentOptions _strictJson = new() {
    AllowDuplicateProperties = false,
    AllowTrailingCommas = false,
    CommentHandling = JsonCommentHandling.Disallow,
  };

  private static readonly JsonSerializerOptions _writeOptions = new() {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    NewLine = "\n",
  };

  public static async Task<IReadOnlyList<string>> Migrate(
      IFileSystem fs, string directory, IUserInteraction ui) {
    string toothPath = fs.Path.Join(directory, ManifestFormat.ToothFileName);
    string manifestPath = fs.Path.Join(directory, ManifestFormat.ManifestFileName);

    bool hasTooth = fs.File.Exists(toothPath);
    bool hasManifest = fs.File.Exists(manifestPath);
    if (!hasTooth && !hasManifest) {
      throw new InvalidOperationException(
          $"There is neither {ManifestFormat.ToothFileName} nor {ManifestFormat.ManifestFileName}"
          + $" in {directory}. Run lip migrate in the root directory of the package repository.");
    }

    if (hasTooth && hasManifest) {
      throw new InvalidOperationException(
          $"Both {ManifestFormat.ToothFileName} and {ManifestFormat.ManifestFileName} exist."
          + $" Move {ManifestFormat.ManifestFileName} away and run lip migrate again.");
    }

    ToothMigrator.Result result;
    List<string> notes = [];
    if (hasTooth) {
      // tooth.json is deleted last, after manifest.json was written: fail before writing
      // anything when it obviously cannot be.
      if (fs.File.GetAttributes(toothPath).HasFlag(FileAttributes.ReadOnly)) {
        throw new InvalidOperationException(
            $"{ManifestFormat.ToothFileName} is read-only, so it cannot be deleted after the "
            + "migration. Make it writable and run lip migrate again.");
      }

      using JsonDocument json = await ReadJson(fs, toothPath, ManifestFormat.ToothFileName);
      PackageManifest tooth;
      try {
        tooth = PackageManifestMigration.Migrate(json);
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        // The v1/v2 migration and the v3 model throw many kinds of exceptions on bad input.
        throw new InvalidOperationException(
            $"{ManifestFormat.ToothFileName} is not a valid tooth.json of format 1, 2 or 3: "
            + ex.Message, ex);
      }

      result = await ToothMigrator.Migrate(tooth, ui, () => RepositoryFiles(fs, directory));
      notes.AddRange(ToothMigrator.DroppedFields(json.RootElement, tooth));
    } else {
      JsonObject manifest;
      using (JsonDocument json = await ReadJson(fs, manifestPath, ManifestFormat.ManifestFileName)) {
        manifest = JsonObject.Create(json.RootElement.Clone())
            ?? throw new InvalidOperationException(
                $"{ManifestFormat.ManifestFileName} is not a JSON object.");
      }

      if (manifest.ContainsKey(ManifestFormat.FormatKey)) {
        throw new InvalidOperationException(
            $"{ManifestFormat.ManifestFileName} already has the \"{ManifestFormat.FormatKey}\""
            + " key: there is nothing to migrate.");
      }

      result = await LeviLaminaMigrator.Migrate(manifest, ui);
    }

    notes.InsertRange(0, result.Notes);
    byte[] bytes = Serialize(result.Document);
    if (ToothMigrator.ValidationNote(bytes) is string validation) {
      notes.Add(validation);
    }

    string targetsPath = fs.Path.Join(directory, TargetsDirectoryName);
    if (result.Targets.Count > 0) {
      if (fs.Directory.Exists(targetsPath) || fs.File.Exists(targetsPath)) {
        throw new InvalidOperationException(
            $"{TargetsDirectoryName} exists, but the package documents of the targets are to be"
            + $" written there. Move {TargetsDirectoryName} away and run lip migrate again.");
      }

      try {
        for (int i = 0; i < result.Targets.Count; i++) {
          string name = $"{TargetsDirectoryName}/{i}/{ManifestFormat.ManifestFileName}";
          byte[] target = Serialize(result.Targets[i]);
          if (ToothMigrator.ValidationNote(target) is string targetValidation) {
            notes.Add($"{name}: {targetValidation}");
          }

          fs.Directory.CreateDirectory(fs.Path.Join(targetsPath, $"{i}"));
          await fs.File.WriteAllBytesAsync(fs.Path.Join(directory, name), target);
        }
      }
      catch {
        if (fs.Directory.Exists(targetsPath)) {
          fs.Directory.Delete(targetsPath, recursive: true);
        }

        throw;
      }
    }

    string tempPath = fs.Path.Join(directory, TempFileName);
    try {
      // A leftover of an interrupted migration; on Windows a hidden file cannot be overwritten.
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }

      await fs.File.WriteAllBytesAsync(tempPath, bytes);
      fs.SetHidden(tempPath, true);
      fs.File.Move(tempPath, manifestPath, overwrite: true);
      fs.SetHidden(manifestPath, false);
    }
    catch when (result.Targets.Count > 0 && fs.Directory.Exists(targetsPath)) {
      fs.Directory.Delete(targetsPath, recursive: true);
      throw;
    }
    finally {
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }
    }

    if (hasTooth) {
      try {
        fs.File.Delete(toothPath);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        throw new InvalidOperationException(
            $"{ManifestFormat.ManifestFileName} was written, but {ManifestFormat.ToothFileName} "
            + $"could not be deleted ({ex.Message}). Delete it yourself: lip reads tooth.json "
            + "when an archive has both.", ex);
      }
    }

    return notes;
  }

  private static byte[] Serialize(JsonObject document) =>
      Encoding.UTF8.GetBytes(document.ToJsonString(_writeOptions) + "\n");

  private static async Task<JsonDocument> ReadJson(IFileSystem fs, string path, string name) {
    try {
      return JsonDocument.Parse(await fs.File.ReadAllBytesAsync(path), _strictJson);
    }
    catch (JsonException ex) {
      throw new InvalidOperationException($"Cannot read {name}: {ex.Message}", ex);
    }
  }

  /// The '/'-separated paths of the files of the repository, in ordinal order, which make up
  /// the archive of the tooth.json ("self" assets) and of the migrated manifest.json: everything
  /// in the directory but .git and tooth.json itself. Links are not followed and inaccessible
  /// entries are skipped. Untracked files are included: the working tree is all lip can see.
  private static List<string> RepositoryFiles(IFileSystem fs, string directory) {
    EnumerationOptions options = new() {
      IgnoreInaccessible = true,
      AttributesToSkip = FileAttributes.ReparsePoint,
    };
    List<string> files = [];
    void Walk(string current) {
      foreach (string file in fs.Directory.EnumerateFiles(current, "*", options)) {
        files.Add(fs.Path.GetRelativePath(directory, file).Replace('\\', '/'));
      }

      foreach (string sub in fs.Directory.EnumerateDirectories(current, "*", options)) {
        if (current != directory || fs.Path.GetFileName(sub) != ".git") {
          Walk(sub);
        }
      }
    }

    try {
      Walk(directory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new InvalidOperationException(
          $"Cannot list the files of the repository: {ex.Message}", ex);
    }

    files.Remove(ManifestFormat.ToothFileName);
    files.Sort(StringComparer.Ordinal);
    return files;
  }
}
