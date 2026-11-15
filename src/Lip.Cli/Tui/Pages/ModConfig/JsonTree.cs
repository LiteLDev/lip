using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Config;

namespace Lip.Cli.Tui.Pages.ModConfig;

/// What a row of a JsonTree is.
internal enum TreeRowKind {
  /// A member of an object.
  Member,

  /// An element of an array.
  Element,

  /// The value of a file that holds neither an object nor an array.
  Value,

  /// The row after the elements of an array that adds one.
  Add,
}

/// A row of a JsonTree: its name, its value, whether the user may not change it, and its index
/// in the object or the array.
internal sealed record TreeRow(
    TreeRowKind Kind, string Name, JsonNode? Value, bool ReadOnly, int Index) {
  /// Whether the value is an object or an array, which Enter opens.
  public bool IsContainer => Value is JsonObject or JsonArray;

  /// Whether the value is true or false, which Space switches.
  public bool IsBoolean =>
      Value is JsonValue value
      && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False;
}

/// A JSON configuration file as it is edited (protocol/lip-tui.md, "Configuration"): a tree of
/// objects and arrays, of which one level is shown at a time.
internal sealed class JsonTree(JsonNode root) {
  private readonly List<(JsonNode Node, string Name, int Selected)> _path = [];
  private JsonNode _root = root;
  private string _saved = JsonConfigFile.Serialize(root);
  private bool? _dirty;

  /// Whether the tree is not what the file holds. Computed again only after a change: the
  /// screen asks several times a frame.
  public bool Dirty => _dirty ??= JsonConfigFile.Serialize(_root) != _saved;

  /// The names of the levels from the top to the one shown.
  public IReadOnlyList<string> Location => [.. _path.Select(level => level.Name)];

  private JsonNode Current => _path.Count == 0 ? _root : _path[^1].Node;

  /// The rows of the level shown.
  public IReadOnlyList<TreeRow> Rows() => Current switch {
    JsonObject o => [
      .. o.Select((member, i) => new TreeRow(
          TreeRowKind.Member, member.Key, member.Value,
          JsonConfigFile.IsReadOnly(o, member.Key), i)),
    ],
    JsonArray a => [
      .. a.Select((value, i) => new TreeRow(TreeRowKind.Element, $"[{i}]", value, false, i)),
      new TreeRow(TreeRowKind.Add, "", null, false, a.Count),
    ],
    _ => [new TreeRow(TreeRowKind.Value, "", Current, false, 0)],
  };

  /// Shows the level of the object or the array of a row; selected is the index of that row,
  /// which Up gives back.
  public void Open(TreeRow row, int selected) {
    if (row.Value is JsonObject or JsonArray) {
      _path.Add((row.Value, row.Name, selected));
    }
  }

  /// Shows the level above, and returns the index of the row that was opened; null at the top.
  public int? Up() {
    if (_path.Count == 0) {
      return null;
    }

    int selected = _path[^1].Selected;
    _path.RemoveAt(_path.Count - 1);
    return selected;
  }

  /// Gives the row another value.
  public void Set(TreeRow row, JsonNode? value) {
    switch (Current) {
      case JsonObject o when row.Kind == TreeRowKind.Member:
        o[row.Name] = value;
        break;
      case JsonArray a when row.Kind == TreeRowKind.Element && row.Index < a.Count:
        a[row.Index] = value;
        break;
      default:
        if (row.Kind == TreeRowKind.Value && _path.Count == 0 && value is not null) {
          _root = value;
        }

        break;
    }

    _dirty = null;
  }

  /// Switches the row between true and false.
  public void Toggle(TreeRow row) {
    if (row.IsBoolean) {
      Set(row, JsonValue.Create(row.Value!.GetValueKind() != JsonValueKind.True));
    }
  }

  /// Removes the element of a row from the array shown.
  public void Remove(TreeRow row) {
    if (Current is JsonArray a && row.Kind == TreeRowKind.Element && row.Index < a.Count) {
      a.RemoveAt(row.Index);
      _dirty = null;
    }
  }

  /// Adds a value to the end of the array shown, and returns its index; -1 when no array is
  /// shown.
  public int Add(JsonNode? value) {
    if (Current is not JsonArray a) {
      return -1;
    }

    a.Add(value);
    _dirty = null;
    return a.Count - 1;
  }

  /// The text of the file that holds the tree.
  public string Serialize() => JsonConfigFile.Serialize(_root);

  /// The file now holds text.
  public void MarkSaved(string text) {
    _saved = text;
    _dirty = null;
  }
}
