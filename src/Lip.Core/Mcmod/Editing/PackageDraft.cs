using System.Text.Json.Nodes;

namespace Lip.Core.Mcmod.Editing;

/// Thrown when a lip.toml cannot be edited as a PackageDraft: a key of protocol/lip.toml.md
/// holds a value of another type than the one it must have, which the editor has no field for.
public sealed class PackageDraftException(string message) : FormatException(message);

/// What the user edits of a package file (protocol/lip.toml.md, "Keys"), as written: each
/// string is the template as written, not its rendering. Every text is what the user typed;
/// an empty optional text stands for a key that is not written.
///
/// A draft read from a lip.toml (Read) keeps the value it was read from, so that ToJson changes
/// only what the draft changed: keys that the draft has no field for keep their values.
public sealed class PackageDraft {
  public string Id { get; set; } = "";
  public string Version { get; set; } = "";
  public string Name { get; set; } = "";
  public string Description { get; set; } = "";
  public List<string> Tags { get; set; } = [];
  public string Logo { get; set; } = "";
  public List<VariantDraft> Variants { get; set; } = [];

  /// What lip pkg init writes (protocol/lip-pkg.md), with the id given: the version 0.1.0 and
  /// one entry of the default variant for the server on win-x64.
  public static PackageDraft New(string id = "") => new() {
    Id = id,
    Version = "0.1.0",
    Variants = [new VariantDraft { Entries = [new EntryDraft()] }],
  };

  /// The draft of a lip.toml as written (TomlJson.Parse). Throws PackageDraftException when a
  /// key that the draft has a field for holds a value of another type.
  public static PackageDraft Read(JsonObject written) {
    // A file of another format, such as a newer one, is not one the draft can write.
    if (written.ContainsKey(PackageFormat.FormatKey)
        && !(written[PackageFormat.FormatKey] is JsonValue format
            && format.TryGetValue(out long number) && number == PackageFormat.Format)) {
      throw new PackageDraftException(
          $"{PackageFormat.FormatKey} is not {PackageFormat.Format}.");
    }

    PackageDraft draft = new() {
      Id = String(written, "id", "id"),
      Version = String(written, "version", "version"),
    };
    if (Object(written, "info", "info") is { } info) {
      draft.Name = String(info, "name", "info.name");
      draft.Description = String(info, "description", "info.description");
      draft.Tags = Strings(info, "tags", "info.tags");
      draft.Logo = String(info, "logo", "info.logo");
    }

    if (Object(written, "variants", "variants") is { } variants) {
      foreach ((string name, JsonNode? value) in variants) {
        string path = $"variants[\"{name}\"]";
        if (value is not JsonArray entries) {
          throw Wrong(path, "an array of tables");
        }

        VariantDraft variant = new() { Name = name };
        for (int i = 0; i < entries.Count; i++) {
          variant.Entries.Add(EntryDraft.Read(entries[i], $"{path}[{i}]"));
        }

        draft.Variants.Add(variant);
      }
    }

    return draft;
  }

  /// The lip.toml as written with the values of the draft: original, when given, with the keys
  /// of the draft set to its values, and those that it leaves empty removed, every other key
  /// kept as it is; format is 4. original is not changed.
  public JsonObject ToJson(JsonObject? original = null) {
    JsonObject result = original?.DeepClone().AsObject() ?? [];
    result[PackageFormat.FormatKey] = PackageFormat.Format;
    result["id"] = Id;
    result["version"] = Version;

    // A key that is set keeps its place among the keys; a new one goes after them, so the keys
    // of a new file are in the order of protocol/lip.toml.md.
    JsonObject info = result["info"] as JsonObject ?? [];
    // A table that was empty already stays.
    bool emptyInfo = info.Parent is not null && info.Count == 0;
    SetText(info, "name", Name);
    SetText(info, "description", Description);
    SetList(info, "tags", Tags);
    SetText(info, "logo", Logo);
    if (info.Count == 0 && !emptyInfo) {
      result.Remove("info");
    } else if (info.Parent is null) {
      result["info"] = info;
    }

    // Each entry keeps the other keys of the entry it was read from.
    JsonObject variants = [];
    foreach (VariantDraft variant in Variants) {
      variants[variant.Name] = new JsonArray(
          [.. variant.Entries.Select(entry => (JsonNode?)entry.ToJson(entry.Source))]);
    }

    result["variants"] = variants;
    return result;
  }

  /// Sets a string key, or removes it when the text is empty.
  internal static void SetText(JsonObject obj, string key, string text) {
    string? current = obj[key] is JsonValue value && value.TryGetValue(out string? s) ? s : null;
    if (text.Length == 0) {
      obj.Remove(key);
    } else if (current != text) {
      obj[key] = text;
    }
  }

  /// Sets an array of strings, without its empty strings; removes it when nothing is left,
  /// unless it holds an empty array.
  internal static void SetList(JsonObject obj, string key, IEnumerable<string> items) {
    List<string> list = [.. items.Where(item => item.Length > 0)];
    if (list.Count == 0) {
      if (!(obj[key] is JsonArray { Count: 0 })) {
        obj.Remove(key);
      }

      return;
    }

    if (obj[key] is JsonArray array && array.Count == list.Count
        && array.Select(item => item is JsonValue v && v.TryGetValue(out string? s) ? s : null)
            .SequenceEqual(list)) {
      return;
    }

    obj[key] = new JsonArray([.. list.Select(item => (JsonNode?)JsonValue.Create(item))]);
  }

  internal static string String(JsonObject obj, string key, string path) =>
      obj[key] switch {
        null when !obj.ContainsKey(key) => "",
        JsonValue value when value.TryGetValue(out string? text) => text,
        _ => throw Wrong(path, "a string"),
      };

  internal static List<string> Strings(JsonObject obj, string key, string path) {
    if (!obj.ContainsKey(key)) {
      return [];
    }

    if (obj[key] is not JsonArray array) {
      throw Wrong(path, "an array of strings");
    }

    List<string> result = [];
    for (int i = 0; i < array.Count; i++) {
      result.Add(array[i] is JsonValue value && value.TryGetValue(out string? text)
          ? text
          : throw Wrong($"{path}[{i}]", "a string"));
    }

    return result;
  }

  internal static JsonObject? Object(JsonObject obj, string key, string path) =>
      !obj.ContainsKey(key) ? null
          : obj[key] as JsonObject ?? throw Wrong(path, "a table");

  internal static PackageDraftException Wrong(string path, string type) =>
      new($"{path} is not {type}.");
}

/// A variant: its name, "" for the default variant, and its entries.
public sealed class VariantDraft {
  public string Name { get; set; } = "";
  public List<EntryDraft> Entries { get; set; } = [];
}

/// An entry of a variant (protocol/lip.toml.md, "Keys").
public sealed class EntryDraft {
  public string Side { get; set; } = "server";
  public string Platform { get; set; } = "win-x64";
  public List<DependencyDraft> Dependencies { get; set; } = [];
  public List<AssetDraft> Assets { get; set; } = [];
  public List<string> Preserve { get; set; } = [];
  public List<string> Purge { get; set; } = [];
  public List<string> PreInstall { get; set; } = [];
  public List<string> PostInstall { get; set; } = [];
  public List<string> PreUninstall { get; set; } = [];
  public List<string> PostUninstall { get; set; } = [];

  /// The entry as it was read, whose other keys ToJson keeps; null for a new entry.
  public JsonObject? Source { get; set; }

  /// The names of the hooks, in the order of protocol/lip.toml.md.
  public static readonly string[] HookNames =
      ["pre_install", "post_install", "pre_uninstall", "post_uninstall"];

  /// The commands of a hook, by its index in HookNames.
  public List<string> Hook(int index) => index switch {
    0 => PreInstall,
    1 => PostInstall,
    2 => PreUninstall,
    _ => PostUninstall,
  };

  internal static EntryDraft Read(JsonNode? node, string path) {
    if (node is not JsonObject obj) {
      throw PackageDraft.Wrong(path, "a table");
    }

    EntryDraft entry = new() {
      Side = PackageDraft.String(obj, "side", $"{path}.side"),
      Platform = PackageDraft.String(obj, "platform", $"{path}.platform"),
      Preserve = PackageDraft.Strings(obj, "preserve", $"{path}.preserve"),
      Purge = PackageDraft.Strings(obj, "purge", $"{path}.purge"),
      Source = obj,
    };
    if (PackageDraft.Object(obj, "deps", $"{path}.deps") is { } deps) {
      foreach ((string key, JsonNode? value) in deps) {
        entry.Dependencies.Add(new DependencyDraft {
          Package = key,
          Range = value is JsonValue v && v.TryGetValue(out string? range)
              ? range
              : throw PackageDraft.Wrong($"{path}.deps[\"{key}\"]", "a string"),
        });
      }
    }

    if (obj.ContainsKey("assets")) {
      if (obj["assets"] is not JsonArray assets) {
        throw PackageDraft.Wrong($"{path}.assets", "an array of tables");
      }

      for (int i = 0; i < assets.Count; i++) {
        entry.Assets.Add(AssetDraft.Read(assets[i], $"{path}.assets[{i}]"));
      }
    }

    if (PackageDraft.Object(obj, "hooks", $"{path}.hooks") is { } hooks) {
      for (int i = 0; i < HookNames.Length; i++) {
        entry.Hook(i).AddRange(
            PackageDraft.Strings(hooks, HookNames[i], $"{path}.hooks.{HookNames[i]}"));
      }
    }

    return entry;
  }

  /// The entry as written: source with the keys of the entry set, or a new table.
  internal JsonObject ToJson(JsonObject? source) {
    JsonObject result = source?.DeepClone().AsObject() ?? [];
    result["side"] = Side;
    result["platform"] = Platform;

    JsonObject deps = [];
    foreach (DependencyDraft dependency in Dependencies.Where(d => d.Package.Length > 0)) {
      deps[dependency.Package] = dependency.Range;
    }

    if (deps.Count > 0 || result["deps"] is JsonObject { Count: 0 }) {
      if (!JsonNode.DeepEquals(result["deps"], deps)) {
        result["deps"] = deps;
      }
    } else {
      result.Remove("deps");
    }

    JsonArray? sourceAssets = source?["assets"] as JsonArray;
    JsonArray assets = [];
    foreach (AssetDraft asset in Assets) {
      assets.Add((JsonNode)asset.ToJson(asset.Source));
    }

    if (assets.Count > 0 || sourceAssets is { Count: 0 }) {
      if (!JsonNode.DeepEquals(result["assets"], assets)) {
        result["assets"] = assets;
      }
    } else {
      result.Remove("assets");
    }

    PackageDraft.SetList(result, "preserve", Preserve);
    PackageDraft.SetList(result, "purge", Purge);

    JsonObject hooks = result["hooks"] as JsonObject ?? [];
    bool emptyHooks = hooks.Parent is not null && hooks.Count == 0;
    for (int i = 0; i < HookNames.Length; i++) {
      PackageDraft.SetList(hooks, HookNames[i], Hook(i));
    }

    if (hooks.Count > 0 || emptyHooks) {
      if (hooks.Parent is null) {
        result["hooks"] = hooks;
      }
    } else {
      result.Remove("hooks");
    }

    return result;
  }
}

/// A dependency: the key, "<package id>" or "<package id>#<variant>", and the range.
public sealed class DependencyDraft {
  public string Package { get; set; } = "";
  public string Range { get; set; } = "";

  /// The package id of the key, without its variant.
  public string Id => Package.Split('#')[0];
}

/// An asset of an entry.
public sealed class AssetDraft {
  /// The types of assets, in the order of protocol/lip.toml.md.
  public static readonly string[] Types = ["self", "zip", "mcmod"];

  public string Type { get; set; } = "self";
  public List<string> Urls { get; set; } = [];
  public List<PlaceDraft> Place { get; set; } = [];

  /// The asset as it was read; null for a new one.
  public JsonObject? Source { get; set; }

  internal static AssetDraft Read(JsonNode? node, string path) {
    if (node is not JsonObject obj) {
      throw PackageDraft.Wrong(path, "a table");
    }

    AssetDraft asset = new() {
      Type = PackageDraft.String(obj, "type", $"{path}.type"),
      Urls = PackageDraft.Strings(obj, "urls", $"{path}.urls"),
      Source = obj,
    };
    if (obj.ContainsKey("place")) {
      if (obj["place"] is not JsonArray place) {
        throw PackageDraft.Wrong($"{path}.place", "an array of tables");
      }

      for (int i = 0; i < place.Count; i++) {
        if (place[i] is not JsonObject member) {
          throw PackageDraft.Wrong($"{path}.place[{i}]", "a table");
        }

        asset.Place.Add(new PlaceDraft {
          Src = PackageDraft.String(member, "src", $"{path}.place[{i}].src"),
          Dest = PackageDraft.String(member, "dest", $"{path}.place[{i}].dest"),
          Source = member,
        });
      }
    }

    return asset;
  }

  internal JsonObject ToJson(JsonObject? source) {
    JsonObject result = source?.DeepClone().AsObject() ?? [];
    result["type"] = Type;
    // urls is ignored for "self", but kept when it is there.
    if (Type != "self" || result.ContainsKey("urls")) {
      PackageDraft.SetList(result, "urls", Urls);
    }

    JsonArray place = [];
    foreach (PlaceDraft member in Place) {
      JsonObject value = member.Source?.DeepClone().AsObject() ?? [];
      value["src"] = member.Src;
      value["dest"] = member.Dest;
      place.Add((JsonNode)value);
    }

    if (place.Count > 0 || result["place"] is JsonArray { Count: 0 } && Type != "mcmod") {
      if (!JsonNode.DeepEquals(result["place"], place)) {
        result["place"] = place;
      }
    } else {
      result.Remove("place");
    }

    return result;
  }
}

/// A member of place: src and dest.
public sealed class PlaceDraft {
  public string Src { get; set; } = "";
  public string Dest { get; set; } = "";

  /// The member as it was read; null for a new one.
  public JsonObject? Source { get; set; }
}
