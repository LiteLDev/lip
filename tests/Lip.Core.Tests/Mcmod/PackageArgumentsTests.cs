using System.IO.Abstractions.TestingHelpers;
using Lip.Core.Mcmod;

namespace Lip.Core.Tests.Mcmod;

/// Package arguments (protocol/cli.md §3).
public class PackageArgumentsTests {
  private const string _commit = "0123456789abcdef0123456789abcdef01234567";

  private readonly WorkspaceHarness _ws = new();

  [Theory]
  [InlineData("github.com/LiteLDev/LeviLamina", "github.com/LiteLDev/LeviLamina")]
  [InlineData("github.com/a/b#client", "github.com/a/b#client")]
  public void Parse_RemoteRepository_ReturnsSpecRequest(string argument, string reference) {
    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(RefKind.Spec, request.Ref.Kind);
    Assert.Null(request.Ref.GitRef);
    Assert.Equal(reference, request.Ref.ToString());
    Assert.Null(request.Range);
  }

  [Theory]
  [InlineData("github.com/LiteLDev/LeviLamina@1.9.7", GitRefKind.Version, "1.9.7", "")]
  [InlineData("github.com/a/b#client@1.0.0-rc.1", GitRefKind.Version, "1.0.0-rc.1", "client")]
  [InlineData("github.com/LiteLDev/LeviLamina@branch:main", GitRefKind.Branch, "main", "")]
  [InlineData("github.com/a/b#client@branch:feat/x#1", GitRefKind.Branch, "feat/x#1", "client")]
  [InlineData("github.com/a/b@tag:release-2024", GitRefKind.Tag, "release-2024", "")]
  [InlineData("github.com/a/b@tag:build.zip", GitRefKind.Tag, "build.zip", "")]
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
    // Act
    PackageRequest request = Parse("https://example.com/x.zip#lua");

    // Assert
    Assert.Equal(RefKind.Url, request.Ref.Kind);
    Assert.Equal("https://example.com/x.zip", request.Ref.Target);
    Assert.Equal("lua", request.Ref.Variant);
    Assert.Null(request.Range);
  }

  [Theory]
  [InlineData("./x.zip", "x.zip", "")]
  [InlineData("x.zip", "x.zip", "")]
  [InlineData("UP.ZIP", "UP.ZIP", "")]
  [InlineData("sub/x.zip#lua", "sub/x.zip", "lua")]
  [InlineData("a#b.zip", "a#b.zip", "")]
  [InlineData("a#b.zip#lua", "a#b.zip", "lua")]
  [InlineData("./c#d#lua", "c#d", "lua")]
  public void Parse_RelativeLocalFile_ReturnsFileRefOfAbsolutePath(
      string argument, string file, string variant) {
    // Arrange
    _ws.AddZip("x.zip", []);
    _ws.AddZip("UP.ZIP", []);
    _ws.AddZip("sub/x.zip", []);
    _ws.AddZip("a#b.zip", []);
    _ws.AddZip("c#d", []);

    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(RefKind.File, request.Ref.Kind);
    Assert.Equal(_ws.Path(file), request.Ref.LocalPath);
    Assert.Equal(variant, request.Ref.Variant);
    Assert.Null(request.Range);
  }

  [Fact]
  public void Parse_ExistingFileNotEndingInZip_IsALocalFile() {
    // Arrange
    _ws.AddZip("pkg.mcmod", []);

    // Act
    PackageRequest request = Parse("pkg.mcmod");

    // Assert
    Assert.Equal(RefKind.File, request.Ref.Kind);
    Assert.Equal(_ws.Path("pkg.mcmod"), request.Ref.LocalPath);
  }

  [Fact]
  public void Parse_MissingMcmodFile_IsNotALocalPath() {
    // Act
    PackageRequest request = Parse("example.com/x.mcmod");

    // Assert: ".mcmod" is no local-path extension, so this is a remote repository.
    Assert.Equal(RefKind.Spec, request.Ref.Kind);
  }

  [Fact]
  public void Parse_AbsoluteLocalFileOutsideWorkspace_ReturnsFileRef() {
    // Arrange
    string outside = _ws.Fs.Path.Combine(_ws.Fs.Path.GetDirectoryName(_ws.Root)!, "elsewhere");
    string path = _ws.Fs.Path.Combine(outside, "mod.zip");
    _ws.Fs.AddFile(path, new MockFileData("zip"));

    // Act
    PackageRequest request = Parse(path);

    // Assert
    Assert.Equal(path, request.Ref.LocalPath);
    Assert.StartsWith("file:///", request.Ref.ToString());
  }

  [Theory]
  [InlineData("./pkg", "pkg", "")]
  [InlineData("./pkg/", "pkg", "")]
  [InlineData("pkg", "pkg", "")]
  [InlineData("pkg#lua", "pkg", "lua")]
  [InlineData(".", "", "")]
  public void Parse_Directory_ReturnsFileRefOfTheDirectory(
      string argument, string directory, string variant) {
    // Arrange
    _ws.AddFile("pkg/tooth.json", "{}");

    // Act
    PackageRequest request = Parse(argument);

    // Assert
    Assert.Equal(RefKind.File, request.Ref.Kind);
    Assert.Equal(
        Path.TrimEndingDirectorySeparator(directory == "" ? _ws.Root : _ws.Path(directory)),
        request.Ref.LocalPath);
    Assert.Equal(variant, request.Ref.Variant);
  }

  [Theory]
  [InlineData("github.com/a/b@v1.0.0")]
  [InlineData("github.com/a/b@1.0")]
  [InlineData("github.com/a/b@branch:")]
  [InlineData("github.com/a/b@branch:a..b")]
  [InlineData("github.com/a/b@rev:abc1234")]
  [InlineData("github.com/a/b@commit:abc")]
  [InlineData("github.com/a/b#")]
  [InlineData("./missing.zip")]
  [InlineData("missing.zip")]
  [InlineData("missing.ZIP")]
  [InlineData("./missing")]
  [InlineData("https://example.com/x.zip#")]
  [InlineData("http://example.com/x.zip")]
  [InlineData("file:///c/x.zip")]
  [InlineData("ftp://example.com/x.zip")]
  public void Parse_InvalidArgument_Throws(string argument) {
    // Act & Assert
    Assert.Throws<ArgumentException>(() => Parse(argument));
  }

  private PackageRequest Parse(string argument) =>
      PackageArguments.Parse(argument, _ws.Root, _ws.Fs);
}
