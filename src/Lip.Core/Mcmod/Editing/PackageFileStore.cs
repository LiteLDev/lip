using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Json;

namespace Lip.Core.Mcmod.Editing;

/// A lip.toml as the editor read it: its path, and, when it exists, its text and its value as
/// written (TomlJson), templates not rendered.
public sealed record PackageFileText(string Path, string? Text, JsonObject? Written, bool Bom = false) {
  public bool Exists => Text is not null;
}

/// What the editor writes to lip.toml: the text, and whether it keeps the text of the file that
/// was read, its comments among it, where the values did not change.
public sealed record PackageFileUpdate(string Text, bool KeepsText, bool Changed);

/// Reads and writes the lip.toml of a directory for the editor.
public static class PackageFileStore {
  private static readonly UTF8Encoding _utf8 = new(
      encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  /// Reads the lip.toml of a directory; one that does not exist has no text. Throws
  /// TomlFormatException when it is not a TOML file that represents a JSON object, and
  /// IOException when it cannot be read.
  public static PackageFileText Read(IFileSystem fs, string directory) {
    string path = fs.Path.Join(directory, PackageFormat.FileName);
    if (!fs.File.Exists(path)) {
      return new PackageFileText(path, null, null);
    }

    byte[] bytes = fs.File.ReadAllBytes(path);
    bool bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
    string text;
    try {
      text = _utf8.GetString(bom ? bytes[Encoding.UTF8.Preamble.Length..] : bytes);
    }
    catch (DecoderFallbackException ex) {
      throw new TomlFormatException(
          "The file is not valid UTF-8 text. Save it with the UTF-8 encoding.", ex);
    }

    using JsonDocument document = TomlJson.Parse(text);
    return new PackageFileText(
        path, text, JsonNode.Parse(document.RootElement.GetRawText())!.AsObject(), bom);
  }

  /// The text to write for a draft: for a file that exists, its text changed as little as it
  /// can be (TomlPatcher), or written whole when it cannot be; otherwise written whole.
  public static PackageFileUpdate Update(PackageFileText file, PackageDraft draft) {
    JsonObject updated = draft.ToJson(file.Written);
    if (file is { Text: { } text, Written: { } written }) {
      if (JsonNode.DeepEquals(JsonNode.Parse(written.ToJsonString()), JsonNode.Parse(updated.ToJsonString()))) {
        return new PackageFileUpdate(text, KeepsText: true, Changed: false);
      }

      if (TomlPatcher.Patch(text, written, updated) is { } patched) {
        return new PackageFileUpdate(patched, KeepsText: true, Changed: true);
      }
    }

    return new PackageFileUpdate(TomlJson.Serialize(updated), KeepsText: !file.Exists, Changed: true);
  }

  /// Whether the file is still as it was read: false when it was changed, created or deleted
  /// since.
  public static bool Unchanged(IFileSystem fs, PackageFileText file) {
    if (!fs.File.Exists(file.Path)) {
      return !file.Exists;
    }

    try {
      return file.Exists
          && Read(fs, fs.Path.GetDirectoryName(file.Path)!) is { } now
          && now.Text == file.Text && now.Bom == file.Bom;
    }
    catch (Exception ex) when (ex is TomlFormatException or IOException
        or UnauthorizedAccessException) {
      return false;
    }
  }

  /// Writes the text to the file atomically (protocol/README.md, "Files"): to a file next to it
  /// that then replaces it. A byte order mark that the file had is kept.
  public static void Write(IFileSystem fs, PackageFileText file, string text) {
    string temporary = file.Path + ".tmp";
    byte[] bytes = _utf8.GetBytes(text);
    if (file.Bom) {
      bytes = [.. Encoding.UTF8.Preamble, .. bytes];
    }

    fs.File.WriteAllBytes(temporary, bytes);
    try {
      fs.File.Move(temporary, file.Path, overwrite: true);
    }
    catch {
      try {
        fs.File.Delete(temporary);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // The renaming failed already; that is what the user hears of.
      }

      throw;
    }
  }
}
