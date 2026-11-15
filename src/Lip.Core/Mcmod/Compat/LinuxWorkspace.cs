using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Json;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Mcmod.Compat;

/// The state files of earlier versions of lip that record the platform linux-x64
/// (protocol/lip-compatibility.md, "Workspaces of linux-x64"). lip installs packages for win-x64
/// only, so such a state file is read as one of win-x64 in which the server
/// (github.com/LiteLDev/bds) is its variant "linux". Any other package cannot be read so.
public static class LinuxWorkspace {
  /// The platform of the state files read here.
  public const string Platform = "linux-x64";

  /// The state file of win-x64 that a state file of linux-x64 is read as. Throws
  /// InvalidOperationException, naming the package, when it records a package other than the
  /// server, or a server without an entry of linux-x64.
  public static LockFile Read(LockFile lockFile, string path) {
    return lockFile with {
      Platform = McmodService.Platform,
      Packages = [.. lockFile.Packages.Select(p => Server(p) ?? throw Unsupported(p, path))],
    };
  }

  /// The package of the variant "linux" of the server that a package of the state file is read
  /// as, or null when it is not the server of the default variant with an entry of linux-x64.
  private static LockedPackage? Server(LockedPackage package) {
    if (!Ref.TryParse(package.Source, out Ref? source, out _)
        || !BedrockServer.IsServer(source)
        || source.Variant != ""
        || JsonSerializer.SerializeToNode(package.Manifest, LipJsonContext.Default.JsonElement) is not JsonObject manifest
        || manifest["variants"]?[""] is not JsonArray entries
        || entries.OfType<JsonObject>().FirstOrDefault(e =>
            e["side"]?.GetValueKind() == JsonValueKind.String
            && e["side"]!.GetValue<string>() == "server"
            && e["platform"]?.GetValueKind() == JsonValueKind.String
            && e["platform"]!.GetValue<string>() == Platform) is not JsonObject linux) {
      return null;
    }

    JsonObject entry = (JsonObject)linux.DeepClone();
    entry["platform"] = McmodService.Platform;
    entry.Remove("hooks");
    manifest["variants"] = new JsonObject {
      [BedrockServer.LinuxVariant] = new JsonArray(entry),
    };
    return package with {
      Variant = BedrockServer.LinuxVariant,
      Source = source.WithVariant(BedrockServer.LinuxVariant).ToString(),
      Manifest = JsonSerializer.SerializeToElement(manifest, LipJsonContext.Default.JsonNode),
    };
  }

  private static InvalidOperationException Unsupported(LockedPackage package, string path) =>
      new($"The state file '{path}' records the platform {Platform}, for which an earlier"
          + $" version of lip installed '{package.Identity}'. lip installs packages for"
          + $" {McmodService.Platform} only, and reads only the server"
          + $" {BedrockServer.Repository} of such a workspace, as"
          + $" {BedrockServer.Repository}#{BedrockServer.LinuxVariant}. Install the packages"
          + $" again in a new workspace, for {McmodService.Platform}.");
}
