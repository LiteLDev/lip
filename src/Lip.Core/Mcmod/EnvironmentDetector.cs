using System.IO.Abstractions;
using System.Runtime.InteropServices;
using Lip.Core.Mcmod.Lock;

namespace Lip.Core.Mcmod;

/// Determines the environment (platform, rid) of a workspace (spec §5.1).
///
/// platform: explicit, then the lock, then bedrock_server.exe in the workspace root (server),
/// then bedrock_server_mod.exe (server), then Minecraft.Windows.exe (client), else null (see
/// LipEnvironment.RequirePlatform).
/// rid: explicit, then the lock, then the RID lip itself runs on.
///
/// bedrock_server_mod.exe deviates from spec §5.1: a server with LeviLamina installed renames
/// bedrock_server.exe to bedrock_server.exe.bak and runs bedrock_server_mod.exe instead, so
/// without it such a workspace would have no platform.
public class EnvironmentDetector(IFileSystem fs) : IEnvironmentDetector {
  public const string ServerExecutable = "bedrock_server.exe";
  public const string ModdedServerExecutable = "bedrock_server_mod.exe";
  public const string ClientExecutable = "Minecraft.Windows.exe";

  private readonly IFileSystem _fs = fs;

  public LipEnvironment Detect(
      string workspaceRoot,
      McmodPlatform? explicitPlatform,
      string? explicitRid,
      LockFile lockFile) {
    McmodPlatform? platform = explicitPlatform
        ?? FromLock(lockFile)
        ?? DetectFromWorkspace(workspaceRoot);

    string rid = !string.IsNullOrEmpty(explicitRid) ? explicitRid
        : lockFile.Rid is { } lockRid ? (lockRid != ""
            ? lockRid
            : throw new LockFormatException("The lock records an empty rid."))
        : RuntimeInformation.RuntimeIdentifier;

    return new LipEnvironment(platform, rid);
  }

  private static McmodPlatform? FromLock(LockFile lockFile) {
    if (lockFile.Platform is null) {
      return null;
    }

    return McmodPlatformExtensions.TryParse(lockFile.Platform, out McmodPlatform platform)
        ? platform
        : throw new LockFormatException(
            $"The lock records an invalid platform \"{lockFile.Platform}\","
            + " expected \"server\" or \"client\".");
  }

  private McmodPlatform? DetectFromWorkspace(string workspaceRoot) {
    if (!_fs.Directory.Exists(workspaceRoot)) {
      return null;
    }

    HashSet<string> names = new(
        _fs.Directory.EnumerateFiles(workspaceRoot).Select(f => _fs.Path.GetFileName(f)),
        StringComparer.OrdinalIgnoreCase);

    if (names.Contains(ServerExecutable) || names.Contains(ModdedServerExecutable)) {
      return McmodPlatform.Server;
    }

    if (names.Contains(ClientExecutable)) {
      return McmodPlatform.Client;
    }

    return null;
  }
}
