using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Mcmod;
using Semver;

namespace Lip.Core.Tests.Mcmod;

public class PackageArgumentsTests {
  private const string _commit = "0123456789abcdef0123456789abcdef01234567";

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
    Assert.Null(request.Ref.GitRef);
    Assert.Equal(reference, request.Ref.ToString());
    Assert.Null(request.Base);
    if (version is null) {
      Assert.Null(request.Range);
    } else {
      Assert.Equal(
          new ExactVersionConstraint(SemVersion.Parse(version, SemVersionStyles.Strict)),
          request.Range);
    }
  }

  [Theory]
  [InlineData("github.com/LiteLDev/LeviLamina@branch:main", GitRefKind.Branch, "main", "")]
  [InlineData("github.com/a/b#client@branch:feat/x#1", GitRefKind.Branch, "feat/x#1", "client")]
  [InlineData("github.com/a/b@tag:release-2024", GitRefKind.Tag, "release-2024", "")]
  [InlineData("github.com/a/b@rev:" + _commit, GitRefKind.Rev, _commit, "")]
  public void Parse_GitRef_ReturnsPinnedSpecRequest(
      string argument, GitRefKind kind, string name, string variant) {
    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(RefKind.Spec, request.Ref.Kind);
    Assert.Equal(kind, request.Ref.GitRef?.Kind);
    Assert.Equal(name, request.Ref.GitRef?.Name);
    Assert.Equal(variant, request.Ref.Variant);
    Assert.Null(request.Range);
    Assert.Equal(argument, request.Ref.ToString());
  }

  [Fact]
  public void Parse_HttpsUrl_ReturnsUrlRef() {
    PackageRequest request = Parse("https://example.com/x.mcmod#lua");

    Assert.Equal(RefKind.Url, request.Ref.Kind);
    Assert.Equal("lua", request.Ref.Variant);
  }

  [Theory]
  [InlineData("./x.mcmod", "x.mcmod", "")]
  [InlineData("x.mcmod", "x.mcmod", "")]
  [InlineData("sub/x.zip#lua", "sub/x.zip", "lua")]
  [InlineData("a#b.mcmod", "a#b.mcmod", "")]
  public void Parse_RelativeLocalFile_ReturnsFileRefOfAbsolutePath(
      string argument, string file, string variant) {
    // Arrange
    _ws.AddZip("x.mcmod", []);
    _ws.AddZip("sub/x.zip", []);
    _ws.AddZip("a#b.mcmod", []);

    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(RefKind.File, request.Ref.Kind);
    Assert.Equal(_ws.Path(file), request.Ref.LocalPath);
    Assert.Equal(variant, request.Ref.Variant);
  }

  [Fact]
  public void Parse_AbsoluteLocalFileOutsideWorkspace_ReturnsFileRef() {
    // Arrange
    string outside = _ws.Fs.Path.Combine(_ws.Fs.Path.GetDirectoryName(_ws.Root)!, "elsewhere");
    string path = _ws.Fs.Path.Combine(outside, "mod.mcmod");
    _ws.Fs.AddFile(path, new MockFileData("zip"));

    // Act
    PackageRequest request = Parse(path);

    // Assert
    Assert.Equal(path, request.Ref.LocalPath);
    Assert.StartsWith("file:///", request.Ref.ToString());
  }

  [Theory]
  [InlineData("./pkg")]
  [InlineData("./pkg/")]
  [InlineData("pkg")]
  [InlineData(".")]
  public void Parse_Directory_ThrowsWithClearMessage(string argument) {
    // Arrange
    _ws.AddFile("pkg/manifest.json", "{}");

    // Act & Assert
    ArgumentException ex = Assert.Throws<ArgumentException>(() => Parse(argument));
    Assert.Contains("is a directory", ex.Message);
  }

  [Theory]
  [InlineData("github.com/a/b@v1.0.0")]
  [InlineData("github.com/a/b@1.0")]
  [InlineData("github.com/a/b@branch:")]
  [InlineData("github.com/a/b@branch:a..b")]
  [InlineData("github.com/a/b@rev:abc1234")]
  [InlineData("github.com/a/b@commit:abc")]
  [InlineData("./missing.mcmod")]
  [InlineData("missing.zip")]
  [InlineData("http://example.com/x.mcmod")]
  [InlineData("https://example.com/x.mcmod@branch:main")]
  public void Parse_InvalidArgument_Throws(string argument) {
    Assert.Throws<ArgumentException>(() => Parse(argument));
  }

  private PackageRequest Parse(string argument) => PackageArguments.Parse(argument, _ws.Fs);
}
