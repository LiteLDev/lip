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

/// The environment (platform, rid) of a workspace (spec §5.1).
///
/// lip manages server workspaces only: its operations always use McmodPlatform.Server. Client
/// workspaces are left to launchers implementing the same spec (LeviLauncher). The client
/// platform is still part of the model, since documents select artifacts by platform.
public record LipEnvironment(McmodPlatform Platform, string Rid);
