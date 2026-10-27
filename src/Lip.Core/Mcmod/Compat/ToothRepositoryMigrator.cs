using System.IO.Abstractions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Lip.Core.Migration.PackageManifests;

namespace Lip.Core.Mcmod.Compat;

/// Migrates a package repository from tooth.json to manifest.json (lip migrate): the tooth.json
/// at the root of the repository is converted by ToothMigrator and written to manifest.json,
/// then tooth.json is deleted.
///
/// - A repository without tooth.json is a failure, and nothing is changed.
/// - An existing manifest.json (e.g. the LeviLamina manifest of a mod) is merged into, not
///   replaced: the converted keys it lacks are appended in their converted order, in the file's
///   own indentation and line endings, and every other byte of the file is kept. A key it already
///   has is never overwritten; a note tells when its value differs from the converted one.
/// - manifest.json is written through the hidden temporary file .manifest.json.tmp and a rename,
///   and tooth.json is deleted only after that succeeded.
///
/// The notes are ToothMigrator's, those of DroppedFields, one per conflicting key, and a last one
/// when the written manifest.json is not valid (e.g. because a kept key conflicts).
public static partial class ToothRepositoryMigrator {
  public const string TempFileName = ".manifest.json.tmp";

  private static readonly UTF8Encoding _strictUtf8 = new(
      encoderShouldEmitUTF8Identifier: false,
      throwOnInvalidBytes: true);

  private static readonly JsonDocumentOptions _strictJson = new() {
    AllowDuplicateProperties = false,
    AllowTrailingCommas = false,
    CommentHandling = JsonCommentHandling.Disallow,
  };

  public static async Task<IReadOnlyList<string>> Migrate(IFileSystem fs, string directory) {
    string toothPath = fs.Path.Join(directory, ManifestFormat.ToothFileName);
    string manifestPath = fs.Path.Join(directory, ManifestFormat.ManifestFileName);
    string tempPath = fs.Path.Join(directory, TempFileName);

    if (!fs.File.Exists(toothPath)) {
      throw new InvalidOperationException(
          $"There is no {ManifestFormat.ToothFileName} in {directory}. Run lip migrate in the "
          + "root directory of the package repository.");
    }

    // tooth.json is deleted last, after manifest.json was written: fail before writing anything
    // when it obviously cannot be.
    if (fs.File.GetAttributes(toothPath).HasFlag(FileAttributes.ReadOnly)) {
      throw new InvalidOperationException(
          $"{ManifestFormat.ToothFileName} is read-only, so it cannot be deleted after the "
          + "migration. Make it writable and run lip migrate again.");
    }

    JsonDocument json;
    PackageManifest tooth;
    try {
      json = JsonDocument.Parse(await fs.File.ReadAllBytesAsync(toothPath), _strictJson);
    }
    catch (JsonException ex) {
      throw new InvalidOperationException(
          $"Cannot read {ManifestFormat.ToothFileName}: {ex.Message}", ex);
    }

    List<string> notes = [];
    JsonObject converted;
    using (json) {
      try {
        tooth = PackageManifestMigration.Migrate(json);
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        // The v1/v2 migration and the v3 model throw many kinds of exceptions on bad input.
        throw new InvalidOperationException(
            $"{ManifestFormat.ToothFileName} is not a valid tooth.json of format 1, 2 or 3: "
            + ex.Message, ex);
      }

      ToothMigrator.Result result = ToothMigrator.Migrate(
          tooth, () => RepositoryFiles(fs, directory));
      converted = result.Document;
      notes.AddRange(result.Notes);
      notes.AddRange(ToothMigrator.DroppedFields(json.RootElement, tooth));
    }

    byte[] bytes;
    if (fs.File.Exists(manifestPath)) {
      bytes = Merge(await fs.File.ReadAllBytesAsync(manifestPath), converted, notes);
    } else {
      bytes = Encoding.UTF8.GetBytes(converted.ToJsonString(new JsonSerializerOptions {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      }) + "\n");
    }

    if (ToothMigrator.ValidationNote(bytes) is string validation) {
      notes.Add(validation);
    }

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
    finally {
      if (fs.File.Exists(tempPath)) {
        fs.File.Delete(tempPath);
      }
    }

    try {
      fs.File.Delete(toothPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new InvalidOperationException(
          $"{ManifestFormat.ManifestFileName} was written, but {ManifestFormat.ToothFileName} "
          + $"could not be deleted ({ex.Message}). Delete it yourself: lip reads tooth.json "
          + "when an archive has both.", ex);
    }

    return notes;
  }

  /// The existing manifest.json (UTF-8 bytes, optionally with a BOM, which is kept) with the keys
  /// of converted it lacks appended. Notes are added for the keys whose values differ. Throws
  /// InvalidOperationException when the file is not a strict JSON object.
  public static byte[] Merge(byte[] existing, JsonObject converted, List<string> notes) {
    byte[] preamble = Encoding.UTF8.Preamble.ToArray();
    bool bom = existing.AsSpan().StartsWith(preamble);
    string text;
    JsonObject root;
    try {
      text = _strictUtf8.GetString(existing, bom ? preamble.Length : 0,
          existing.Length - (bom ? preamble.Length : 0));
      root = JsonNode.Parse(text, documentOptions: _strictJson) as JsonObject
          ?? throw new JsonException("the top level is not an object");
    }
    catch (Exception ex) when (ex is JsonException or DecoderFallbackException) {
      throw new InvalidOperationException(
          $"The existing {ManifestFormat.ManifestFileName} cannot be merged into: it is not a "
          + $"strict JSON object in UTF-8 ({ex.Message}). Fix it and run lip migrate again.", ex);
    }

    List<KeyValuePair<string, JsonNode?>> added = [];
    foreach ((string key, JsonNode? value) in converted) {
      if (!root.TryGetPropertyValue(key, out JsonNode? current)) {
        added.Add(new(key, value));
      } else if (!JsonNode.DeepEquals(current, value)) {
        notes.Add($"{ManifestFormat.ManifestFileName} already has \"{key}\": "
            + $"{Compact(current)}; it was kept instead of the converted {Compact(value)}.");
      }
    }

    if (added.Count == 0) {
      return existing;
    }

    string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    // The indentation of the root object's first key, when it is on a line of its own.
    Match indentMatch = IndentRegex().Match(text);
    string indent = indentMatch.Success && indentMatch.Groups[1].Value != ""
        ? indentMatch.Groups[1].Value
        : "  ";
    JsonSerializerOptions options = new() {
      WriteIndented = true,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      NewLine = newline,
    };
    if ((indent.All(c => c == ' ') || indent.All(c => c == '\t')) && indent.Length <= 127) {
      options.IndentCharacter = indent[0];
      options.IndentSize = indent.Length;
    }

    // The root object's closing brace is the last non-whitespace character of strict JSON;
    // the keys go right after the last value, before the whitespace preceding the brace.
    int close = text.TrimEnd().Length - 1;
    int insertAt = text[..close].TrimEnd().Length;

    StringBuilder insertion = new();
    bool first = root.Count == 0;
    foreach ((string key, JsonNode? value) in added) {
      insertion.Append(first ? "" : ",");
      first = false;
      string serialized = value is null ? "null" : value.ToJsonString(options);
      insertion
          .Append(newline).Append(indent)
          .Append(JsonSerializer.Serialize(key, options)).Append(": ")
          .Append(serialized.Replace(newline, newline + indent, StringComparison.Ordinal));
    }

    // An object that was written on one line gets its closing brace on a line of its own.
    if (!text[insertAt..close].Contains('\n', StringComparison.Ordinal)) {
      insertion.Append(newline);
    }

    string merged = text[..insertAt] + insertion + text[insertAt..];
    return [.. bom ? preamble : [], .. Encoding.UTF8.GetBytes(merged)];
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

  private static string Compact(JsonNode? node) =>
      node?.ToJsonString(new JsonSerializerOptions {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      }) ?? "null";

  /// The whitespace before the first key of the root object, after the last line break.
  [GeneratedRegex(@"\A\s*\{\s*?\n([ \t]*)""")]
  private static partial Regex IndentRegex();
}
