using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Tests.Mcmod;

public class EnvironmentDetectorTests {
  private static readonly string _root = OperatingSystem.IsWindows()
      ? @"C:\lip-workspace"
      : "/lip-workspace";

  private readonly MockFileSystem _fs = new();
  private readonly EnvironmentDetector _detector;

  public EnvironmentDetectorTests() {
    _fs.AddDirectory(_root);
    _detector = new EnvironmentDetector(_fs);
  }

  private void AddRootFile(string name) =>
      _fs.AddFile(_fs.Path.Combine(_root, name), new MockFileData(""));

  [Theory]
  // explicit wins over everything
  [InlineData(McmodPlatform.Client, "server", "bedrock_server.exe", McmodPlatform.Client)]
  [InlineData(McmodPlatform.Server, null, "Minecraft.Windows.exe", McmodPlatform.Server)]
  [InlineData(McmodPlatform.Server, null, null, McmodPlatform.Server)]
  // lock wins over executables
  [InlineData(null, "client", "bedrock_server.exe", McmodPlatform.Client)]
  [InlineData(null, "server", "Minecraft.Windows.exe", McmodPlatform.Server)]
  [InlineData(null, "server", null, McmodPlatform.Server)]
  // executables
  [InlineData(null, null, "bedrock_server.exe", McmodPlatform.Server)]
  [InlineData(null, null, "Minecraft.Windows.exe", McmodPlatform.Client)]
  [InlineData(null, null, "BEDROCK_SERVER.EXE", McmodPlatform.Server)]
  [InlineData(null, null, "minecraft.windows.exe", McmodPlatform.Client)]
  [InlineData(null, null, "bedrock_server.exe|Minecraft.Windows.exe", McmodPlatform.Server)]
  // a LeviLamina server renames bedrock_server.exe and runs bedrock_server_mod.exe
  [InlineData(null, null, "bedrock_server_mod.exe|bedrock_server.exe.bak", McmodPlatform.Server)]
  [InlineData(null, null, "bedrock_server_mod.exe|Minecraft.Windows.exe", McmodPlatform.Server)]
  [InlineData(null, "client", "bedrock_server_mod.exe", McmodPlatform.Client)]
  // nothing decides
  [InlineData(null, null, null, null)]
  [InlineData(null, null, "bedrock_server.exe.bak", null)]
  public void Detect_PlatformSources_FollowsSpecOrder(
      McmodPlatform? explicitPlatform,
      string? lockPlatform,
      string? rootFiles,
      McmodPlatform? expected) {
    // Arrange
    foreach (string name in rootFiles?.Split('|') ?? []) {
      AddRootFile(name);
    }

    // Act
    LipEnvironment environment = _detector.Detect(
        _root, explicitPlatform, null, new LockFile { Platform = lockPlatform });

    // Assert
    Assert.Equal(expected, environment.Platform);
  }

  [Fact]
  public void Detect_ExecutableInSubdirectory_IsIgnored() {
    // Arrange
    AddRootFile(_fs.Path.Combine("bin", "bedrock_server.exe"));

    // Act
    LipEnvironment environment = _detector.Detect(_root, null, null, new LockFile());

    // Assert
    Assert.Null(environment.Platform);
    Assert.Throws<PlatformUnknownException>(() => environment.RequirePlatform());
  }

  [Fact]
  public void Detect_MissingWorkspaceDirectory_ReturnsNullPlatform() {
    // Act
    LipEnvironment environment = _detector.Detect(
        _fs.Path.Combine(_root, "missing"), null, null, new LockFile());

    // Assert
    Assert.Null(environment.Platform);
  }

  [Theory]
  [InlineData("linux-x64", "win-x64", "linux-x64")]
  [InlineData("linux-x64", null, "linux-x64")]
  [InlineData(null, "win-arm64", "win-arm64")]
  [InlineData("", "win-arm64", "win-arm64")]
  public void Detect_RidSources_FollowsSpecOrder(
      string? explicitRid, string? lockRid, string expected) {
    // Act
    LipEnvironment environment = _detector.Detect(
        _root, null, explicitRid, new LockFile { Rid = lockRid });

    // Assert
    Assert.Equal(expected, environment.Rid);
  }

  [Fact]
  public void Detect_NoRid_UsesRuntimeIdentifier() {
    // Act
    LipEnvironment environment = _detector.Detect(_root, null, null, new LockFile());

    // Assert
    Assert.Equal(RuntimeInformation.RuntimeIdentifier, environment.Rid);
  }

  [Theory]
  [InlineData("universal")]
  [InlineData("Server")]
  [InlineData("")]
  public void Detect_InvalidLockPlatform_ThrowsLockFormatException(string lockPlatform) {
    // Arrange
    AddRootFile("bedrock_server.exe");

    // Act & Assert
    Assert.Throws<LockFormatException>(() => _detector.Detect(
        _root, null, null, new LockFile { Platform = lockPlatform }));
  }

  [Fact]
  public void Detect_InvalidLockPlatformWithExplicitPlatform_UsesExplicit() {
    // Act
    LipEnvironment environment = _detector.Detect(
        _root, McmodPlatform.Client, null, new LockFile { Platform = "universal" });

    // Assert
    Assert.Equal(McmodPlatform.Client, environment.Platform);
  }

  [Fact]
  public void Detect_EmptyLockRid_ThrowsLockFormatException() {
    // Act & Assert
    Assert.Throws<LockFormatException>(
        () => _detector.Detect(_root, null, null, new LockFile { Rid = "" }));
  }
}
