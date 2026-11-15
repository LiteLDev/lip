using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class ListCommand(LipClient lipClient, CommandOutput output)
    : AsyncCommand<ListCommand.Settings> {
  private readonly CommandOutput _output = output;
  private readonly LipClient _lipClient = lipClient;

  /// The packages written by --json.
  internal sealed record Installed(
      IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled);

  public class Settings : CommandSettings {
    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    (IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled) packages = await _lipClient.List();

    if (settings.Json) {
      // A value tuple has no properties to serialize, so name the two groups explicitly. JSON is
      // written as is: Spectre.Console would wrap long lines, breaking strings apart.
      _output.Out.WriteLine(JsonSerializer.Serialize(
          new Installed(packages.ExplicitInstalled, packages.ImplicitInstalled),
          CliJsonContext.TypeInfo<Installed>(CliJsonContext.Indented)));
    } else {
      Tree explicitTree = new("User-Requested");
      foreach (string package in packages.ExplicitInstalled) {
        explicitTree.AddNode(package);
      }
      if (!packages.ExplicitInstalled.Any()) {
        explicitTree.AddNode("[grey italic]None[/]");
      }

      Tree implicitTree = new("Dependencies");
      foreach (string package in packages.ImplicitInstalled) {
        implicitTree.AddNode(package);
      }
      if (!packages.ImplicitInstalled.Any()) {
        implicitTree.AddNode("[grey italic]None[/]");
      }

      _output.Console.Write(explicitTree);
      _output.Console.Write(implicitTree);
    }

    return 0;
  }
}
