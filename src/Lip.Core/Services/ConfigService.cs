using System.IO.Abstractions;
using System.Text.Json;
using Lip.Core.Infrastructure;
using YamlDotNet.Serialization;

namespace Lip.Core.Services;

/// Reads and writes the user configuration, &lt;Config&gt;/config.yaml: a mapping of the
/// configuration keys to their values (protocol/userspace.md §2). The only configuration key is
/// github_proxy; an absent key has its default value, none.
///
/// Earlier versions kept the configuration in &lt;Config&gt;/liprc.json. When config.yaml is
/// absent and liprc.json exists, it is converted to config.yaml and deleted.
public class ConfigService(IFileSystem fileSystem, IUserInteraction userInteraction) {
  private const string _githubProxyKey = "github_proxy";

  private static readonly string _configPath = Path.Join(LipDirectories.Config, "config.yaml");
  private static readonly string _legacyConfigPath = Path.Join(LipDirectories.Config, "liprc.json");

  private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();
  private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
      .WithQuotingNecessaryStrings()
      .Build();

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task Delete(string key) {
    CheckKey(key);
    await LoadGithubProxy();
    await SaveConfig(null);
  }

  public async Task<string> Get(string key) {
    CheckKey(key);
    return await LoadGithubProxy() ?? "";
  }

  /// The value of github_proxy, or null when it has its default.
  public async Task<string?> GetGithubProxy() {
    return await LoadGithubProxy();
  }

  public async Task<IDictionary<string, string>> List() {
    return new Dictionary<string, string> { [_githubProxyKey] = await LoadGithubProxy() ?? "" };
  }

  public async Task Set(string key, string value) {
    CheckKey(key);
    await LoadGithubProxy();
    await SaveConfig(value);
  }

  private static void CheckKey(string key) {
    if (key != _githubProxyKey) {
      throw new KeyNotFoundException($"Configuration key '{key}' not found.");
    }
  }

  private async Task<string?> LoadGithubProxy() {
    if (!_fileSystem.File.Exists(_configPath)) {
      return await ConvertLegacyConfig();
    }

    try {
      string yaml = await _fileSystem.File.ReadAllTextAsync(_configPath);

      // Keys that are not configuration keys are ignored, whatever their value.
      Dictionary<string, object?> values =
          _yamlDeserializer.Deserialize<Dictionary<string, object?>?>(yaml) ?? [];

      return values.GetValueOrDefault(_githubProxyKey) switch {
        null => null,
        string value => value,
        _ => throw new InvalidDataException($"The value of '{_githubProxyKey}' is not a string."),
      };
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_configPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return null;
    }
  }

  /// Converts liprc.json to config.yaml and deletes it. Returns the github_proxy it holds; null
  /// when it holds none, there is no liprc.json or it cannot be read. An unreadable liprc.json is
  /// kept.
  private async Task<string?> ConvertLegacyConfig() {
    if (!_fileSystem.File.Exists(_legacyConfigPath)) {
      return null;
    }

    string? githubProxy;
    try {
      using Stream readStream = _fileSystem.File.OpenRead(_legacyConfigPath);
      using JsonDocument document = await JsonDocument.ParseAsync(readStream);

      githubProxy = document.RootElement.TryGetProperty(_githubProxyKey, out JsonElement value)
          ? value.GetString()
          : null;
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_legacyConfigPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return null;
    }

    await SaveConfig(githubProxy);
    _fileSystem.File.Delete(_legacyConfigPath);
    await _userInteraction.PrintInfo($"Converted '{_legacyConfigPath}' to '{_configPath}'.");

    return githubProxy;
  }

  private async Task SaveConfig(string? githubProxy) {
    // Keys at their default (none) are left out.
    Dictionary<string, string> values = [];
    if (githubProxy is not null) {
      values[_githubProxyKey] = githubProxy;
    }

    using Stream writeStream = _fileSystem.CreateFileWithDirectory(_configPath);
    using StreamWriter writer = new(writeStream);

    await writer.WriteAsync(_yamlSerializer.Serialize(values));
  }
}
