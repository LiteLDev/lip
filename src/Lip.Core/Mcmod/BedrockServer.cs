using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// The packages of github.com/LiteLDev/bds, the server of Minecraft, which lip defines itself
/// instead of reading them from the repository (protocol/lip-package.md, "The server").
public static partial class BedrockServer {
  /// The remote repository whose version tags are the versions of the server.
  public const string Repository = "github.com/LiteLDev/bds";

  /// The file of the repository that lists the builds of the server, one per line, and the
  /// branch it is read from.
  public const string BuildsFileName = "versions.txt";
  public const string BuildsBranch = "main";

  private static readonly string[] _preserve =
      ["server.properties", "allowlist.json", "permissions.json"];

  private static readonly string[] _purge = [
    "bedrock_server.exe.bak",
    "packet-statistics.txt",
    "valid_known_packs.json",
    "behavior_packs/",
    "config/",
    "definitions/",
    "development_behavior_packs/",
    "development_resource_packs/",
    "development_skin_packs/",
    "resource_packs/",
    "world_templates/",
  ];

  /// Whether a remote repository is that of the server.
  public static bool IsRepository(string repository) =>
      PackageNames.AreEqual(repository, Repository);

  /// The build of a version of the server: the last line of the builds file that is four
  /// numbers separated by '.', the first three of them the major, minor and patch numbers of the
  /// version; null when the file has no such line.
  public static string? Build(string builds, SemVersion version) {
    string? found = null;
    foreach (string line in builds.Split('\n')) {
      Match match = BuildRegex().Match(line.Trim());
      if (match.Success
          && IsNumber(match.Groups[1].Value, version.Major)
          && IsNumber(match.Groups[2].Value, version.Minor)
          && IsNumber(match.Groups[3].Value, version.Patch)) {
        found = match.Value;
      }
    }

    return found;
  }

  /// The package file of a version of the server, of a build.
  public static JsonElement PackageFile(SemVersion version, string build) {
    JsonObject Entry(string platform, string directory, bool windows) => new() {
      ["side"] = "server",
      ["platform"] = platform,
      ["assets"] = new JsonArray(new JsonObject {
        ["type"] = "zip",
        ["urls"] = new JsonArray(
            $"https://www.minecraft.net/bedrockdedicatedserver/{directory}/bedrock-server-"
            + $"{build}.zip"),
        ["place"] = new JsonArray(new JsonObject { ["src"] = "", ["dest"] = "" }),
      }),
      ["preserve"] = new JsonArray([.. _preserve.Select(glob => (JsonNode)glob)]),
      ["purge"] = new JsonArray([
        .. _purge.Skip(windows ? 0 : 1).Select(glob => (JsonNode)glob),
      ]),
    };

    JsonObject linux = Entry("linux-x64", "bin-linux", windows: false);
    // lip writes files without the mode they have in a ZIP archive.
    linux["hooks"] = new JsonObject {
      ["post_install"] = new JsonArray("chmod +x bedrock_server"),
    };

    JsonObject file = new() {
      [PackageFormat.FormatKey] = PackageFormat.Format,
      ["id"] = Repository,
      ["version"] = version.ToString(),
      ["info"] = new JsonObject {
        ["name"] = "Minecraft Bedrock Dedicated Server",
        ["description"] = $"The server of Minecraft: Bedrock Edition, build {build}.",
      },
      ["variants"] = new JsonObject {
        [""] = new JsonArray(Entry("win-x64", "bin-win", windows: true), linux),
      },
    };
    using JsonDocument json = JsonDocument.Parse(file.ToJsonString());
    return json.RootElement.Clone();
  }

  private static bool IsNumber(string digits, System.Numerics.BigInteger number) =>
      System.Numerics.BigInteger.Parse(digits) == number;

  [GeneratedRegex(@"^([0-9]+)\.([0-9]+)\.([0-9]+)\.[0-9]+\z")]
  private static partial Regex BuildRegex();
}
