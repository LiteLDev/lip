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
}

/// The environment (platform, runtime) of a workspace (protocol/package.md §1). Runtime is a .NET RID.
///
/// lip manages server workspaces only: its operations always use McmodPlatform.Server. Client
/// workspaces are left to launchers implementing the same spec (LeviLauncher). The client
/// platform is still part of the model, since documents select targets by platform.
public record LipEnvironment(McmodPlatform Platform, string Runtime);
