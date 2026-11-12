using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flurl.Http;
using Lip.Core.Infrastructure;
using Lip.Core.Sources;
using Semver;

namespace Lip.Core.Mcmod;

/// The packages of github.com/LiteLDev/bds, the server of Minecraft, which lip defines itself
/// from the builds list instead of reading them from the repository (protocol/lip-package.md,
/// "The server").
public sealed partial class BedrockServer {
  /// The source whose packages are those of the server.
  public const string Repository = "github.com/LiteLDev/bds";

  /// Where the builds list is.
  public const string BuildsUrl = "https://levimc.org/api/v4/mc/versions/";

  /// The platforms the server has builds for, with the directory of their downloads.
  private static readonly (string Platform, string Directory)[] _platforms =
      [("win-x64", "bin-win"), ("linux-x64", "bin-linux")];

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

  /// The builds of each platform, in the order of the builds list.
  private readonly Dictionary<string, List<string>> _builds = [];

  private BedrockServer() {
  }

  /// The versions of the server, from highest to lowest: major.minor.patch of every build, and
  /// major.minor.patch-patch.N of every build major.minor.patch.N.
  public IReadOnlyList<SemVersion> Versions { get; private set; } = [];

  /// The versions that are not of one build only, from highest to lowest.
  public IEnumerable<SemVersion> ReleaseVersions => Versions.Where(v => !v.IsPrerelease);

  /// Whether a remote repository is the source of the server.
  public static bool IsRepository(string repository) =>
      PackageNames.AreEqual(repository, Repository);

  /// An archive without files: the packages of the server are in no archive.
  public static ArchiveHandle Archive(SemVersion version) =>
      new($"{Repository}@{version}", new NoFiles());

  /// Downloads the builds list. Throws ArchiveUnavailableException when it cannot be obtained.
  public static async Task<string> Download() {
    RunTrace.Write("download", $"Downloading the builds of the server from {BuildsUrl}");
    try {
      return await BuildsUrl.WithTimeout(TimeSpan.FromSeconds(30)).GetStringAsync();
    }
    catch (FlurlHttpException ex) {
      throw new ArchiveUnavailableException(
          $"Could not download the list of the builds of the server from {BuildsUrl}, which"
          + $" {Repository} is installed from.", ex);
    }
  }

  /// Reads the builds list: a JSON object whose members "server/win-x64" and "server/linux-x64"
  /// are arrays of builds. Members and builds of another form are ignored. Throws
  /// ArchiveUnavailableException when it is not a JSON object.
  public static BedrockServer Parse(string json) {
    BedrockServer server = new();
    try {
      using JsonDocument document = JsonDocument.Parse(json);
      if (document.RootElement.ValueKind != JsonValueKind.Object) {
        throw new JsonException("The root is not an object.");
      }

      foreach ((string platform, _) in _platforms) {
        if (document.RootElement.TryGetProperty($"server/{platform}", out JsonElement builds)
            && builds.ValueKind == JsonValueKind.Array) {
          server._builds[platform] = [
            .. builds.EnumerateArray()
                .Where(b => b.ValueKind == JsonValueKind.String)
                .Select(b => b.GetString()!)
                .Where(b => BuildRegex().IsMatch(b)),
          ];
        }
      }
    }
    catch (JsonException ex) {
      throw new ArchiveUnavailableException(
          $"The list of the builds of the server at {BuildsUrl} is not a JSON object.", ex);
    }

    server.Versions = [
      .. server._builds.Values.SelectMany(builds => builds)
          .SelectMany(b => new[] { VersionOf(b), PatchVersionOf(b) }).Distinct()
          .OrderByDescending(v => v, SemVersion.PrecedenceComparer),
    ];
    return server;
  }

  /// The package file of a version of the server: an entry for each platform that has a build
  /// of the version, the first one in the builds list, or the one a version
  /// major.minor.patch-patch.N names.
  public JsonElement PackageFile(SemVersion version) {
    JsonArray entries = [];
    foreach ((string platform, string directory) in _platforms) {
      if (_builds.GetValueOrDefault(platform)?.FirstOrDefault(
              b => (version.IsPrerelease ? PatchVersionOf(b) : VersionOf(b)).Equals(version))
          is not string build) {
        continue;
      }

      bool windows = platform == "win-x64";
      JsonObject entry = new() {
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
      if (!windows) {
        // lip writes files without the mode they have in a ZIP archive.
        entry["hooks"] = new JsonObject {
          ["post_install"] = new JsonArray("chmod +x bedrock_server"),
        };
      }

      entries.Add(entry);
    }

    JsonObject file = new() {
      [PackageFormat.FormatKey] = PackageFormat.Format,
      ["id"] = Repository,
      ["version"] = version.ToString(),
      ["info"] = new JsonObject {
        ["name"] = "Minecraft Bedrock Dedicated Server",
        ["description"] = "The server of Minecraft: Bedrock Edition.",
      },
      ["variants"] = new JsonObject { [""] = entries },
    };
    using JsonDocument json = JsonDocument.Parse(file.ToJsonString());
    return json.RootElement.Clone();
  }

  /// The version of a build: its first three numbers.
  private static SemVersion VersionOf(string build) {
    Match match = BuildRegex().Match(build);
    return new SemVersion(
        BigInteger.Parse(match.Groups[1].Value),
        BigInteger.Parse(match.Groups[2].Value),
        BigInteger.Parse(match.Groups[3].Value));
  }

  /// The version that names a build alone: major.minor.patch-patch.N for major.minor.patch.N.
  private static SemVersion PatchVersionOf(string build) => VersionOf(build).WithPrerelease(
      "patch", BigInteger.Parse(BuildRegex().Match(build).Groups[4].Value).ToString());

  [GeneratedRegex(@"^([0-9]+)\.([0-9]+)\.([0-9]+)\.([0-9]+)\z")]
  private static partial Regex BuildRegex();

  private sealed class NoFiles : ISource {
    public IEnumerable<string> Keys => [];

    public Task<Stream> OpenRead(string key) =>
        throw new FileNotFoundException($"The packages of {Repository} have no file '{key}'.");
  }
}
