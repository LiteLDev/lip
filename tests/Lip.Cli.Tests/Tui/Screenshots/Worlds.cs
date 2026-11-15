using Lip.Cli.Tui.I18n;

namespace Lip.Cli.Tests.Tui.Screenshots;

/// The worlds that most scenarios start from.
public static class Worlds {
  public static string Creative { get; } = World.PathOf(@"D:\servers\creative");
  public static string Skyblock { get; } = World.PathOf(@"D:\servers\skyblock");

  /// A folder without a server: what lip finds the first time it is started.
  public static World Empty(
      Language language, int width = World.DefaultWidth, int height = World.DefaultHeight) =>
      new(language, width, height);

  /// A server with LeviLamina and no mods yet.
  public static World Server(
      Language language, int width = World.DefaultWidth, int height = World.DefaultHeight) =>
      new World(language, width, height).InstallServer()
          .File("server.properties", Properties);

  /// A server that has been played on: mods with their files, the files of the game, and
  /// two more workspaces in the workspace list, one of them without a server. With
  /// configurable set, the Mod config page lists the mods (Installed.Mod); without it, lip
  /// can plan to uninstall them.
  public static World Survival(
      Language language, int width = World.DefaultWidth, int height = World.DefaultHeight,
      bool configurable = false) {
    World world = new World(language, width, height).InstallServer(
        new Installed(
            Catalogue.ScriptEngine, "0.13.1", Variant: "lua", Mod: configurable,
            Files: ["mods/legacy-script-engine-lua/manifest.json"]),
        new Installed(
            Catalogue.RemoteCall, "0.13.0", Explicit: false, Mod: configurable,
            Files: ["mods/LegacyRemoteCall/manifest.json"]),
        new Installed(
            Catalogue.Gmlib, "1.6.0", Mod: configurable, Files: ["mods/GMLIB/manifest.json"]),
        new Installed(
            Catalogue.Optimize, "1.8.1", Mod: configurable,
            Files: ["mods/LeviOptimize/manifest.json"]),
        new Installed(
            Catalogue.Money, "0.16.1", Mod: configurable,
            Files: ["mods/LegacyMoney/manifest.json"]));
    world.File("server.properties", Properties)
        .File("allowlist.json", Allowlist)
        .File("permissions.json", Permissions)
        .File("mods/LeviOptimize/config/config.json", OptimizeConfig)
        .File("mods/LegacyMoney/money.json", MoneyConfig)
        .File("mods/GMLIB/config/config.json", GmlibConfig);
    world.InstallIn(
        Creative, new Installed(World.Game, "1.26.50"), new Installed(World.Loader, "26.50.2"));
    return world.List(Creative, Skyblock);
  }

  /// What the server writes while it starts, as a player reads it in the Server page.
  public static readonly string[] ServerOutput = [
    "NO LOG FILE! - setting up server logging...",
    "[2026-11-14 20:15:02:114 INFO] Starting Server",
    "[2026-11-14 20:15:02:115 INFO] Version: 1.26.60.2",
    "[2026-11-14 20:15:02:115 INFO] Session ID: 6f1c1c1e-0d0f-4a38-9a51-2f2b7c1d9e44",
    "[2026-11-14 20:15:02:116 INFO] Level Name: Bedrock level",
    "[2026-11-14 20:15:02:118 INFO] Game mode: 0 Survival",
    "[2026-11-14 20:15:02:118 INFO] Difficulty: 2 NORMAL",
    Loader("20:15:02.412", "LeviLamina 26.60.3 is loading mods..."),
    Loader("20:15:02.538", "Loaded LegacyRemoteCall v0.13.0"),
    Loader("20:15:02.611", "Loaded legacy-script-engine-lua v0.13.1"),
    Loader("20:15:02.684", "Loaded GMLIB v1.6.0"),
    Loader("20:15:02.731", "Loaded LeviOptimize v1.8.1"),
    Loader("20:15:02.790", "Loaded LegacyMoney v0.16.1"),
    Loader("20:15:02.791", "5 mod(s) loaded"),
    "[2026-11-14 20:15:03:902 INFO] opening worlds/Bedrock level/db",
    "[2026-11-14 20:15:05:377 INFO] IPv4 supported, port: 19132: Used for gameplay and LAN",
    "[2026-11-14 20:15:05:378 INFO] IPv6 supported, port: 19133: Used for gameplay",
    "[2026-11-14 20:15:05:641 INFO] Server started.",
    "[2026-11-14 20:16:12:208 INFO] Player connected: Steve, xuid: 2535416409681153",
    "[2026-11-14 20:16:15:530 INFO] Player Spawned: Steve xuid: 2535416409681153",
  ];

  /// A line that LeviLamina writes, with its colors.
  private static string Loader(string time, string message) =>
      $"\x1b[90m{time}\x1b[0m \x1b[36mINFO\x1b[0m \x1b[1m[LeviLamina]\x1b[0m {message}";

  private const string Properties = """
      server-name=Survival
      # Used as the server name
      # Allowed values: Any string without semicolon symbol.

      gamemode=survival
      # Sets the game mode for new players.
      # Allowed values: "survival", "creative", or "adventure"

      difficulty=normal
      # Sets the difficulty of the world.
      # Allowed values: "peaceful", "easy", "normal", or "hard"

      allow-cheats=false
      # If true then cheats like commands can be used.
      # Allowed values: "true" or "false"

      max-players=10
      # The maximum number of players that can play on the server.
      # Allowed values: Any positive integer

      online-mode=true
      # If true then all connected players must be authenticated to Xbox Live.
      # Allowed values: "true" or "false"

      allow-list=true
      # If true then all connected players must be listed in the separate allowlist.json file.
      # Allowed values: "true" or "false"

      server-port=19132
      # Which IPv4 port the server should listen to.
      # Allowed values: Integers in the range [1, 65535]

      server-portv6=19133
      # Which IPv6 port the server should listen to.
      # Allowed values: Integers in the range [1, 65535]

      view-distance=32
      # The maximum allowed view distance in number of chunks.
      # Allowed values: Positive integer equal to 5 or greater.

      tick-distance=4
      # The world will be ticked this many chunks away from any player.
      # Allowed values: Integers in the range [4, 12]

      player-idle-timeout=30
      # After a player has idled for this many minutes they will be kicked.
      # Allowed values: Any non-negative integer.

      level-name=Bedrock level
      # Allowed values: Any string without semicolon symbol or symbols illegal for file name

      level-seed=
      # Use to randomize the world
      # Allowed values: Any string

      default-player-permission-level=member
      # Permission level for new players joining for the first time.
      # Allowed values: "visitor", "member", "operator"

      texturepack-required=false
      # Force clients to use texture packs in the current world
      # Allowed values: "true" or "false"

      """;

  private const string Allowlist = """
      [
        { "ignoresPlayerLimit": true, "name": "Steve", "xuid": "2535416409681153" },
        { "ignoresPlayerLimit": false, "name": "Alex", "xuid": "2535428104317920" },
        { "ignoresPlayerLimit": false, "name": "Noor", "xuid": "2535467732905414" },
        { "ignoresPlayerLimit": false, "name": "Kai" }
      ]
      """;

  private const string Permissions = """
      [
        { "permission": "operator", "xuid": "2535416409681153" },
        { "permission": "member", "xuid": "2535428104317920" }
      ]
      """;

  private const string OptimizeConfig = """
      {
          "version": 4,
          "features": {
              "optPushEntity": { "enabled": true, "maxPushTimes": 3, "unlimitedPlayerPush": false },
              "optSeenPercent": true,
              "fixHopperItem": true,
              "fixMovingBlock": true,
              "fixChunkLeak": true,
              "disableAutoCompactionLog": true
          },
          "commands": {
              "timing": { "enabled": true, "permission": "GameDirectors" }
          },
          "ignoredDimensions": ["the_end"]
      }
      """;

  private const string MoneyConfig = """
      {
          "language": "en",
          "def_money": 100,
          "pay_tax": 0.05,
          "currency_symbol": "$",
          "enable_ranking": true
      }
      """;

  private const string GmlibConfig = """
      {
          "version": 2,
          "language": "en_US",
          "placeholderApi": true
      }
      """;
}
