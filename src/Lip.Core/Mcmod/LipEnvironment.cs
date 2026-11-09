namespace Lip.Core.Mcmod;

public enum McmodSide {
  Server,
  Client,
}

public static class McmodSideExtensions {
  public static string ToSpecString(this McmodSide side) => side switch {
    McmodSide.Server => "server",
    McmodSide.Client => "client",
    _ => throw new ArgumentOutOfRangeException(
        nameof(side), side, "The side must be server or client."),
  };

  public static bool TryParse(string s, out McmodSide side) {
    switch (s) {
      case "server":
        side = McmodSide.Server;
        return true;
      case "client":
        side = McmodSide.Client;
        return true;
      default:
        side = default;
        return false;
    }
  }
}

/// The environment (side, platform) of a workspace (protocol/package.md §1). Platform is a
/// .NET RID.
///
/// lip manages server workspaces only: its operations always use McmodSide.Server. Client
/// workspaces are left to launchers implementing the same spec (LeviLauncher). The client
/// side is still part of the model, since documents select targets by side.
public record LipEnvironment(McmodSide Side, string Platform);
