using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lip.Core.Infrastructure;

namespace Lip.Cli.Workspaces;

/// The workspace list (protocol/lip-tui.md, "Workspaces"): the absolute paths of the workspaces
/// that the user added to the interface, in order, kept in the file workspaces.json of the state
/// directory. Every change reads the file again, so that the interfaces that run at once, in the
/// daemon or in other processes, share it. Without a path, the list is kept in memory only.
public sealed class WorkspaceList(IFileSystem fileSystem, string? path) {
  /// The name of the file in the state directory.
  public const string FileName = "workspaces.json";

  // The changes of the lists of this process, one at a time.
  private static readonly object s_gate = new();

  private List<string> _memory = [];

  /// Compares paths as the file system of the platform does: ignoring case on Windows.
  public static StringComparer PathComparer { get; } =
      OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

  /// The file system of the paths.
  public IFileSystem FileSystem => fileSystem;

  /// The path of the file, or null when the list is kept in memory.
  public string? Path => path;

  /// The list of the state directory of the current invocation.
  public static WorkspaceList InStateDirectory(IFileSystem fileSystem) =>
      new(fileSystem, System.IO.Path.Join(LipDirectories.State, FileName));

  /// The paths of the list, in order. A file that is missing or not a JSON array is read as an
  /// empty list, and its elements that are not absolute paths, or name a workspace named before
  /// them, are left out.
  public IReadOnlyList<string> Read() {
    lock (s_gate) {
      return [.. Load()];
    }
  }

  /// Adds the directory at the end of the list, unless the list names it; returns the path of
  /// the list that names it.
  public string Add(string directory) {
    string added = Normalize(directory);
    lock (s_gate) {
      List<string> list = Load();
      if (list.FirstOrDefault(p => PathComparer.Equals(p, added)) is { } listed) {
        return listed;
      }

      list.Add(added);
      Save(list);
      return added;
    }
  }

  /// Removes the directory from the list; no file in it changes.
  public void Remove(string directory) {
    string removed = Normalize(directory);
    lock (s_gate) {
      List<string> list = Load();
      if (list.RemoveAll(p => PathComparer.Equals(p, removed)) > 0) {
        Save(list);
      }
    }
  }

  /// Whether the two paths name the same workspace.
  public bool Same(string a, string b) => PathComparer.Equals(Normalize(a), Normalize(b));

  /// The absolute path, without a separator at its end unless it is a root.
  public string Normalize(string directory) {
    string full = fileSystem.Path.GetFullPath(directory);
    string trimmed = full.TrimEnd(
        fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar);
    return trimmed.Length == 0 || fileSystem.Path.GetPathRoot(full) == full ? full : trimmed;
  }

  private List<string> Load() {
    if (path is null) {
      return [.. _memory];
    }

    string text;
    try {
      text = fileSystem.File.ReadAllText(path);
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
      return [];
    }

    List<string> list = [];
    try {
      using JsonDocument document = JsonDocument.Parse(text);
      if (document.RootElement.ValueKind != JsonValueKind.Array) {
        return [];
      }

      foreach (JsonElement element in document.RootElement.EnumerateArray()) {
        if (element.ValueKind == JsonValueKind.String
            && element.GetString() is { } entry
            && fileSystem.Path.IsPathFullyQualified(entry)) {
          string normalized = Normalize(entry);
          if (!list.Contains(normalized, PathComparer)) {
            list.Add(normalized);
          }
        }
      }
    }
    catch (JsonException) {
      return [];
    }

    return list;
  }

  /// Writes the list atomically (protocol/README.md, "Files").
  private void Save(List<string> list) {
    if (path is null) {
      _memory = list;
      return;
    }

    using MemoryStream stream = new();
    using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions {
      Indented = true,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    })) {
      writer.WriteStartArray();
      foreach (string entry in list) {
        writer.WriteStringValue(entry);
      }

      writer.WriteEndArray();
    }

    stream.WriteByte((byte)'\n');
    string directory = fileSystem.Path.GetDirectoryName(path)!;
    fileSystem.Directory.CreateDirectory(directory);
    string temporary = fileSystem.Path.Join(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
    try {
      fileSystem.File.WriteAllBytes(temporary, stream.ToArray());
      fileSystem.File.Move(temporary, path, overwrite: true);
    }
    catch {
      try {
        fileSystem.File.Delete(temporary);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      }

      throw;
    }
  }
}
