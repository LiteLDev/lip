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
