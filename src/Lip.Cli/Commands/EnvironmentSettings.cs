using System.ComponentModel;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// The environment options of commands that install or uninstall packages. lip manages server
/// workspaces only; when --platform is omitted, the platform comes from the lock or the RID lip
/// runs on.
public class EnvironmentSettings : CommandSettings {
  [CommandOption("--platform <RID>")]
  [Description("The platform, a .NET runtime identifier, e.g. win-x64")]
  public string? Platform { get; init; }
}
