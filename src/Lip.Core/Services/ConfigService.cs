using System.IO.Abstractions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;
using YamlDotNet.Serialization;

namespace Lip.Core.Services;

public interface IConfigService {
  Task Delete(string key);
  Task<string> Get(string key);
  Task<T> Get<T>(string key);
  Task<IDictionary<string, string>> List();
  Task Set(string key, string value);
}

/// Reads and writes the user configuration, &lt;Config&gt;/config.yaml: a mapping of the
/// configuration keys to their values. An absent key has its default value.
///
/// Earlier versions kept the configuration in &lt;Config&gt;/liprc.json. When config.yaml is
/// absent and liprc.json exists, it is converted to config.yaml and deleted.
public class ConfigService(IFileSystem fileSystem, IUserInteraction userInteraction) : IConfigService {
  private const string _formatVersionKey = "format_version";
  private const string _formatUuidKey = "format_uuid";

  private static readonly string _configPath = Path.Join(LipDirectories.Config, "config.yaml");
  private static readonly string _legacyConfigPath = Path.Join(LipDirectories.Config, "liprc.json");

  private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();
  private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
      .WithQuotingNecessaryStrings()
      .Build();

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task Delete(string key) {
    RuntimeConfig config = await LoadConfig();
    JsonNode json = JsonSerializer.SerializeToNode(config)!;

    if (key == _formatVersionKey
        || key == _formatUuidKey
        || !json.AsObject().TryGetPropertyValue(key, out _)) {
      throw new KeyNotFoundException($"Configuration key '{key}' not found.");
    }

    json.AsObject().Remove(key);

    RuntimeConfig newConfig = json.Deserialize<RuntimeConfig>()!;
    await SaveConfig(newConfig);
  }

  public async Task<string> Get(string key) {
    RuntimeConfig config = await LoadConfig();
    JsonNode json = JsonSerializer.SerializeToNode(config)!;

    if (key == _formatVersionKey
        || key == _formatUuidKey
        || !json.AsObject().TryGetPropertyValue(key, out JsonNode? value)) {
      throw new KeyNotFoundException($"Configuration key '{key}' not found.");
    }

    return value?.ToString() ?? "";
  }

  public async Task<T> Get<T>(string key) {
    RuntimeConfig config = await LoadConfig();

    // Use reflection to find the property with the matching JsonPropertyName.
    PropertyInfo prop = typeof(RuntimeConfig).GetProperties()
        .Where(p => p.Name != "FormatVersion" && p.Name != "FormatUuid")
        .Where(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
        .FirstOrDefault(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name == key)
        ?? throw new KeyNotFoundException($"Configuration key '{key}' not found.");

    return (T)prop.GetValue(config)!;
  }

  public async Task<IDictionary<string, string>> List() {
    RuntimeConfig config = await LoadConfig();
    JsonNode json = JsonSerializer.SerializeToNode(config)!;

    return json.AsObject()
        .Where(kv => kv.Key != _formatVersionKey && kv.Key != _formatUuidKey)
        .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
  }

  public async Task Set(string key, string value) {
    RuntimeConfig config = await LoadConfig();
    JsonNode json = JsonSerializer.SerializeToNode(config)!;

    if (key == _formatVersionKey
        || key == _formatUuidKey
        || !json.AsObject().TryGetPropertyValue(key, out _)) {
      throw new KeyNotFoundException($"Configuration key '{key}' not found.");
    }

    json[key] = value;

    RuntimeConfig newConfig = json.Deserialize<RuntimeConfig>()!;
    await SaveConfig(newConfig);
  }

  private async Task<RuntimeConfig> LoadConfig() {
    if (!_fileSystem.File.Exists(_configPath)) {
      return await ConvertLegacyConfig() ?? new();
    }

    try {
      string yaml = await _fileSystem.File.ReadAllTextAsync(_configPath);
      Dictionary<string, string?> values =
          _yamlDeserializer.Deserialize<Dictionary<string, string?>?>(yaml) ?? [];

      // The format fields belong to RuntimeConfig, not to the file.
      RuntimeConfig defaults = new();
      JsonObject json = new() {
        [_formatVersionKey] = defaults.FormatVersion,
        [_formatUuidKey] = defaults.FormatUuid,
      };
      foreach ((string key, string? value) in values) {
        if (key is not (_formatVersionKey or _formatUuidKey)) {
          json[key] = value;
        }
      }

      return json.Deserialize<RuntimeConfig>()!;
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_configPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return new();
    }
  }

  /// Converts liprc.json to config.yaml and deletes it. Returns null when there is no liprc.json
  /// or it cannot be read; an unreadable liprc.json is kept.
  private async Task<RuntimeConfig?> ConvertLegacyConfig() {
    if (!_fileSystem.File.Exists(_legacyConfigPath)) {
      return null;
    }

    RuntimeConfig config;
    try {
      using Stream readStream = _fileSystem.File.OpenRead(_legacyConfigPath);
      config = (await JsonSerializer.DeserializeAsync<RuntimeConfig>(readStream))!;
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_legacyConfigPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return null;
    }

    await SaveConfig(config);
    _fileSystem.File.Delete(_legacyConfigPath);
    await _userInteraction.PrintInfo($"Converted '{_legacyConfigPath}' to '{_configPath}'.");

    return config;
  }

  private async Task SaveConfig(RuntimeConfig config) {
    // Keys at their default (null) value are left out.
    Dictionary<string, string> values = JsonSerializer.SerializeToNode(config)!.AsObject()
        .Where(kv => kv.Key is not (_formatVersionKey or _formatUuidKey) && kv.Value is not null)
        .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString());

    using Stream writeStream = _fileSystem.CreateFileWithDirectory(_configPath);
    using StreamWriter writer = new(writeStream);

    await writer.WriteAsync(_yamlSerializer.Serialize(values));
  }
}
