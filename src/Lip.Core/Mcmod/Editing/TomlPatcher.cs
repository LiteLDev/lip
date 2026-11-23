using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Json;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Lip.Core.Mcmod.Editing;

/// Changes the text of a TOML file so that it represents another JSON value, keeping what it
/// can of the text as it is: the comments, the order of the keys and the layout of what did not
/// change. A key whose value changed gets the new value in place of the old one; a key or a table
/// that is gone is removed with its lines; a new key goes after the last key of its table, and a
/// new table after the tables it belongs with.
///
/// What this cannot do, such as a value that changed between a table and something else, makes
/// Patch return null: the caller writes the value whole instead (TomlJson.Serialize). What Patch
/// returns is always read back (TomlJson.Parse) as the new value.
public static class TomlPatcher {
  /// A step of a path to a value: the name of a member of an object, or the index of an element
  /// of an array.
  private readonly record struct Step(string? Name, int Index) {
    public static Step Member(string name) => new(name, -1);

    public static Step Element(int index) => new(null, index);

    public override string ToString() => Name is null ? $"[{Index}]" : $".{Name}";
  }

  /// A table of the text: the root, a [table], or an element of an [[array of tables]], with
  /// the path of its value and its keys.
  private sealed class Context(List<Step> path, TableSyntaxBase? node, List<KeyValueSyntax> items) {
    public List<Step> Path { get; } = path;
    public TableSyntaxBase? Node { get; } = node;
    public List<KeyValueSyntax> Items { get; } = items;
    public bool Removed { get; set; }
  }

  /// A change of the text: what replaces the characters from Start to End, End excluded.
  /// Order keeps the insertions at the same place in the order they were made.
  private sealed record Edit(int Start, int End, string Text, int Order);

  /// The text of a TOML file whose value is the old value, changed to represent the new value;
  /// null when that cannot be done while keeping the rest of the text. old is the value of text,
  /// as TomlJson.Parse reads it.
  public static string? Patch(string text, JsonObject old, JsonObject updated) {
    try {
      string? patched = TryPatch(text, old, updated);
      return patched is not null && ReadsAs(patched, updated) ? patched : null;
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
        or TomlFormatException or FormatException or KeyNotFoundException) {
      return null;
    }
  }

  /// Whether a text reads as the value.
  private static bool ReadsAs(string text, JsonObject value) {
    using JsonDocument read = TomlJson.Parse(text);
    return JsonNode.DeepEquals(
        JsonNode.Parse(read.RootElement.GetRawText()), JsonNode.Parse(value.ToJsonString()));
  }

  private static string? TryPatch(string text, JsonObject old, JsonObject updated) {
    DocumentSyntax document = SyntaxParser.Parse(text, "", validate: true);
    if (document.HasErrors) {
      return null;
    }

    List<Context> contexts = Contexts(document, old);

    // The paths of the values that a key holds whole, and those that a table or a key is
    // below, which the text defines.
    Dictionary<string, (Context Context, KeyValueSyntax Item)> keys = [];
    HashSet<string> defined = [];
    foreach (Context context in contexts) {
      Define(defined, context.Path);
      foreach (KeyValueSyntax item in context.Items) {
        List<Step> path = [.. context.Path, .. Keys(item.Key!).Select(Step.Member)];
        keys[Text(path)] = (context, item);
        Define(defined, path);
      }
    }

    List<Edit> edits = [];
    int order = 0;

    // Tables that are gone.
    foreach (Context context in contexts.Where(c => c.Node is not null)) {
      JsonNode? now = Get(updated, context.Path);
      if (now is null) {
        context.Removed = true;
        edits.Add(new Edit(
            LineStart(text, context.Node!.Span.Start.Offset), After(text, context.Node.Span),
            "", order++));
      } else if (now is not JsonObject) {
        return null;
      }
    }

    // Keys that are gone or hold another value.
    foreach ((string path, (Context context, KeyValueSyntax item)) in keys) {
      if (context.Removed) {
        continue;
      }

      List<Step> steps = [.. context.Path, .. Keys(item.Key!).Select(Step.Member)];
      JsonNode? before = Get(old, steps);
      JsonNode? now = Get(updated, steps);
      if (now is null) {
        edits.Add(new Edit(
            LineStart(text, item.Span.Start.Offset), After(text, item.Span), "", order++));
      } else if (!JsonNode.DeepEquals(before, now)) {
        edits.Add(new Edit(
            item.Value!.Span.Start.Offset, item.Value.Span.End.Offset + 1, TomlJson.Inline(now),
            order++));
      }
    }

    // What is new.
    if (!Add(text, contexts, keys, defined, updated, [], edits, ref order)) {
      return null;
    }

    StringBuilder result = new(text);
    // From the end of the text, so that each edit leaves the places of the others as they are;
    // at one place, what is removed first, then what is inserted, the last insertion first.
    foreach (Edit edit in edits.OrderByDescending(e => e.Start)
        .ThenByDescending(e => e.End > e.Start).ThenByDescending(e => e.Order)) {
      result.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
    }

    return result.ToString();
  }

  /// The tables of the text, the root first, each with the path of its value in old.
  private static List<Context> Contexts(DocumentSyntax document, JsonObject old) {
    List<Context> contexts = [new Context([], null, [.. document.KeyValues])];
    // How many elements of each array of tables the text has defined so far.
    Dictionary<string, int> counts = [];
    foreach (TableSyntaxBase table in document.Tables) {
      List<string> names = Keys(table.Name!);
      List<Step> path = [];
      for (int i = 0; i < names.Count; i++) {
        path.Add(Step.Member(names[i]));
        bool last = i == names.Count - 1;
        if (last && table.Kind == SyntaxKind.TableArray) {
          string key = Text(path);
          int index = counts.GetValueOrDefault(key);
          counts[key] = index + 1;
          path.Add(Step.Element(index));
        } else if (!last && Get(old, path) is JsonArray) {
          // A table below an array of tables belongs to its last element so far.
          path.Add(Step.Element(counts.GetValueOrDefault(Text(path)) - 1));
        }
      }

      contexts.Add(new Context(path, table, [.. table.Items]));
    }

    return contexts;
  }

  /// Adds what the new value has at path and the text does not define.
  private static bool Add(
      string text,
      List<Context> contexts,
      Dictionary<string, (Context Context, KeyValueSyntax Item)> keys,
      HashSet<string> defined,
      JsonObject updated,
      List<Step> path,
      List<Edit> edits,
      ref int order) {
    if (Get(updated, path) is not JsonObject value) {
      return false;
    }

    foreach ((string name, JsonNode? member) in value) {
      if (member is null) {
        return false;
      }

      List<Step> memberPath = [.. path, Step.Member(name)];
      string memberText = Text(memberPath);
      if (keys.ContainsKey(memberText)) {
        continue;
      }

      if (defined.Contains(memberText)) {
        switch (member) {
          case JsonObject:
            if (!Add(text, contexts, keys, defined, updated, memberPath, edits, ref order)) {
              return false;
            }

            break;
          case JsonArray array:
            // Where the new elements of an array of tables go: after the last one of the text,
            // each after the one before it.
            int? at = null;
            for (int i = 0; i < array.Count; i++) {
              List<Step> elementPath = [.. memberPath, Step.Element(i)];
              if (Find(contexts, elementPath) is { } existing) {
                if (!Add(text, contexts, keys, defined, updated, elementPath, edits, ref order)) {
                  return false;
                }

                at = RegionEnd(text, contexts, existing);
              } else if (array[i] is JsonObject element && at is { } place) {
                edits.Add(Block(
                    text, place, Headers(memberPath), new JsonArray(element.DeepClone()),
                    order++));
              } else {
                return false;
              }
            }

            break;
          default:
            return false;
        }

        continue;
      }

      // A member that the text does not define: in the table of its object, as a key or under
      // a header of its own.
      Context? owner = Find(contexts, path);
      bool headed = TomlJson.IsHeaded(member);
      if (owner is not null && !headed) {
        int end = KeyEnd(text, owner, contexts);
        edits.Add(new Edit(
            end, end, Line(text, end, $"{TomlJson.QuoteKey(name)} = {TomlJson.Inline(member)}\n"),
            order++));
      } else if (owner is not null || !path.Any(step => step.Name is null)) {
        int at = owner is null ? text.Length : RegionEnd(text, contexts, owner);
        if (headed) {
          edits.Add(Block(text, at, Headers(memberPath), member.DeepClone(), order++));
        } else {
          edits.Add(Block(text, at, Headers(path), new JsonObject { [name] = member.DeepClone() },
              order++));
        }
      } else {
        return false;
      }
    }

    return true;
  }

  /// Writes a value under its own header at a place of the text, after an empty line.
  private static Edit Block(string text, int at, List<string> path, JsonNode value, int order) {
    string block = TomlJson.SerializeAt(path, value);
    string before = at == 0 ? "" : at > 0 && text[at - 1] != '\n' ? "\n\n" : "\n";
    return new Edit(at, at, before + block, order);
  }

  /// A line inserted at a place of the text, after a line break when the text before it does
  /// not end with one.
  private static string Line(string text, int at, string line) =>
      at > 0 && text[at - 1] != '\n' ? "\n" + line : line;

  /// Where a new key of a table goes: after its last key, or after its header; for the root,
  /// before the first table and the comments above it.
  private static int KeyEnd(string text, Context context, List<Context> contexts) {
    if (context.Items.Count > 0) {
      return After(text, context.Items[^1].Span);
    }

    if (context.Node is { } node) {
      return node.EndOfLineToken is { } end ? end.Span.End.Offset + 1 : text.Length;
    }

    TableSyntaxBase? first = contexts.Skip(1).FirstOrDefault()?.Node;
    if (first is null) {
      return text.Length;
    }

    int start = first.Span.Start.Offset;
    if (first.LeadingTrivia is { Count: > 0 } trivia) {
      start = Math.Min(start, trivia[0].Span.Start.Offset);
    }

    return LineStart(text, start);
  }

  /// The end of what a table holds in the text: of the last table after it that is below it,
  /// before any table that is not.
  private static int RegionEnd(string text, List<Context> contexts, Context context) {
    if (context.Node is null) {
      return text.Length;
    }

    int index = contexts.IndexOf(context);
    Context last = context;
    for (int i = index + 1; i < contexts.Count; i++) {
      if (!StartsWith(contexts[i].Path, context.Path)) {
        break;
      }

      last = contexts[i];
    }

    return After(text, last.Node!.Span);
  }

  private static bool StartsWith(List<Step> path, List<Step> prefix) =>
      path.Count >= prefix.Count && path.Take(prefix.Count).SequenceEqual(prefix);

  private static Context? Find(List<Context> contexts, List<Step> path) =>
      contexts.FirstOrDefault(c => !c.Removed && c.Path.SequenceEqual(path));

  /// The names of a header: the members of the path, without the elements of arrays.
  private static List<string> Headers(List<Step> path) =>
      [.. path.Where(step => step.Name is not null).Select(step => step.Name!)];

  /// Marks the path and every path above it as defined by the text.
  private static void Define(HashSet<string> defined, List<Step> path) {
    for (int i = 1; i <= path.Count; i++) {
      defined.Add(Text(path.Take(i)));
    }
  }

  private static string Text(IEnumerable<Step> path) => string.Concat(path);

  /// The character after a node: after its line break, which its span holds when it has one.
  private static int After(string text, SourceSpan span) =>
      Math.Min(text.Length, span.End.Offset + 1);

  /// The start of the line of a position when only spaces are before it on its line.
  private static int LineStart(string text, int offset) {
    int start = offset;
    while (start > 0 && text[start - 1] is ' ' or '\t') {
      start--;
    }

    return start == 0 || text[start - 1] == '\n' ? start : offset;
  }

  /// The names of a key, dotted or not.
  private static List<string> Keys(KeySyntax key) {
    List<string> names = [Name(key.Key!)];
    foreach (DottedKeyItemSyntax item in key.DotKeys) {
      names.Add(Name(item.Key!));
    }

    return names;
  }

  private static string Name(BareKeyOrStringValueSyntax key) => key switch {
    BareKeySyntax bare => bare.Key!.Text!,
    StringValueSyntax quoted => quoted.Value!,
    _ => throw new InvalidOperationException("A key of an unknown kind."),
  };

  /// The value at a path, or null.
  private static JsonNode? Get(JsonNode root, IEnumerable<Step> path) {
    JsonNode? node = root;
    foreach (Step step in path) {
      node = (step.Name, node) switch {
        ( { } name, JsonObject obj) => obj[name],
        (null, JsonArray array) when step.Index >= 0 && step.Index < array.Count =>
            array[step.Index],
        _ => null,
      };
      if (node is null) {
        return null;
      }
    }

    return node;
  }
}
