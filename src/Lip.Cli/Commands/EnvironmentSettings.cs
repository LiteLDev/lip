using System.ComponentModel;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// The environment options of commands that install or uninstall packages. lip manages server
/// workspaces only; when --rid is omitted, the rid comes from the lock or the runtime lip runs on.
public class EnvironmentSettings : CommandSettings {
  [CommandOption("--rid <RID>")]
  [Description("The .NET runtime identifier, e.g. win-x64")]
  public string? Rid { get; init; }
}
