using System.ComponentModel;
using System.Text.Json;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

[Description("Shows available versions for a package")]
public class VersionsCommand(ILipClient lipClient) : AsyncCommand<VersionsCommand.Settings> {
  private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
    WriteIndented = true
  };

  private readonly ILipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<PACKAGE>")]
    [Description("The package to show versions for")]
    public required string Package { get; init; }

    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    IEnumerable<string> versions = await _lipClient.Versions(settings.Package);

    if (settings.Json) {
      // Not through Spectre.Console, which wraps long lines at the console width.
      Console.Out.WriteLine(JsonSerializer.Serialize(versions, _jsonSerializerOptions));
    } else {
      Tree tree = new($"Versions");

      foreach (string version in versions) {
        tree.AddNode(version);
      }

      AnsiConsole.Write(tree);
    }

    return 0;
  }
}
