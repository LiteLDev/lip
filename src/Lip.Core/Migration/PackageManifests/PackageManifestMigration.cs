using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNet.Globbing;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using Semver;

namespace Lip.Core.Migration.PackageManifests;

public static partial class PackageManifestMigration {
  public static PackageManifest Migrate(JsonDocument jsonDocument) {
    int formatVersion = jsonDocument.RootElement.GetProperty("format_version").GetInt32();
    RunTrace.Write("parse", $"reading a tooth.json of format_version {formatVersion} as format 3");

    return formatVersion switch {
      1 => MigrateV2ToV3(MigrateV1ToV2(Deserialize<PackageManifestV1>(jsonDocument))),
      2 => MigrateV2ToV3(Deserialize<PackageManifestV2>(jsonDocument)),
      3 => ParseV3(jsonDocument),
      _ => throw new NotSupportedException(
          $"A tooth.json of format_version {formatVersion} cannot be read as format 3; only "
          + "format_version 1, 2 and 3 can."),
    };
  }

  /// Reads a tooth.json of format 1 or 2 into its model. Throws JsonException naming the field
  /// that is missing or of the wrong type.
  private static T Deserialize<T>(JsonDocument jsonDocument) {
    try {
      return JsonSerializer.Deserialize<T>(jsonDocument)!;
    }
    catch (JsonException ex) {
      throw FieldError(ex);
    }
  }

  /// A JsonException of System.Text.Json, in words for the user, naming the field by its path
  /// inside tooth.json.
  private static JsonException FieldError(JsonException ex) {
    bool named = ex.Path is { Length: > 1 } path && path.StartsWith("$.", StringComparison.Ordinal);
    string field = named ? $"the field '{ex.Path![2..]}'" : "a required field";
    // System.Text.Json's own errors are about a missing field or a value of the wrong type and
    // carry the position in their message; the lip converters say why a value is not valid.
    bool ownError = ex.Message.Contains(" Path: ", StringComparison.Ordinal)
        || ex.Message.Contains("required propert", StringComparison.Ordinal);
    return new JsonException(
        ownError || !named
            ? $"In tooth.json, {field} is missing or has a value of the wrong type."
            : $"In tooth.json, {field} has a value that is not valid.",
        ex.Path, ex.LineNumber, ex.BytePositionInLine, ex);
  }

  /// A JsonException naming a field of a tooth.json of format 1 and what it must be.
  private static JsonException InvalidField(string field, string expected) =>
      new($"In tooth.json, the field '{field}' must be {expected}.", $"$.{field}", null, null);

  private static PackageManifestV2 MigrateV1ToV2(PackageManifestV1 manifestV1) {
    Dictionary<string, JsonElement>? data = manifestV1.Information?.Data;
    string Text(string key) {
      if (data is null || !data.TryGetValue(key, out JsonElement value)
          || value.ValueKind == JsonValueKind.Null) {
        return "";
      }
      return value.ValueKind == JsonValueKind.String
          ? value.GetString()!
          : throw InvalidField($"information.{key}", "a string");
    }
    PackageManifestV2Info info = new() {
      Name = Text("name"),
      Description = Text("description"),
      Author = Text("author"),
      Tags = data is not null && data.TryGetValue("tags", out JsonElement t) && t.ValueKind == JsonValueKind.Array
            ? [.. t.EnumerateArray().Select(e => e.ValueKind switch {
              JsonValueKind.String => e.GetString()!,
              JsonValueKind.Null => "",
              _ => throw InvalidField("information.tags", "an array of strings"),
            })]
            : []
    };

    List<PackageManifestV2Platform> platforms = [];

    PackageManifestV2Platform GetOrCreatePlatform(string goos, string? goarch) {
      PackageManifestV2Platform? platform = platforms.SingleOrDefault(p => p.GOOS == goos && p.GOARCH == goarch);
      if (platform is not null) {
        return platform;
      }

      PackageManifestV2Platform result = new() {
        GOOS = goos,
        GOARCH = goarch
      };
      platforms.Add(result);
      return result;
    }

    static bool HasCommands(PackageManifestV2Commands commands) {
      return commands.PreInstall is not null
             || commands.PostInstall is not null
             || commands.PreUninstall is not null
             || commands.PostUninstall is not null;
    }

    PackageManifestV2Commands? commands = null;
    if (manifestV1.Commands is not null) {
      PackageManifestV2Commands rootCommands = new();
      for (int i = 0; i < manifestV1.Commands.Count; i++) {
        PackageManifestV1Command? cmd = manifestV1.Commands[i]
            ?? throw InvalidField($"commands[{i}]", "an object");
        if (cmd.Type is null) {
          throw InvalidField($"commands[{i}].type", "\"install\" or \"uninstall\"");
        }
        if (cmd.Commands is null) {
          throw InvalidField($"commands[{i}].commands", "an array of strings");
        }
        PackageManifestV2Commands targetCommands;
        if (string.IsNullOrEmpty(cmd.GOOS)) {
          targetCommands = rootCommands;
        } else {
          PackageManifestV2Platform platform = GetOrCreatePlatform(cmd.GOOS, cmd.GOARCH);
          platform.Commands ??= new();
          targetCommands = platform.Commands;
        }

        if (cmd.Type.Equals("install", StringComparison.OrdinalIgnoreCase)) {
          targetCommands.PostInstall ??= [];
          targetCommands.PostInstall.AddRange(cmd.Commands);
        } else if (cmd.Type.Equals("uninstall", StringComparison.OrdinalIgnoreCase)) {
          targetCommands.PreUninstall ??= [];
          targetCommands.PreUninstall.AddRange(cmd.Commands);
        }
      }
      if (HasCommands(rootCommands)) {
        commands = rootCommands;
      }
    }

    foreach ((string name, List<List<string>> groups) in manifestV1.Dependencies ?? []) {
      if (groups is null || groups.Any(g => g is null)) {
        throw InvalidField($"dependencies.{name}", "an array of arrays of version ranges");
      }
    }
    for (int i = 0; i < (manifestV1.Placement?.Count ?? 0); i++) {
      PackageManifestV1Placement? placement = manifestV1.Placement![i];
      if (placement?.Source is null || placement.Destination is null) {
        throw InvalidField($"placement[{i}]", "an object with the strings \"source\" and \"destination\"");
      }
    }

    return new PackageManifestV2 {
      FormatVersion = 2,
      Tooth = manifestV1.Tooth,
      Version = manifestV1.Version,
      Info = info,
      AssetUrl = null,
      Commands = commands,
      Dependencies = manifestV1.Dependencies?.ToDictionary(
            kvp => kvp.Key,
            kvp => string.Join(" || ", kvp.Value.Select(group => string.Join(" && ", group)))
        ),
      Prerequisites = null,
      Files = manifestV1.Placement is not null
            ? new PackageManifestV2Files {
              Place = [.. manifestV1.Placement.Select(p => new PackageManifestV2Place { Src = p.Source, Dest = p.Destination.TrimEnd('*') })]
            }
            : null,
      Platforms = platforms.Count > 0 ? platforms : null
    };
  }

  private static PackageManifest MigrateV2ToV3(PackageManifestV2 manifestV2) {
    Dictionary<string, object> model = new()
    {
      { "version", manifestV2.Version },
      { "path", manifestV2.Tooth }
    };

    string RenderTemplate(string? template) {
      if (template is null) return "";
      // Convert $(variable) format to {{variable}} format for Scriban
      string converted = TemplateVariableRegex().Replace(template, "{{$1}}");
      return Render(converted, template, model);
    }

    List<PackageManifestAssetPlacement> Placements(List<PackageManifestV2Place> places) => [
      .. places.Select(pl => new PackageManifestAssetPlacement {
        Src = RenderTemplate(pl.Src).TrimEnd('*'),
        Dst = RenderTemplate(pl.Dest),
        Type = pl.Src.EndsWith('*')
              ? PackageManifestAssetPlacement.PlacementType.Directory
              : PackageManifestAssetPlacement.PlacementType.File
      }),
    ];

    SemVersion version;
    try {
      version = SemVersion.Parse(manifestV2.Version, SemVersionStyles.Any);
    }
    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) {
      throw new FormatException(
          $"In tooth.json, the version '{manifestV2.Version}' is not a valid version, e.g. 1.2.3.",
          ex);
    }

    PackageManifest manifest = new() {
      Path = manifestV2.Tooth,
      Version = version,
      Info = new PackageManifestInfo {
        Name = manifestV2.Info.Name,
        Description = manifestV2.Info.Description,
        Tags = manifestV2.Info.Tags,
        AvatarUrl = RenderTemplate(manifestV2.Info.AvatarUrl)
      },
      Variants = []
    };

    // Without platforms, the root fields make one variant without a platform, whose ranges are
    // read as SemVer ranges and whose places, without an asset_url, are of a self asset; a platform
    // uses its own fields and else the root ones, with ranges read as npm ranges.
    IEnumerable<PackageManifestV2Platform?> platforms =
        (IEnumerable<PackageManifestV2Platform?>?)manifestV2.Platforms ?? [null];
    foreach (PackageManifestV2Platform? p in platforms) {
      Dictionary<PackageId, SemVersionRange> dependencies = [];
      foreach (KeyValuePair<string, string> kvp in
          p?.Dependencies ?? manifestV2.Dependencies ?? []) {
        try {
          dependencies[PackageId.Parse(kvp.Key)] = p is null
              ? SemVersionRange.Parse(kvp.Value)
              : SemVersionRange.ParseNpm(kvp.Value);
        }
        catch (Exception ex) {
          // Ignore invalid dependencies
          RunTrace.Write("parse", $"dependency '{kvp.Key}': '{kvp.Value}' cannot be read as "
              + $"format 3, ignored: {ex.Message}");
        }
      }

      List<PackageManifestV2Place>? places = p?.Files?.Place ?? manifestV2.Files?.Place;
      List<PackageManifestAsset> assets = [];
      string? assetUrl = p?.AssetUrl ?? manifestV2.AssetUrl;
      if (assetUrl is not null) {
        string url = RenderTemplate(assetUrl);
        assets.Add(new PackageManifestAsset {
          Type = url.Split('.').Last() switch {
            "tar" => PackageManifestAsset.AssetType.Tar,
            "tgz" => PackageManifestAsset.AssetType.Tgz,
            "gz" => PackageManifestAsset.AssetType.Tgz,
            "zip" => PackageManifestAsset.AssetType.Zip,
            _ => PackageManifestAsset.AssetType.Uncompressed
          },
          Urls = [new Flurl.Url(url)],
          Placements = Placements(places ?? [])
        });
      } else if (p is null && places is not null) {
        assets.Add(new PackageManifestAsset {
          Type = PackageManifestAsset.AssetType.Self,
          Placements = Placements(places)
        });
      }

      PackageManifestV2Commands? cmds = p?.Commands ?? manifestV2.Commands;
      manifest.Variants.Add(new PackageManifestVariant {
        Platform = p is null ? "" : PlatformOf(p),
        Dependencies = dependencies,
        Assets = assets,
        PreserveFiles = (p?.Files?.Preserve ?? manifestV2.Files?.Preserve)?.Select(s => Glob.Parse(RenderTemplate(s))).ToList() ?? [],
        RemoveFiles = (p?.Files?.Remove ?? manifestV2.Files?.Remove)?.Select(s => Glob.Parse(RenderTemplate(s))).ToList() ?? [],
        Scripts = new PackageManifestScripts {
          PreInstall = cmds?.PreInstall?.Select(RenderTemplate).ToList() ?? [],
          PostInstall = cmds?.PostInstall?.Select(RenderTemplate).ToList() ?? [],
          PreUninstall = cmds?.PreUninstall?.Select(RenderTemplate).ToList() ?? [],
          PostUninstall = cmds?.PostUninstall?.Select(RenderTemplate).ToList() ?? []
        }
      });
    }

    // lip v0.37.0 added an empty variant for its own runtime when a platform is a pattern, so that
    // label "" has a variant for every runtime: an empty variant without a platform, merged for
    // the runtime being read, does the same.
    if (manifest.Variants.Any(v => v.Platform.Contains('*'))) {
      manifest.Variants.Insert(0, new PackageManifestVariant());
    }

    return manifest;
  }

  private static string PlatformOf(PackageManifestV2Platform p) {
    string prefix = p.GOOS switch {
      "windows" => "win",
      "darwin" => "osx",
      "linux" => "linux",
      "ios" => "ios",
      "android" => "android",
      _ => throw new FormatException(
          $"In tooth.json, a platform has the GOOS '{p.GOOS}', which lip does not know; it must "
          + "be one of windows, darwin, linux, ios and android.")
    };

    string suffix = p.GOARCH switch {
      "amd64" => "x64",
      "386" => "x86",
      "arm64" => "arm64",
      "arm" => "arm",
      "loong64" => "loongarch64",
      null => "*",
      _ => throw new FormatException(
          $"In tooth.json, a platform has the GOARCH '{p.GOARCH}', which lip does not know; it "
          + "must be one of amd64, 386, arm64, arm and loong64, or absent.")
    };

    return $"{prefix}-{suffix}";
  }

  private static PackageManifest ParseV3(JsonDocument jsonDocument) {
    Dictionary<string, object> model = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonDocument) ?? [];

    JsonNode? node = JsonNode.Parse(jsonDocument.RootElement.GetRawText());

    ProcessNode(node);

    try {
      return node.Deserialize<PackageManifest>()!;
    }
    catch (JsonException ex) {
      throw FieldError(ex);
    }

    void ProcessNode(JsonNode? current) {
      switch (current) {
        case null:
          return;

        case JsonValue value when value.TryGetValue(out string? s): {
            string newValue = Render(s, s, model);

            value.ReplaceWith(newValue);

            return;
          }

        case JsonObject obj: {
            foreach (KeyValuePair<string, JsonNode?> kvp in obj.ToList()) {
              ProcessNode(kvp.Value);
            }
            return;
          }

        case JsonArray arr: {
            for (int i = 0; i < arr.Count; i++) {
              ProcessNode(arr[i]);
            }
            return;
          }
      }
    }
  }

  /// Renders a Scriban template made from the value of tooth.json written. Throws
  /// InvalidOperationException naming the value when it is not a valid template.
  private static string Render(string template, string written, object model) {
    try {
      return Scriban.Template.Parse(template).Render(model);
    }
    catch (Exception ex) when (ex is InvalidOperationException
        or Scriban.Syntax.ScriptRuntimeException) {
      throw new InvalidOperationException(
          $"In tooth.json, the value '{written}' is not a valid template (text with "
          + "variables such as {{version}}).", ex);
    }
  }

  [GeneratedRegex(@"\$\(([^)]+?)\)")]
  private static partial Regex TemplateVariableRegex();
}
