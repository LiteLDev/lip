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
/// liprc.json is a JSON object whose format_version, when present, is 3 (lip before February 2026
/// wrote none), its configuration is written atomically as config.yaml before it is read, and
/// liprc.json is deleted (protocol/compatibility.md §4). Any other liprc.json is kept, and every
/// key has its default.
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
    RunTrace.Write("config", $"resetting {key} to its default");
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

  /// A github_proxy value for the run trace: only its scheme and host, since its user info, path
  /// or query may carry a token.
  public static string DescribeProxy(string? githubProxy) =>
      githubProxy is null
          ? "(default: none)"
          : Uri.TryCreate(githubProxy, UriKind.Absolute, out Uri? uri) && uri.Host != ""
              ? $"{uri.Scheme}://{uri.Host}/..."
              : "(a value that is not an absolute URL)";

  public async Task<IDictionary<string, string>> List() {
    return new Dictionary<string, string> { [_githubProxyKey] = await LoadGithubProxy() ?? "" };
  }

  public async Task Set(string key, string value) {
    CheckKey(key);
    await LoadGithubProxy();
    // The value is not traced: a proxy URL may carry a token.
    RunTrace.Write("config", $"setting {key}");
    await SaveConfig(value);
  }

  // An unknown key is a mistake of the user, not of lip: ArgumentException, not
  // KeyNotFoundException, which the CLI reports as a bug.
  private static void CheckKey(string key) {
    if (key != _githubProxyKey) {
      throw new ArgumentException(
          $"'{key}' is not a configuration key. The only configuration key is "
          + $"{_githubProxyKey}, a URL prefix through which lip reaches GitHub.");
    }
  }

  private async Task<string?> LoadGithubProxy() {
    if (!_fileSystem.File.Exists(_configPath)) {
      RunTrace.Write("config", $"{_configPath} does not exist");
      return await MigrateLiprc();
    }

    try {
      RunTrace.Write("config", $"reading {_configPath}");
      using JsonDocument document =
          YamlJson.Parse(await _fileSystem.File.ReadAllBytesAsync(_configPath));
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new InvalidDataException(
            $"It is not a YAML mapping of configuration keys to their values, like "
            + $"'{PackageFormat.FormatKey}: {PackageFormat.Format}' and "
            + $"'{_githubProxyKey}: https://example.com/' on separate lines.");
      }

      if (PackageFileParser.Format(root) != PackageFormat.Format) {
        throw new InvalidDataException(
            $"Its field {PackageFormat.FormatKey} is missing or not {PackageFormat.Format}; it "
            + $"must be '{PackageFormat.FormatKey}: {PackageFormat.Format}'.");
      }

      // Keys that are not configuration keys are ignored, whatever their value.
      if (!root.TryGetProperty(_githubProxyKey, out JsonElement value)) {
        return null;
      }

      string? githubProxy = value.ValueKind == JsonValueKind.String
          ? value.GetString()
          : throw new InvalidDataException(
              $"Its field {_githubProxyKey} is not a single text value; it must be a URL "
              + "prefix like https://example.com/.");
      RunTrace.Write("config", $"{_githubProxyKey} = {DescribeProxy(githubProxy)}");
      return githubProxy;
    }
    catch (Exception ex) {
      RunTrace.Write("config", $"cannot read {_configPath}: {ex}");
      await _userInteraction.PrintError(
          $"Cannot read the configuration file {_configPath}: {ex.Message}");
      await _userInteraction.PrintInfo(
          $"lip continues with the default configuration (no {_githubProxyKey}). Fix the file "
          + $"so that it is YAML with the lines "
          + $"'{PackageFormat.FormatKey}: {PackageFormat.Format}' and "
          + $"'{_githubProxyKey}: https://example.com/', or run "
          + $"'lip config set {_githubProxyKey} <URL>' or 'lip config delete {_githubProxyKey}' "
          + "to rewrite it.");

      return null;
    }
  }

  /// Migrates liprc.json to config.yaml (protocol/compatibility.md §4). Returns the github_proxy
  /// it holds; null when it holds none, or when there is no liprc.json or it is not one that
  /// Section 4 reads, which is kept.
  private async Task<string?> MigrateLiprc() {
    if (!_fileSystem.File.Exists(_liprcPath)) {
      RunTrace.Write("config", $"{_liprcPath} does not exist either; using the defaults");
      return null;
    }

    string? githubProxy;
    try {
      RunTrace.Write("config", $"reading the lip v0.x configuration file {_liprcPath}");
      using JsonDocument document = JsonDocument.Parse(
          await _fileSystem.File.ReadAllBytesAsync(_liprcPath));
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object
          || (root.TryGetProperty(_liprcFormatVersionKey, out _)
              && PackageFileParser.IntegerMember(root, _liprcFormatVersionKey) != 3)) {
        throw new InvalidDataException(
            $"It is not a JSON object whose field {_liprcFormatVersionKey}, when present, is 3, "
            + "the only lip v0.x configuration that lip can convert.");
      }

      githubProxy = root.TryGetProperty(_githubProxyKey, out JsonElement value)
          && value.ValueKind == JsonValueKind.String
          ? value.GetString()
          : null;
    }
    catch (Exception ex) {
      RunTrace.Write("config", $"cannot read {_liprcPath}: {ex}");
      await _userInteraction.PrintError(
          $"Cannot read the lip v0.x configuration file {_liprcPath}: {ex.Message}");
      await _userInteraction.PrintInfo(
          $"lip continues with the default configuration (no {_githubProxyKey}) and keeps "
          + $"{_liprcPath} as it is. Run 'lip config set {_githubProxyKey} <URL>' to write "
          + $"{_configPath}, or delete {_liprcPath} if you no longer need it.");

      return null;
    }

    RunTrace.Write("config", $"{_githubProxyKey} = {DescribeProxy(githubProxy)}");
    await SaveConfig(githubProxy);
    RunTrace.Write("config", $"deleting {_liprcPath}");
    _fileSystem.File.Delete(_liprcPath);
    await _userInteraction.PrintInfo(
        $"Converted the lip v0.x configuration file {_liprcPath} to {_configPath}.");

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
      RunTrace.Write("config", $"writing {tempPath}");
      using (Stream writeStream = _fileSystem.CreateFileWithDirectory(tempPath))
      using (StreamWriter writer = new(writeStream)) {
        await writer.WriteAsync(YamlJson.Serialize(values));
      }

      RunTrace.Write("config", $"renaming {tempPath} to {_configPath}");
      _fileSystem.File.Move(tempPath, _configPath, overwrite: true);
    }
    catch (IOException ex) {
      throw new IOException(
          $"Cannot write the configuration file {_configPath}; it was not changed.", ex);
    }
    catch (UnauthorizedAccessException ex) {
      throw new UnauthorizedAccessException(
          $"lip is not allowed to write the configuration file {_configPath}; it was not "
          + "changed.", ex);
    }
    finally {
      if (_fileSystem.File.Exists(tempPath)) {
        _fileSystem.File.Delete(tempPath);
      }
    }
  }
}
