using System.Text;
using System.Text.Json;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod.Parsing;

/// The templates of lip.toml (protocol/lip.toml.md).
public class PackageTemplatesTests {
  private const string _head = """
      format = 4
      id = "github.com/example/MyMod"
      version = "1.2.3"

      [info]
      name = "MyMod"
      tags = ["a", "b"]

      """;

  private static PackageFile Parse(string toml) =>
      PackageFileParser.Parse(Encoding.UTF8.GetBytes(toml));

  private static PackageEntry Entry(string entry) =>
      Assert.Single(Parse(_head + entry).Variants[""]);

  [Fact]
  public void Parse_VersionInUrl_IsRendered() {
    // Act
    PackageEntry entry = Entry("""
        [[variants.""]]
        side = "server"
        platform = "win-x64"

        [[variants."".assets]]
        type = "mcmod"
        urls = ["https://github.com/example/MyMod/releases/download/v{{version}}/MyMod-{{version}}.zip"]
        """);

    // Assert
    Assert.Equal(
        "https://github.com/example/MyMod/releases/download/v1.2.3/MyMod-1.2.3.zip",
        Assert.Single(Assert.Single(entry.Assets).Urls));
  }

  [Fact]
  public void Parse_NestedMembersAndExpressions_AreRendered() {
    // Act
    PackageEntry entry = Entry("""
        [[variants.""]]
        side = "server"
        platform = "win-x64"
        preserve = ["mods/{{info.name}}/config/"]

        [variants."".hooks]
        post_install = ["echo {{id}} {{info.tags[1]}} {{format + 1}} {{version | string.replace '.' '_'}}"]
        """);

    // Assert
    Assert.Equal("mods/MyMod/config/", Assert.Single(entry.PreserveFiles).ToString());
    Assert.Equal("echo github.com/example/MyMod b 5 1_2_3", Assert.Single(entry.Hooks.PostInstall));
  }

  [Fact]
  public void Parse_Variables_HoldValuesAsWritten() {
    // Act
    PackageFile file = Parse("""
        format = 4
        id = "github.com/example/{{info.name}}"
        version = "1.2.3"
        info = { name = "MyMod", description = "{{info.name}} {{version}}" }

        [[variants.""]]
        side = "server"
        platform = "win-x64"
        hooks = { pre_install = ["echo {{info.description}}"] }
        """);

    // Assert: info.description is rendered, but the variable info holds it as written.
    Assert.Equal("github.com/example/MyMod", file.Id);
    Assert.Equal("MyMod 1.2.3", file.Json.GetProperty("info").GetProperty("description").GetString());
    Assert.Equal("echo {{info.name}} {{version}}", Assert.Single(file.Variants[""]).Hooks.PreInstall[0]);
  }

  [Fact]
  public void ParseToml_KeysAndNonStrings_AreKept() {
    // Act
    using JsonDocument json = PackageFileParser.ParseToml(Encoding.UTF8.GetBytes(_head + """
        [variants]
        "{{version}}" = 1
        """));

    // Assert
    Assert.Equal(1, json.RootElement.GetProperty("variants").GetProperty("{{version}}").GetInt32());
    Assert.Equal(4, json.RootElement.GetProperty("format").GetInt32());
  }

  [Fact]
  public void Parse_UndefinedVariable_RendersEmpty() {
    // Act
    PackageEntry entry = Entry("""
        [[variants.""]]
        side = "server"
        platform = "win-x64"
        preserve = ["a{{nothing}}/"]
        """);

    // Assert
    Assert.Equal("a/", Assert.Single(entry.PreserveFiles).ToString());
  }

  [Theory]
  [InlineData("{{ if }}")]
  [InlineData("{{ nothing.y.z }}")]
  public void Parse_InvalidTemplate_Throws(string text) {
    // Act
    PackageFormatException ex = Assert.Throws<PackageFormatException>(() => Entry($$"""
        [[variants.""]]
        side = "server"
        platform = "win-x64"

        [variants."".hooks]
        pre_install = ["{{text}}"]
        """));

    // Assert
    Assert.Contains("at $.variants[\"\"][0].hooks.pre_install[0]", ex.Message);
    Assert.Contains("not a valid template", ex.Message);
  }

  [Fact]
  public void Parse_EscapedBraces_AreText() {
    // Act
    PackageEntry entry = Entry("""
        [[variants.""]]
        side = "server"
        platform = "win-x64"

        [variants."".hooks]
        pre_install = ["echo {{ '{{' }}x}}"]
        """);

    // Assert
    Assert.Equal("echo {{x}}", Assert.Single(entry.Hooks.PreInstall));
  }
}
