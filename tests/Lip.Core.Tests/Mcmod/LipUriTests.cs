using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

/// Links of the scheme lip (protocol/lip-uri.md).
public class LipUriTests {
  [Theory]
  [InlineData(
      "lip://install/github.com/IceBlcokMC/PLand?version=0.26.1&variant=headless",
      "github.com/IceBlcokMC/PLand", "0.26.1", "headless",
      "github.com/IceBlcokMC/PLand#headless@0.26.1")]
  [InlineData(
      "lip://install/github.com/LiteLDev/LeviLamina", "github.com/LiteLDev/LeviLamina", null, "",
      "github.com/LiteLDev/LeviLamina")]
  // One '/' at the end, which a browser may add.
  [InlineData(
      "lip://install/github.com/LiteLDev/LeviLamina/", "github.com/LiteLDev/LeviLamina", null,
      "", "github.com/LiteLDev/LeviLamina")]
  // The scheme and the action are compared ignoring case; the path keeps its case.
  [InlineData(
      "LIP://Install/github.com/Owner/Repo?version=1.0.0", "github.com/Owner/Repo", "1.0.0", "",
      "github.com/Owner/Repo@1.0.0")]
  // Percent-encoded octets are decoded.
  [InlineData(
      "lip://install/github.com%2Fa%2Fb%2Dc?variant=lua%2Fsub", "github.com/a/b-c", null,
      "lua/sub", "github.com/a/b-c#lua/sub")]
  // '+' is itself: the build metadata of a version.
  [InlineData(
      "lip://install/github.com/a/b?version=1.0.0-rc.1%2Bbuild.5", "github.com/a/b",
      "1.0.0-rc.1+build.5", "", "github.com/a/b@1.0.0-rc.1+build.5")]
  [InlineData(
      "lip://install/github.com/a/b?version=1.0.0+build", "github.com/a/b", "1.0.0+build", "",
      "github.com/a/b@1.0.0+build")]
  // Unknown parameters, empty values, empty parameters and the fragment are left out.
  [InlineData(
      "lip://install/github.com/a/b?utm_source=web&&version=&variant=&x#frag", "github.com/a/b",
      null, "", "github.com/a/b")]
  [InlineData(
      "lip://install/github.com/a/b?variant=client&ref=levimc.org", "github.com/a/b", null,
      "client", "github.com/a/b#client")]
  public void TryParse_ValidLink_ReturnsThePackage(
      string link, string modulePath, string? version, string variant, string argument) {
    Assert.True(LipUri.TryParse(link, out LipUri? uri, out string? error), error);

    Assert.Equal(LipUri.InstallAction, uri.Action);
    Assert.Equal(modulePath, uri.ModulePath);
    Assert.Equal(version, uri.Version);
    Assert.Equal(variant, uri.Variant);
    Assert.Equal(argument, uri.PackageArgument);
  }

  [Theory]
  // Not a link of lip.
  [InlineData("")]
  [InlineData("https://levimc.org/mods/github.com/a/b")]
  [InlineData("lip:install/github.com/a/b")]
  [InlineData("lipx://install/github.com/a/b")]
  // No action, or another one.
  [InlineData("lip:///github.com/a/b")]
  [InlineData("lip://uninstall/github.com/a/b")]
  [InlineData("lip://run/github.com/a/b")]
  [InlineData("lip://install@evil/github.com/a/b")]
  [InlineData("lip://install:80/github.com/a/b")]
  // No package.
  [InlineData("lip://install")]
  [InlineData("lip://install/")]
  [InlineData("lip://install//")]
  [InlineData("lip://install?version=1.0.0")]
  // Not a remote repository: a URL, a local path, a shorthand, a ref in the path.
  [InlineData("lip://install/https://evil.example/x.zip")]
  [InlineData("lip://install/https%3A%2F%2Fevil.example%2Fx.zip")]
  [InlineData("lip://install/file:///C:/x.zip")]
  [InlineData("lip://install/C:%5CUsers%5Cx")]
  [InlineData("lip://install/%2E%2E/%2E%2E/x")]
  [InlineData("lip://install/../../github.com/a/b")]
  [InlineData("lip://install/github.com/a/../b")]
  [InlineData("lip://install/github.com/a/b/..")]
  [InlineData("lip://install/github.com/a/b/.")]
  [InlineData("lip://install/LeviLamina")]
  [InlineData("lip://install/github.com/a/b@branch:x")]
  [InlineData("lip://install/github.com/a/b%40branch:x")]
  [InlineData("lip://install/github.com/a/b@1.0.0")]
  [InlineData("lip://install/github.com/a/b%23lua")]
  [InlineData("lip://install/github.com/a//b")]
  [InlineData("lip://install/github.com/a/b//")]
  // The host of a module path is lowercase.
  [InlineData("lip://install/GitHub.com/a/b")]
  // Characters that a URI does not allow, and percent-encodings that are not UTF-8 or of a
  // character that a module path does not allow.
  [InlineData("lip://install/github.com/a/b c")]
  [InlineData("lip://install/github.com/a/é")]
  [InlineData("lip://install/github.com/a/%C3%A9")]
  [InlineData("lip://install/github.com/a/%FF")]
  [InlineData("lip://install/github.com/a/%zz")]
  [InlineData("lip://install/github.com/a/b%")]
  [InlineData("lip://install/github.com/a/b%00")]
  [InlineData("lip://install/github.com/a/b%0A")]
  [InlineData("lip://install/github.com/a/b\n")]
  [InlineData("lip://install/github.com/a/b\"")]
  [InlineData("lip://install/github.com/a/b\" --other")]
  // A version that is not strict SemVer: a ref, a leading v, a partial version.
  [InlineData("lip://install/github.com/a/b?version=branch:main")]
  [InlineData("lip://install/github.com/a/b?version=branch%3Amain")]
  [InlineData("lip://install/github.com/a/b?version=tag:v1")]
  [InlineData("lip://install/github.com/a/b?version=rev:0123456789abcdef0123456789abcdef01234567")]
  [InlineData("lip://install/github.com/a/b?version=v1.0.0")]
  [InlineData("lip://install/github.com/a/b?version=1.0")]
  [InlineData("lip://install/github.com/a/b?version=latest")]
  [InlineData("lip://install/github.com/a/b?version=https://evil.example/x.zip")]
  [InlineData("lip://install/github.com/a/b?version=1.0.0%0A")]
  // A variant that Ref does not allow.
  [InlineData("lip://install/github.com/a/b?variant=Client")]
  [InlineData("lip://install/github.com/a/b?variant=a/b/c")]
  [InlineData("lip://install/github.com/a/b?variant=..")]
  [InlineData("lip://install/github.com/a/b?variant=x%23y")]
  // A parameter given twice.
  [InlineData("lip://install/github.com/a/b?version=1.0.0&version=2.0.0")]
  [InlineData("lip://install/github.com/a/b?variant=a&variant=b")]
  // A query that is not UTF-8.
  [InlineData("lip://install/github.com/a/b?version=%FF")]
  public void TryParse_InvalidLink_ReturnsWhy(string link) {
    Assert.False(LipUri.TryParse(link, out LipUri? uri, out string? error));

    Assert.Null(uri);
    Assert.False(string.IsNullOrEmpty(error));
  }

  [Fact]
  public void TryParse_TooLongLink_IsInvalid() {
    string link = "lip://install/github.com/a/" + new string('b', LipUri.MaxLength);

    Assert.False(LipUri.TryParse(link, out _, out string? error));
    Assert.Contains($"{LipUri.MaxLength}", error);
  }

  [Fact]
  public void TryParse_VersionWithLeadingV_SaysToDropIt() {
    Assert.False(LipUri.TryParse(
        "lip://install/github.com/a/b?version=v1.2.3", out _, out string? error));

    Assert.Contains("'1.2.3'", error);
  }

  [Fact]
  public void TryParse_UnknownAction_NamesIt() {
    Assert.False(LipUri.TryParse("lip://uninstall/github.com/a/b", out _, out string? error));

    Assert.Contains("'uninstall'", error);
  }

  [Fact]
  public void Parse_InvalidLink_ThrowsFormatException() {
    FormatException ex = Assert.Throws<FormatException>(() => LipUri.Parse("lip://install/"));

    Assert.Contains("'lip://install/'", ex.Message);
  }
}
