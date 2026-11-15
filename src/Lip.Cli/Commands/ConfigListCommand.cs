using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip config list (protocol/lip-config.md): every key of the scope with its value, or with --json
/// an object mapping each key to its value as a JSON string, or null when it has none.
public class ConfigListCommand(LipClient lipClient, CommandOutput output)
    : AsyncCommand<ConfigListCommand.Settings> {
  private readonly CommandOutput _output = output;
  private readonly JsonSerializerOptions _jsonSerializerOptions = new() {
    WriteIndented = true,
    TypeInfoResolver = CliJsonContext.Default,
  };

  private readonly LipClient _lipClient = lipClient;

  public class Settings : CommandSettings {
    [CommandOption("--workspace")]
    [Description("Use the configuration of the workspace (.lip/config.toml) instead of the user's")]
    public bool Workspace { get; init; }

    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; init; }
  }

  /// A value as markup for a table: a key without a value and an empty value are told apart.
  public static string Describe(string? value) =>
      value switch {
        null => "[grey](not set)[/]",
        "" => "[grey](empty)[/]",
        _ => Markup.Escape(value),
      };

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    IDictionary<string, string?> config = await _lipClient.ConfigList(settings.Workspace);

    if (settings.Json) {
      _output.Out.WriteLine(JsonSerializer.Serialize(
          config,
          (JsonTypeInfo<IDictionary<string, string?>>)_jsonSerializerOptions.GetTypeInfo(
              typeof(IDictionary<string, string?>))));
    } else {
      Table table = new();
      table.AddColumn("Key");
      table.AddColumn("Value");

      foreach (KeyValuePair<string, string?> kvp in config) {
        table.AddRow(Markup.Escape(kvp.Key), Describe(kvp.Value));
      }

      _output.Console.Write(table);
    }

    return 0;
  }
}
