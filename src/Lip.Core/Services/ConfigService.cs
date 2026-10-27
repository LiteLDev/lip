using System.IO.Abstractions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lip.Core.Entities;
using Lip.Core.Infrastructure;

namespace Lip.Core.Services;

public interface IConfigService {
  Task Delete(string key);
  Task<string> Get(string key);
  Task<T> Get<T>(string key);
  Task<IDictionary<string, string>> List();
  Task Set(string key, string value);
}

public class ConfigService(IFileSystem fileSystem, IUserInteraction userInteraction) : IConfigService {
  private static readonly string _configPath = Path.Join(LipDirectories.Config, "liprc.json");
  private static readonly JsonSerializerOptions _jsonSerializerOptions = new() {
    WriteIndented = true
  };

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task Delete(string key) {
    RuntimeConfig config = await LoadConfig();
    JsonNode json = JsonSerializer.SerializeToNode(config)!;

    if (key == "format_version"
        || key == "format_uuid"
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

    if (key == "format_version"
        || key == "format_uuid"
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
        .Where(kv => kv.Key != "format_version" && kv.Key != "format_uuid")
        .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
  }

  public async Task Set(string key, string value) {
    RuntimeConfig config = await LoadConfig();
    JsonNode json = JsonSerializer.SerializeToNode(config)!;

    if (key == "format_version"
        || key == "format_uuid"
        || !json.AsObject().TryGetPropertyValue(key, out _)) {
      throw new KeyNotFoundException($"Configuration key '{key}' not found.");
    }

    json[key] = value;

    RuntimeConfig newConfig = json.Deserialize<RuntimeConfig>()!;
    await SaveConfig(newConfig);
  }

  private async Task<RuntimeConfig> LoadConfig() {
    if (!_fileSystem.File.Exists(_configPath)) {
      return new();
    }

    try {
      using Stream readStream = _fileSystem.File.OpenRead(_configPath);

      RuntimeConfig config = (await JsonSerializer.DeserializeAsync<RuntimeConfig>(readStream, _jsonSerializerOptions))!;

      return config;
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_configPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return new();
    }
  }

  private async Task SaveConfig(RuntimeConfig config) {
    using Stream writeStream = _fileSystem.CreateFileWithDirectory(_configPath);

    await JsonSerializer.SerializeAsync(writeStream, config, _jsonSerializerOptions);
  }
}
