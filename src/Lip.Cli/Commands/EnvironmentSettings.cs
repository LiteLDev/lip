using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// The environment options of commands that install or uninstall packages. When omitted, the
/// platform comes from the lock or the executables in the workspace, and the rid from the lock or
/// the runtime lip runs on.
public class EnvironmentSettings : CommandSettings {
  [CommandOption("--platform <PLATFORM>")]
  [Description("The game platform: server or client")]
  public string? Platform { get; init; }

  [CommandOption("--rid <RID>")]
  [Description("The .NET runtime identifier, e.g. win-x64")]
  public string? Rid { get; init; }

  public override ValidationResult Validate() {
    return Platform is null or "server" or "client"
        ? ValidationResult.Success()
        : ValidationResult.Error("--platform must be 'server' or 'client'.");
  }
}
