using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNet.Globbing;
using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Migration.PackageManifests;

public static partial class PackageManifestMigration {
  public static PackageManifest Migrate(JsonDocument jsonDocument) {
    int formatVersion = jsonDocument.RootElement.GetProperty("format_version").GetInt32();

    return formatVersion switch {
      1 => MigrateV2ToV3(MigrateV1ToV2(JsonSerializer.Deserialize<PackageManifestV1>(jsonDocument)!)),
      2 => MigrateV2ToV3(JsonSerializer.Deserialize<PackageManifestV2>(jsonDocument)!),
      3 => ParseV3(jsonDocument),
      _ => throw new NotSupportedException(),
    };
  }

  private static PackageManifestV2 MigrateV1ToV2(PackageManifestV1 manifestV1) {
    Dictionary<string, JsonElement>? data = manifestV1.Information?.Data;
    PackageManifestV2Info info = new() {
      Name = data is not null && data.TryGetValue("name", out JsonElement n)
            ? n.GetString() ?? ""
            : "",
      Description = data is not null && data.TryGetValue("description", out JsonElement d)
            ? d.GetString() ?? ""
            : "",
      Author = data is not null && data.TryGetValue("author", out JsonElement a)
            ? a.GetString() ?? ""
            : "",
      Tags = data is not null && data.TryGetValue("tags", out JsonElement t) && t.ValueKind == JsonValueKind.Array
            ? [.. t.EnumerateArray().Select(e => e.GetString() ?? "")]
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
      foreach (PackageManifestV1Command cmd in manifestV1.Commands) {
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
      return Scriban.Template.Parse(converted).Render(model);
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

    PackageManifest manifest = new() {
      Path = manifestV2.Tooth,
      Version = SemVersion.Parse(manifestV2.Version, SemVersionStyles.Any),
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
        catch {
          // Ignore invalid dependencies
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
      _ => throw new PlatformNotSupportedException($"Unsupported GOOS: {p.GOOS}")
    };

    string suffix = p.GOARCH switch {
      "amd64" => "x64",
      "386" => "x86",
      "arm64" => "arm64",
      "arm" => "arm",
      "loong64" => "loongarch64",
      null => "*",
      _ => throw new PlatformNotSupportedException($"Unsupported GOARCH: {p.GOARCH}")
    };

    return $"{prefix}-{suffix}";
  }

  private static PackageManifest ParseV3(JsonDocument jsonDocument) {
    Dictionary<string, object> model = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonDocument) ?? [];

    JsonNode? node = JsonNode.Parse(jsonDocument.RootElement.GetRawText());

    ProcessNode(node);

    return node.Deserialize<PackageManifest>()!;

    void ProcessNode(JsonNode? current) {
      switch (current) {
        case null:
          return;

        case JsonValue value when value.TryGetValue(out string? s): {
            string newValue = Scriban.Template.Parse(s).Render(model);

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

  [GeneratedRegex(@"\$\(([^)]+?)\)")]
  private static partial Regex TemplateVariableRegex();
}
