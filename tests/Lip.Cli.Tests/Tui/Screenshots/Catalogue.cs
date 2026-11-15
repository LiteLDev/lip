using System.Text.Json.Nodes;
using Lip.Core.Catalog;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// A mod of the catalogue of the screenshots.
public sealed record CatalogueMod(
    string Id, string Description, string[] Tags, int Stars, int Downloads, string License,
    string[] Versions, string Updated, string[]? Variants = null, int RequiredBy = 0,
    string[]? Depends = null) {
  public string Name => Id[(Id.LastIndexOf('/') + 1)..];
  public string Owner => Id.Split('/')[1];
}

/// The catalogue of the screenshots: mods as levimc.org lists them, with made-up numbers.
public static class Catalogue {
  public const string ScriptEngine = "github.com/LiteLDev/LegacyScriptEngine";
  public const string RemoteCall = "github.com/LiteLDev/LegacyRemoteCall";
  public const string Money = "github.com/LiteLDev/LegacyMoney";
  public const string Gmlib = "github.com/GroupMountain/GMLIB";
  public const string Optimize = "github.com/LiteLDev/LeviOptimize";
  public const string AntiCheat = "github.com/LiteLDev/LeviAntiCheat";
  public const string Land = "github.com/engsr6982/PLand";
  public const string Dimensions = "github.com/LiteLDev/MoreDimensions";

  public static readonly IReadOnlyList<CatalogueMod> Mods = [
    new(ScriptEngine, "A plugin engine for running LLSE plugins on LeviLamina",
        ["type:mod", "engine", "script"], 412, 186240, "LGPL-3.0",
        ["0.13.2", "0.13.1", "0.13.0", "0.12.4"], "2026-11-08",
        Variants: ["lua", "quickjs", "nodejs", "python"], RequiredBy: 37,
        Depends: [RemoteCall]),
    new(Optimize, "A performance optimization mod for LeviLamina",
        ["type:mod", "optimization"], 286, 142877, "LGPL-3.0",
        ["1.8.1", "1.8.0", "1.7.3"], "2026-11-02"),
    new(Gmlib, "A general mod library for LeviLamina", ["type:mod", "library"], 231, 97315,
        "LGPL-3.0", ["1.6.0", "1.5.2", "1.5.1"], "2026-10-27", RequiredBy: 58),
    new(Money, "An economy core with a simple API", ["type:mod", "economy"], 164, 81422,
        "LGPL-3.0", ["0.16.1", "0.16.0", "0.15.0"], "2026-10-19", RequiredBy: 24,
        Depends: [RemoteCall]),
    new(Land, "功能强大的领地系统", ["type:mod", "land", "protection"], 152, 63908, "AGPL-3.0",
        ["0.19.0", "0.18.2"], "2026-11-05", Depends: [Money]),
    new(AntiCheat, "An anti-cheat for Bedrock Dedicated Server", ["type:mod", "anti-cheat"], 118,
        44170, "GPL-3.0", ["1.3.0", "1.2.1"], "2026-09-30"),
    new(Dimensions, "Adds custom dimensions to the game", ["type:mod", "library", "world"], 97,
        29654, "LGPL-3.0", ["0.11.0", "0.10.3"], "2026-10-11", RequiredBy: 6),
    new(RemoteCall, "Lets mods call the functions that other mods export",
        ["type:mod", "library"], 61, 171206, "LGPL-3.0", ["0.13.0", "0.12.0"], "2026-10-02",
        RequiredBy: 45),
  ];

  /// The text of the file of the catalogue at a URL.
  public static string File(string url) {
    if (url == ModCatalogClient.BaseUrl + "index.json") {
      return new JsonObject { ["mods"] = new JsonArray([.. Mods.Select(Entry)]) }
          .ToJsonString();
    }

    CatalogueMod? mod = Mods.FirstOrDefault(m => url == ModCatalogClient.DetailUrl(m.Id));
    return mod is null ? LoaderDetail() : Detail(mod);
  }

  private static JsonObject Deps(CatalogueMod mod) {
    JsonObject deps = new() { [World.Loader] = "26.60.*" };
    foreach (string id in mod.Depends ?? []) {
      deps[id] = ">=" + Mods.First(m => m.Id == id).Versions[^1];
    }

    return deps;
  }

  private static JsonNode Entry(CatalogueMod mod) =>
      new JsonObject {
        ["id"] = mod.Id,
        ["name"] = mod.Name,
        ["owner"] = mod.Owner,
        ["description"] = mod.Description,
        ["tags"] = new JsonArray([.. mod.Tags.Select(tag => JsonValue.Create(tag))]),
        ["side"] = new JsonArray("server"),
        ["platform"] = new JsonArray("win-x64"),
        ["gameVersions"] = new JsonArray(World.GameVersion),
        ["deps"] = Deps(mod),
        ["stars"] = mod.Stars,
        ["license"] = mod.License,
        ["downloads"] = mod.Downloads,
        ["updated"] = mod.Updated + "T00:00:00Z",
        ["created"] = "2024-03-01T00:00:00Z",
        ["url"] = $"https://levimc.org/mods/{mod.Id}/",
        ["requiredBy"] = mod.RequiredBy,
      };

  /// The file of a mod: each version but the oldest is for the LeviLamina of the
  /// screenshots, and the oldest for the one before it.
  private static string Detail(CatalogueMod mod) {
    JsonArray versions = [];
    for (int i = 0; i < mod.Versions.Length; i++) {
      JsonObject deps = Deps(mod);
      if (i == mod.Versions.Length - 1) {
        deps[World.Loader] = "26.50.*";
      }

      JsonObject variants = [];
      foreach (string variant in mod.Variants ?? [""]) {
        variants[variant] = new JsonArray(new JsonObject {
          ["side"] = "server",
          ["platform"] = "win-x64",
          ["deps"] = deps.DeepClone(),
        });
      }

      versions.Add(new JsonObject {
        ["version"] = mod.Versions[i],
        ["prerelease"] = false,
        ["released"] = $"2026-{11 - i:00}-0{8 - i}T00:00:00Z",
        ["variants"] = variants,
      });
    }

    return new JsonObject {
      ["id"] = mod.Id,
      ["license"] = mod.License,
      ["latest"] = new JsonObject {
        ["variant"] = (mod.Variants ?? [""])[0],
        ["version"] = mod.Versions[0],
      },
      ["versions"] = versions,
      ["readmes"] = new JsonArray(new JsonObject {
        ["lang"] = "en",
        ["text"] = $"# {mod.Name}\n\n{mod.Description}.\n\n## Install\n\n"
            + $"```\nlip install {mod.Id}\n```\n",
      }),
    }.ToJsonString();
  }

  /// The file of LeviLamina, which tells the new server wizard which games have one.
  private static string LoaderDetail() {
    JsonArray versions = [];
    foreach ((string loader, string game) in new[] {
      ("26.60.3", "1.26.60"), ("26.50.2", "1.26.50"), ("26.40.1", "1.26.40"),
      ("26.20.4", "1.26.20"), ("26.10.2", "1.26.10"),
    }) {
      versions.Add(new JsonObject {
        ["version"] = loader,
        ["variants"] = new JsonObject {
          [""] = new JsonArray(new JsonObject {
            ["side"] = "server",
            ["platform"] = "win-x64",
            ["deps"] = new JsonObject { [World.Game] = game },
          }),
        },
      });
    }

    return new JsonObject { ["id"] = World.Loader, ["versions"] = versions }.ToJsonString();
  }
}
