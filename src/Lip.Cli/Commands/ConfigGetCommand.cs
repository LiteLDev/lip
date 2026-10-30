using System.ComponentModel;
using System.Text.Json;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class ConfigGetCommand(ILipClient lipClient) : AsyncCommand<ConfigGetCommand.Settings> {
  private readonly ILipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<KEY>")]
    [Description("The configuration key to get")]
    public required string Key { get; init; }

    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    string value = await _lipClient.ConfigGet(settings.Key);

    if (settings.Json) {
      Console.Out.WriteLine(JsonSerializer.Serialize(value));
    } else {
      Table table = new();
      table.AddColumn("Key");
      table.AddColumn("Value");

      table.AddRow(settings.Key, value);

      AnsiConsole.Write(table);
    }

    return 0;
  }
}
