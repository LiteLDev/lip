using System.ComponentModel;
using System.Text.Json;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class ListCommand(ILipClient lipClient) : AsyncCommand<ListCommand.Settings> {
  private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
    WriteIndented = true
  };

  private readonly ILipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    (IEnumerable<string> ExplicitInstalled, IEnumerable<string> ImplicitInstalled) packages = await _lipClient.List();

    if (settings.Json) {
      // A value tuple has no properties to serialize, so name the two groups explicitly.
      AnsiConsole.Write(new Text(JsonSerializer.Serialize(
          new {
            ExplicitInstalled = packages.ExplicitInstalled,
            ImplicitInstalled = packages.ImplicitInstalled,
          },
          _jsonSerializerOptions)));
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

      AnsiConsole.Write(explicitTree);
      AnsiConsole.Write(implicitTree);
    }

    return 0;
  }
}
