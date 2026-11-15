using System.ComponentModel;
using System.Text.Json;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip config get (protocol/lip-config.md): the value of a key, or with --json the value as a JSON
/// string, or null when the key has no value.
public class ConfigGetCommand(LipClient lipClient, CommandOutput output)
    : AsyncCommand<ConfigGetCommand.Settings> {
  private readonly CommandOutput _output = output;
  private readonly LipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandArgument(0, "<KEY>")]
    [Description("The configuration key to get")]
    public required string Key { get; init; }

    [CommandOption("--workspace")]
    [Description("Use the configuration of the workspace (.lip/config.toml) instead of the user's")]
    public bool Workspace { get; init; }

    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    string? value = await _lipClient.ConfigGet(settings.Key, settings.Workspace);

    if (settings.Json) {
      _output.Out.WriteLine(JsonSerializer.Serialize(value));
    } else {
      Table table = new();
      table.AddColumn("Key");
      table.AddColumn("Value");

      table.AddRow(Markup.Escape(settings.Key), ConfigListCommand.Describe(value));

      _output.Console.Write(table);
    }

    return 0;
  }
}
