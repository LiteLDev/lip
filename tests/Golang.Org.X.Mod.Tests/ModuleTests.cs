namespace Golang.Org.X.Mod.Tests;

public class ModuleTests {
  public static TheoryData<string, bool, bool, bool> CheckPathTests => new()
  {
        {"x.y/z", true, true, true},
        {"x.y", true, true, true},

        {"", false, false, false},
        {"x.y/\xFFz", false, false, false},
        {"/x.y/z", false, false, false},
        {"x./z", false, false, false},
        {".x/z", false, true, true},
        {"-x/z", false, false, true},
        {"x..y/z", true, true, true},
        {"x.y/z/../../w", false, false, false},
        {"x.y//z", false, false, false},
        {"x.y/z//w", false, false, false},
        {"x.y/z/", false, false, false},

        {"x.y/z/v0", false, true, true},
        {"x.y/z/v1", false, true, true},
        {"x.y/z/v2", true, true, true},
        {"x.y/z/v2.0", false, true, true},
        {"X.y/z", false, true, true},

        {"!x.y/z", false, false, true},
        {"_x.y/z", false, true, true},
        {"x.y!/z", false, false, true},
        {"x.y\"/z", false, false, false},
        {"x.y#/z", false, false, true},
        {"x.y$/z", false, false, true},
        {"x.y%/z", false, false, true},
        {"x.y&/z", false, false, true},
        {"x.y'/z", false, false, false},
        {"x.y(/z", false, false, true},
        {"x.y)/z", false, false, true},
        {"x.y*/z", false, false, false},
        {"x.y+/z", false, true, true},
        {"x.y,/z", false, false, true},
        {"x.y-/z", true, true, true},
        {"x.y./zt", false, false, false},
        {"x.y:/z", false, false, false},
        {"x.y;/z", false, false, false},
        {"x.y</z", false, false, false},
        {"x.y=/z", false, false, true},
        {"x.y>/z", false, false, false},
        {"x.y?/z", false, false, false},
        {"x.y@/z", false, false, true},
        {"x.y[/z", false, false, true},
        {"x.y\\/z", false, false, false},
        {"x.y]/z", false, false, true},
        {"x.y^/z", false, false, true},
        {"x.y_/z", false, true, true},
        {"x.y`/z", false, false, false},
        {"x.y{/z", false, false, true},
        {"x.y}/z", false, false, true},
        {"x.y~/z", false, true, true},
        {"x.y/z!", false, false, true},
        {"x.y/z\"", false, false, false},
        {"x.y/z#", false, false, true},
        {"x.y/z$", false, false, true},
        {"x.y/z%", false, false, true},
        {"x.y/z&", false, false, true},
        {"x.y/z'", false, false, false},
        {"x.y/z(", false, false, true},
        {"x.y/z)", false, false, true},
        {"x.y/z*", false, false, false},
        {"x.y/z++", false, true, true},
        {"x.y/z,", false, false, true},
        {"x.y/z-", true, true, true},
        {"x.y/z.t", true, true, true},
        {"x.y/z/t", true, true, true},
        {"x.y/z:", false, false, false},
        {"x.y/z;", false, false, false},
        {"x.y/z<", false, false, false},
        {"x.y/z=", false, false, true},
        {"x.y/z>", false, false, false},
        {"x.y/z?", false, false, false},
        {"x.y/z@", false, false, true},
        {"x.y/z[", false, false, true},
        {"x.y/z\\", false, false, false},
        {"x.y/z]", false, false, true},
        {"x.y/z^", false, false, true},
        {"x.y/z_", true, true, true},
        {"x.y/z`", false, false, false},
        {"x.y/z{", false, false, true},
        {"x.y/z}", false, false, true},
        {"x.y/z~", true, true, true},
        {"x.y/x.foo", true, true, true},
        {"x.y/aux.foo", false, false, false},
        {"x.y/prn", false, false, false},
        {"x.y/prn2", true, true, true},
        {"x.y/com", true, true, true},
        {"x.y/com1", false, false, false},
        {"x.y/com1.txt", false, false, false},
        {"x.y/calm1", true, true, true},
        {"x.y/z~", true, true, true},
        {"x.y/z~0", false, false, true},
        {"x.y/z~09", false, false, true},
        {"x.y/z09", true, true, true},
        {"x.y/z09~", true, true, true},
        {"x.y/z09~09z", true, true, true},
        {"x.y/z09~09z~09", false, false, true},
        {"github.com/!123/logrus", false, false, true},

        // TODO: CL 41822 allowed Unicode letters in old "go get"
        // without due consideration of the implications, and only on github.com (!).
        // For now, we disallow non-ASCII characters in module mode,
        // in both module paths and general import paths,
        // until we can get the implications right.
        // When we do, we'll enable them everywhere, not just for GitHub.
        {"github.com/user/unicode/испытание", false, false, true},

        {"../x", false, false, false},
        {"./y", false, false, false},
        {"x:y", false, false, false},
        {@"\temp\foo", false, false, false},
        {".gitignore", false, true, true},
        {".github/ISSUE_TEMPLATE", false, true, true},
        {"x☺y", false, false, false},
    };

  [Theory]
  [MemberData(nameof(CheckPathTests))]
#pragma warning disable xUnit1026 // Theory methods should use all of their parameters
  public void CheckPath_ValidInput_ReturnsExpectedOutput(string path, bool ok, bool importOK, bool fileOK)
#pragma warning restore xUnit1026 // Theory methods should use all of their parameters
  {
    // Act.
    Exception? err = Module.CheckPath(path);

    // Assert.
    Assert.Equal(ok, err is null);
  }

  [Theory]
  [InlineData("example.com/a\uD800b")]
  [InlineData("example.com/\uDC00")]
  public void CheckPath_LoneSurrogate_ReturnsError(string path) {
    // Act.
    Exception? err = Module.CheckPath(path);

    // Assert: a string that is not valid UTF-8 in Go is not a module path.
    Assert.NotNull(err);
  }

  [Theory]
  [InlineData("x.y/z", "")]
  [InlineData("x.y/z", "/v2")]
  [InlineData("x.y/z", "/v3")]
  [InlineData("x.y/v", "")]
  [InlineData("gopkg.in/yaml", ".v0")]
  [InlineData("gopkg.in/yaml", ".v1")]
  [InlineData("gopkg.in/yaml", ".v2")]
  [InlineData("gopkg.in/yaml", ".v3")]
  public void SplitPathVersion_ValidInput_ReturnsExpectedOutput(string expectedPathPrefix, string expectedVersion) {
    // Act.
    (string? pathPrefix, string? version, bool ok) = Module.SplitPathVersion(expectedPathPrefix + expectedVersion);

    // Assert.
    Assert.Equal(expectedPathPrefix, pathPrefix);
    Assert.Equal(expectedVersion, version);
    Assert.True(ok);
  }

  [Theory]
  [MemberData(nameof(CheckPathTests))]
#pragma warning disable xUnit1026 // Theory methods should use all of their parameters
  public void SplitPathVersion_MoreValidPaths_ReturnsExpectedOutput(string path, bool expectedOk, bool importOK, bool fileOK)
#pragma warning restore xUnit1026 // Theory methods should use all of their parameters
  {
    // Act.
    (string? pathPrefix, string? version, _) = Module.SplitPathVersion(path);

    // Assert.
    Assert.Equal(path, pathPrefix + version);
  }
}
