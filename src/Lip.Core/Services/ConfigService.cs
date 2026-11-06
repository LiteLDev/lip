using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using YamlDotNet.Serialization;

namespace Lip.Core.Services;

/// Reads and writes the user configuration, &lt;Config&gt;/config.yaml: a mapping of the
/// configuration keys to their values (protocol/userspace.md §2). The only configuration key is
/// github_proxy; an absent key has its default value, none. A missing config.yaml means every key
/// has its default.
public class ConfigService(IFileSystem fileSystem, IUserInteraction userInteraction) {
  private const string _githubProxyKey = "github_proxy";

  private static readonly string _configPath = Path.Join(LipDirectories.Config, "config.yaml");

  private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();
  private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
      .WithQuotingNecessaryStrings()
      .Build();

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public async Task Delete(string key) {
    CheckKey(key);
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
    await SaveConfig(value);
  }

  private static void CheckKey(string key) {
    if (key != _githubProxyKey) {
      throw new KeyNotFoundException($"Configuration key '{key}' not found.");
    }
  }

  private async Task<string?> LoadGithubProxy() {
    if (!_fileSystem.File.Exists(_configPath)) {
      return null;
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

  /// Writes config.yaml atomically (protocol/userspace.md §2): the whole content goes to a
  /// uniquely named file in the config directory, which is then renamed over config.yaml.
  private async Task SaveConfig(string? githubProxy) {
    // Keys at their default (none) are left out.
    Dictionary<string, string> values = [];
    if (githubProxy is not null) {
      values[_githubProxyKey] = githubProxy;
    }

    string tempPath = Path.Join(LipDirectories.Config, $"{Guid.NewGuid():N}.tmp");
    try {
      using (Stream writeStream = _fileSystem.CreateFileWithDirectory(tempPath))
      using (StreamWriter writer = new(writeStream)) {
        await writer.WriteAsync(_yamlSerializer.Serialize(values));
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
