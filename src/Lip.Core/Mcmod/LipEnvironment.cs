namespace Lip.Core.Mcmod;

public enum McmodPlatform {
  Server,
  Client,
}

public static class McmodPlatformExtensions {
  public static string ToSpecString(this McmodPlatform platform) => platform switch {
    McmodPlatform.Server => "server",
    McmodPlatform.Client => "client",
    _ => throw new ArgumentOutOfRangeException(nameof(platform)),
  };

  public static bool TryParse(string s, out McmodPlatform platform) {
    switch (s) {
      case "server":
        platform = McmodPlatform.Server;
        return true;
      case "client":
        platform = McmodPlatform.Client;
        return true;
      default:
        platform = default;
        return false;
    }
  }

  /// The mod directory under the workspace root (spec §7).
  public static string ModsDirectory(this McmodPlatform platform) => platform switch {
    McmodPlatform.Server => "plugins",
    McmodPlatform.Client => "mods",
    _ => throw new ArgumentOutOfRangeException(nameof(platform)),
  };
}

/// Thrown when an operation needs the environment platform but it could not be determined.
public class PlatformUnknownException() : InvalidOperationException(
    "Cannot determine the platform: none of bedrock_server.exe, bedrock_server_mod.exe and "
    + "Minecraft.Windows.exe exists in the workspace. Specify it explicitly with "
    + "--platform server|client.");

/// The environment (platform, rid) of a workspace (spec §5.1).
///
/// Platform is null when it could not be determined. That is only an error once something needs
/// it (see RequirePlatform); tooth.json packages without a client label never do.
public record LipEnvironment(McmodPlatform? Platform, string Rid) {
  public McmodPlatform RequirePlatform() => Platform ?? throw new PlatformUnknownException();
}
