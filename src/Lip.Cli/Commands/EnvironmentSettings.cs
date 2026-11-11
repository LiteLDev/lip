using System.ComponentModel;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// The environment options of commands that install or uninstall packages. lip manages server
/// workspaces only; when --platform is omitted, the platform is the one the state file records,
/// otherwise win-x64 on every system (protocol/lip.md, "Environment").
public class EnvironmentSettings : CommandSettings {
  [CommandOption("--platform <RID>")]
  [Description("The platform, a .NET runtime identifier, e.g. linux-x64 (default: the platform of the workspace, otherwise win-x64)")]
  public string? Platform { get; init; }
}
