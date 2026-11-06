using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;


namespace Lip.Core.Services;

/// Reads and writes the user configuration, &lt;Config&gt;/config.yaml: a YAML file
/// (protocol/README.md) with "format": 4 and a mapping of the configuration keys to their values
/// (protocol/userspace.md §2, config.schema.yaml). The only configuration key is github_proxy; an
/// absent key has its default value, none. A missing, unreadable or invalid config.yaml means
/// every key has its default.
///
/// lip v0.x kept the configuration in &lt;Config&gt;/liprc.json. When config.yaml is missing and
/// liprc.json is a JSON object with format_version 3, its configuration is written atomically as
/// config.yaml before it is read, and liprc.json is deleted (protocol/compatibility.md §4). Any
/// other liprc.json is kept, and every key has its default.
public class ConfigService(IFileSystem fileSystem, IUserInteraction userInteraction) {
  private const string _githubProxyKey = "github_proxy";

  private const string _liprcFileName = "liprc.json";

  private const string _liprcFormatVersionKey = "format_version";

  private static readonly string _configPath = Path.Join(LipDirectories.Config, "config.yaml");

  private static readonly string _liprcPath = Path.Join(LipDirectories.Config, _liprcFileName);

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
      return await MigrateLiprc();
    }

    try {
      using JsonDocument document = YamlJson.Parse(await _fileSystem.File.ReadAllBytesAsync(_configPath));
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new InvalidDataException("It is not a mapping.");
      }

      if (PackageFileParser.Format(root) != PackageFormat.Format) {
        throw new InvalidDataException(
            $"Its '{PackageFormat.FormatKey}' is not {PackageFormat.Format}.");
      }

      // Keys that are not configuration keys are ignored, whatever their value.
      if (!root.TryGetProperty(_githubProxyKey, out JsonElement value)) {
        return null;
      }

      return value.ValueKind == JsonValueKind.String
          ? value.GetString()
          : throw new InvalidDataException($"The value of '{_githubProxyKey}' is not a string.");
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_configPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return null;
    }
  }

  /// Migrates liprc.json to config.yaml (protocol/compatibility.md §4). Returns the github_proxy
  /// it holds; null when it holds none, or when there is no liprc.json or it is not one that
  /// Section 4 reads, which is kept.
  private async Task<string?> MigrateLiprc() {
    if (!_fileSystem.File.Exists(_liprcPath)) {
      return null;
    }

    string? githubProxy;
    try {
      using JsonDocument document = JsonDocument.Parse(
          await _fileSystem.File.ReadAllBytesAsync(_liprcPath));
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object
          || PackageFileParser.IntegerMember(root, _liprcFormatVersionKey) != 3) {
        throw new InvalidDataException(
            $"It is not a JSON object with {_liprcFormatVersionKey} 3.");
      }

      githubProxy = root.TryGetProperty(_githubProxyKey, out JsonElement value)
          && value.ValueKind == JsonValueKind.String
          ? value.GetString()
          : null;
    }
    catch (Exception ex) {
      await _userInteraction.PrintError($"Failed to load runtime configuration from '{_liprcPath}': {ex.Message}");
      await _userInteraction.PrintInfo("Using default runtime configuration.");

      return null;
    }

    await SaveConfig(githubProxy);
    _fileSystem.File.Delete(_liprcPath);
    await _userInteraction.PrintInfo($"Migrated '{_liprcPath}' to '{_configPath}'.");

    return githubProxy;
  }

  /// Writes config.yaml atomically (protocol/userspace.md §2, protocol/README.md): with "format",
  /// without the keys at their default; the whole content goes to a uniquely named .tmp file in
  /// the config directory, which is then renamed over config.yaml.
  private async Task SaveConfig(string? githubProxy) {
    JsonObject values = new() { [PackageFormat.FormatKey] = PackageFormat.Format };
    if (githubProxy is not null) {
      values[_githubProxyKey] = githubProxy;
    }

    string tempPath = Path.Join(LipDirectories.Config, $"{Guid.NewGuid():N}.tmp");
    try {
      using (Stream writeStream = _fileSystem.CreateFileWithDirectory(tempPath))
      using (StreamWriter writer = new(writeStream)) {
        await writer.WriteAsync(YamlJson.Serialize(values));
      }

      _fileSystem.File.Move(tempPath, _configPath, overwrite: true);
    }
    finally {
      if (_fileSystem.File.Exists(tempPath)) {
        _fileSystem.File.Delete(tempPath);
      }
    }
  }
}
