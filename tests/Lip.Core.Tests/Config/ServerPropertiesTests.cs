using Lip.Core.Config;

namespace Lip.Core.Tests.Config;

/// server.properties (protocol/lip-tui.md, "Configuration"), with the file of BDS 1.26.63.1.
public class ServerPropertiesTests {
  // Kept with LF line breaks in the repository; the server ships it with CRLF.
  private static readonly string _lf = File.ReadAllText(
      Path.Join(AppContext.BaseDirectory, "Config", "server.properties")).Replace("\r\n", "\n");

  private static readonly string _crlf = _lf.Replace("\n", "\r\n");

  [Fact]
  public void Parse_TheFileOfTheServer_ReadsEachActiveProperty() {
    ServerProperties properties = ServerProperties.Parse(_crlf);

    Assert.Equal("Dedicated Server", properties.Find("server-name")!.Value);
    Assert.Equal("survival", properties.Find("gamemode")!.Value);
    Assert.Equal("", properties.Find("level-seed")!.Value);
    // Commented out, so not a property.
    Assert.Null(properties.Find("server-ip"));
    Assert.Null(properties.Find("# server-ip"));
    Assert.All(properties.Properties, p => Assert.DoesNotContain('#', p.Key));
    Assert.Equal(
        ["Sets the game mode for new players.",
          "Allowed values: \"survival\", \"creative\", or \"adventure\""],
        properties.Find("gamemode")!.Comment);
  }

  [Theory]
  [InlineData("allow-cheats", PropertyKind.Boolean)]
  [InlineData("gamemode", PropertyKind.Choice)]
  [InlineData("difficulty", PropertyKind.Choice)]
  [InlineData("default-player-permission-level", PropertyKind.Choice)]
  [InlineData("max-players", PropertyKind.Integer)]
  [InlineData("server-port", PropertyKind.Integer)]
  [InlineData("compression-threshold", PropertyKind.Integer)]
  [InlineData("server-name", PropertyKind.Text)]
  [InlineData("level-seed", PropertyKind.Text)]
  [InlineData("persistent-ticking-area-max-count", PropertyKind.Text)]
  [InlineData("server-build-radius-ratio", PropertyKind.Text)]
  [InlineData("player-movement-action-direction-threshold", PropertyKind.Text)]
  public void Type_ComesFromTheAllowedValues(string key, PropertyKind kind) {
    ServerProperties properties = ServerProperties.Parse(_lf);

    Assert.Equal(kind, properties.Find(key)!.Type.Kind);
  }

  [Fact]
  public void Type_OfAChoiceAndARange_HoldsThem() {
    ServerProperties properties = ServerProperties.Parse(_lf);

    Assert.Equal(["survival", "creative", "adventure"], properties.Find("gamemode")!.Type.Choices);
    PropertyType port = properties.Find("server-port")!.Type;
    Assert.Equal((1L, 65535L), (port.Min!.Value, port.Max!.Value));
    Assert.NotNull(port.Check("0"));
    Assert.NotNull(port.Check("x"));
    Assert.Null(port.Check("19132"));
    Assert.NotNull(properties.Find("gamemode")!.Type.Check("hardcore"));
    Assert.Equal(0L, properties.Find("player-idle-timeout")!.Type.Min);
  }

  /// The numbers that the properties of the file of the server take, as their "Allowed
  /// values:" lines say: a positive integer is not 0, "equal to 5 or greater" is not 4, a
  /// non-negative integer may be 0, and a range has both ends.
  [Theory]
  [InlineData("max-players", "1", true)]
  [InlineData("max-players", "30", true)]
  [InlineData("max-players", "0", false)]
  [InlineData("max-players", "-1", false)]
  [InlineData("max-players", "ten", false)]
  [InlineData("max-players", "1.5", false)]
  // "If set to 0 or removed then it will use as many as possible."
  [InlineData("max-threads", "1", true)]
  [InlineData("max-threads", "0", true)]
  [InlineData("max-threads", "-8", false)]
  [InlineData("view-distance", "5", true)]
  [InlineData("view-distance", "32", true)]
  [InlineData("view-distance", "4", false)]
  [InlineData("view-distance", "0", false)]
  [InlineData("view-distance", "-5", false)]
  [InlineData("player-idle-timeout", "0", true)]
  [InlineData("player-idle-timeout", "-1", false)]
  [InlineData("tick-distance", "4", true)]
  [InlineData("tick-distance", "12", true)]
  [InlineData("tick-distance", "3", false)]
  [InlineData("tick-distance", "13", false)]
  [InlineData("server-port", "65535", true)]
  [InlineData("server-port", "65536", false)]
  [InlineData("compression-threshold", "0", true)]
  [InlineData("compression-threshold", "-1", false)]
  public void Check_OfANumber_FollowsTheAllowedValues(string key, string value, bool allowed) {
    ServerProperties properties = ServerProperties.Parse(_lf);
    PropertyType type = properties.Find(key)!.Type;

    Assert.Equal(PropertyKind.Integer, type.Kind);
    Assert.Equal(allowed, type.Check(value) is null);
    if (allowed) {
      properties.Set(key, value);
    } else {
      Assert.Throws<ArgumentException>(() => properties.Set(key, value));
    }
  }

  [Theory]
  [InlineData("Any positive integer", 1L, null)]
  [InlineData("Any positive integer.", 1L, null)]
  [InlineData("Positive integer equal to 5 or greater.", 5L, null)]
  [InlineData("Any non-negative integer.", 0L, null)]
  [InlineData("Any positive integer, or 0 to disable limits", 0L, null)]
  [InlineData("An integer of at least 3", 3L, null)]
  [InlineData("A positive integer no greater than 2147483647", 1L, 2147483647L)]
  [InlineData("Any integer", null, null)]
  [InlineData("An integer no greater than 8", null, 8L)]
  // A bound that is no number of the type bounds nothing.
  [InlineData("An integer of at least 99999999999999999999", null, null)]
  [InlineData("Integers in the range [4, 12]", 4L, 12L)]
  [InlineData("0-65535", 0L, 65535L)]
  public void Type_OfAnInteger_HasTheBoundsThatTheAllowedValuesName(
      string allowed, long? min, long? max) {
    ServerProperties properties = ServerProperties.Parse($"x=1\n# Allowed values: {allowed}\n");
    PropertyType type = properties.Find("x")!.Type;

    Assert.Equal((PropertyKind.Integer, min, max), (type.Kind, type.Min, type.Max));
  }

  /// A positive integer may be 0 when another line of the comment says what 0 does.
  [Fact]
  public void Type_OfAPositiveInteger_WhoseCommentSaysWhatZeroDoes_MayBeZero() {
    ServerProperties properties = ServerProperties.Parse(
        "threads=8\n# If set to 0 or removed then it will use as many as possible.\n"
        + "# Allowed values: Any positive integer.\n"
        + "players=10\n# The maximum number of players.\n# Allowed values: Any positive integer\n");

    Assert.Equal(0L, properties.Find("threads")!.Type.Min);
    Assert.Equal(1L, properties.Find("players")!.Type.Min);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Serialize_ChangesOnlyTheLinesOfTheChangedProperties(bool crlf) {
    string text = crlf ? _crlf : _lf;
    ServerProperties properties = ServerProperties.Parse(text);

    properties.Set("gamemode", "creative");
    properties.Set("max-players", "20");
    properties.Set("level-seed", "42");
    string written = properties.Serialize();

    string newline = crlf ? "\r\n" : "\n";
    string[] before = text.Split(newline);
    string[] after = written.Split(newline);
    Assert.Equal(before.Length, after.Length);
    List<(string, string)> changed = [
      .. before.Zip(after).Where(pair => pair.First != pair.Second),
    ];
    Assert.Equal(
        [
          ("gamemode=survival", "gamemode=creative"),
          ("max-players=10", "max-players=20"),
          ("level-seed=", "level-seed=42"),
        ],
        changed);
  }

  [Fact]
  public void Serialize_Unchanged_IsTheFile() {
    string text = "﻿a = 1\r\n# about a\n\nb=x=y";
    ServerProperties properties = ServerProperties.Parse(text);
    Assert.Equal(text, properties.Serialize());
    Assert.Equal("x=y", properties.Find("b")!.Value);

    properties.Set("a", "2");
    properties.Set("b", "z");
    Assert.False(properties.Find("b")!.Value == properties.Find("b")!.Original);
    Assert.Equal("﻿a =2\r\n# about a\n\nb=z", properties.Serialize());
  }

  [Fact]
  public void Set_AValueNotOfTheType_Throws() {
    ServerProperties properties = ServerProperties.Parse(_lf);

    Assert.Throws<ArgumentException>(() => properties.Set("allow-cheats", "yes"));
    Assert.Throws<ArgumentException>(() => properties.Set("max-players", "ten"));
    Assert.Throws<ArgumentException>(() => properties.Set("no-such-key", "1"));
    Assert.False(properties.Changed);
  }
}
