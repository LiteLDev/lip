using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lip.Core.Config;

/// allowlist.json and permissions.json of the server (protocol/lip-tui.md, "Configuration"):
/// arrays of objects, read as [] when the file does not exist.
public static class PlayerLists {
  public const string AllowlistFileName = "allowlist.json";
  public const string PermissionsFileName = "permissions.json";

  /// The permissions a player of permissions.json may have.
  public static readonly IReadOnlyList<string> Permissions = ["operator", "member", "visitor"];

  /// Reads one of the lists; null text is a file that does not exist. Throws JsonException when
  /// it is not an array.
  public static JsonArray Parse(string? text) {
    if (text is null) {
      return [];
    }

    return JsonConfigFile.Parse(text) as JsonArray
        ?? throw new JsonException("The file does not hold an array.");
  }

  /// Adds a player to the allowlist, as the server writes one.
  public static void Allow(JsonArray allowlist, string name, string xuid) {
    JsonObject player = new() { ["ignoresPlayerLimit"] = false, ["name"] = name };
    if (xuid.Length > 0) {
      player["xuid"] = xuid;
    }

    allowlist.Add(player);
  }

  /// Gives a player a permission: changes the entry with the xuid, or adds one.
  public static void SetPermission(JsonArray permissions, string xuid, string permission) {
    if (!Permissions.Contains(permission)) {
      throw new ArgumentException($"There is no permission {permission}.", nameof(permission));
    }

    foreach (JsonNode? entry in permissions) {
      if (entry is JsonObject player && Text(player, "xuid") == xuid) {
        player["permission"] = permission;
        return;
      }
    }

    permissions.Add(new JsonObject { ["permission"] = permission, ["xuid"] = xuid });
  }

  /// The string member of an entry, "" when it has none.
  public static string Text(JsonNode? entry, string member) =>
      entry is JsonObject o && o[member] is JsonValue v && v.GetValueKind() == JsonValueKind.String
          ? v.GetValue<string>()
          : "";
}
