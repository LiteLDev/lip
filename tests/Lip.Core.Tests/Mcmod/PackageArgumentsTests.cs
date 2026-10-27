using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class PackageArgumentsTests {
  private readonly WorkspaceHarness _ws = new();

  [Theory]
  [InlineData("github.com/LiteLDev/LeviLamina", "github.com/LiteLDev/LeviLamina", null)]
  [InlineData("github.com/LiteLDev/LeviLamina@1.9.7", "github.com/LiteLDev/LeviLamina", "1.9.7")]
  [InlineData("github.com/a/b#client@1.0.0-rc.1", "github.com/a/b#client", "1.0.0-rc.1")]
  [InlineData("github.com/a/b#client", "github.com/a/b#client", null)]
  public void Parse_SpecForms_ReturnsSpecRequest(string argument, string reference, string? version) {
    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(RefKind.Spec, request.Ref.Kind);
    Assert.Equal(reference, request.Ref.ToString());
    if (version is null) {
      Assert.Null(request.Range);
    } else {
      Assert.Equal(
          new ExactVersionConstraint(SemVersion.Parse(version, SemVersionStyles.Strict)),
          request.Range);
    }
  }

  [Fact]
  public void Parse_HttpsUrl_ReturnsUrlRef() {
    PackageRequest request = Parse("https://example.com/x.mcmod#lua");

    Assert.Equal(RefKind.Url, request.Ref.Kind);
    Assert.Equal("lua", request.Ref.Variant);
  }

  [Theory]
  [InlineData("./pkg/", "./pkg/")]
  [InlineData("./pkg", "./pkg/")]
  [InlineData("pkg", "./pkg/")]
  [InlineData("pkg#lua", "./pkg/#lua")]
  [InlineData("./x.mcmod", "./x.mcmod")]
  [InlineData("sub/x.mcmod", "./sub/x.mcmod")]
  public void Parse_LocalPathInWorkspace_ReturnsRelativeRefOfWorkspace(
      string argument, string reference) {
    // Arrange
    _ws.AddFile("pkg/manifest.json", "{}");
    _ws.AddFile("x.mcmod", "zip");
    _ws.AddFile("sub/x.mcmod", "zip");

    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(reference, request.Ref.ToString());
    Assert.Equal(_ws.Root, request.Base.Key);
  }

  [Fact]
  public void Parse_PathOutsideWorkspace_ReturnsRelativeRefOfParentDirectory() {
    // Arrange
    string outside = _ws.Fs.Path.Combine(_ws.Fs.Path.GetDirectoryName(_ws.Root)!, "elsewhere");
    _ws.Fs.AddFile(_ws.Fs.Path.Combine(outside, "mod.mcmod"), new MockFileData("zip"));

    // Act
    PackageRequest request = Parse(_ws.Fs.Path.Combine(outside, "mod.mcmod"));

    // Assert
    Assert.Equal("./mod.mcmod", request.Ref.ToString());
    Assert.Equal(outside, request.Base.Key);
  }

  [Theory]
  [InlineData("github.com/a/b@v1.0.0")]
  [InlineData("github.com/a/b@1.0")]
  [InlineData("./missing/")]
  [InlineData("http://example.com/x.mcmod")]
  [InlineData("./pkg/#lua@1.0.0")]
  public void Parse_InvalidArgument_Throws(string argument) {
    Assert.Throws<ArgumentException>(() => Parse(argument));
  }

  private PackageRequest Parse(string argument) =>
      PackageArguments.Parse(argument, _ws.Root, _ws.Fs, _ws.ArchiveProvider);
}
