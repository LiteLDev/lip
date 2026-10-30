using System.ComponentModel;
using System.Text.Json;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class ConfigListCommand(ILipClient lipClient) : AsyncCommand<ConfigListCommand.Settings> {
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
    IDictionary<string, string> config = await _lipClient.ConfigList();

    if (settings.Json) {
      Console.Out.WriteLine(JsonSerializer.Serialize(config, _jsonSerializerOptions));
    } else {
      Table table = new();
      table.AddColumn("Key");
      table.AddColumn("Value");

      foreach (KeyValuePair<string, string> kvp in config) {
        table.AddRow(kvp.Key, kvp.Value);
      }

      AnsiConsole.Write(table);
    }

    return 0;
  }
}
