using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lip.Core.Infrastructure;
using Lip.Core.Json;
using Lip.Core.Mcmod;


namespace Lip.Core.Services;

/// Reads and writes the configuration of the two scopes (protocol/config.toml.md): the user
/// configuration, &lt;Config&gt;/config.toml (config.schema.toml), whose keys are github_proxy
/// and language, both without a default; and the workspace configuration, .lip/config.toml in the workspace root
/// (workspace-config.schema.toml), whose only key is restart, default "never". Each is a TOML file
/// (protocol/README.md) with "format" 4 and a mapping of the keys of its scope to their values. A
/// missing, unreadable or invalid file means every key of its scope is absent: it takes its
/// default, or has no value.
///
/// lip v0.x kept the configuration in &lt;Config&gt;/liprc.json. When config.toml is missing and
/// liprc.json is a JSON object whose format_version, when present, is 3 (lip before February 2026
/// wrote none), its configuration is written atomically as config.toml before it is read, and
/// liprc.json is deleted (protocol/lip-compatibility.md, "liprc.json"); a dry run reads it the same
/// way but writes no config.toml and keeps it. Any other liprc.json is kept, and every key has its
/// default.
public class ConfigService(
    IFileSystem fileSystem, IUserInteraction userInteraction, Func<string>? workspaceRoot = null) {
  public const string GithubProxyKey = "github_proxy";

  public const string LanguageKey = "language";

  /// The values of language (protocol/config.toml.md, "User configuration").
  public static readonly IReadOnlyList<string> Languages = ["en", "zh-Hans", "zh-Hant"];

  public const string RestartKey = "restart";

  public const string RestartNever = "never";

  public const string RestartOnFailure = "on-failure";

  private const string _liprcFileName = "liprc.json";

  private const string _liprcFormatVersionKey = "format_version";

  // The config directory of the invocation that the service is created for.
  private readonly string _configPath = Path.Join(LipDirectories.Config, "config.toml");

  private readonly string _liprcPath = Path.Join(LipDirectories.Config, _liprcFileName);

  private static readonly Key _githubProxy = new(
      GithubProxyKey, null, value => value.Length == 0 || IsUri(value),
      "a URL prefix like https://example.com/, or empty to reach GitHub directly",
      "\"https://example.com/\"");

  private static readonly Key _language = new(
      LanguageKey, null, value => Languages.Contains(value),
      "en, zh-Hans or zh-Hant", "\"zh-Hans\"");

  private static readonly Key _restart = new(
      RestartKey, RestartNever, value => value is RestartNever or RestartOnFailure,
      $"{RestartNever} or {RestartOnFailure}", $"\"{RestartOnFailure}\"");

  // The changes of a configuration file by this process, one at a time: each reads the file
  // and writes it whole, and the daemon carries out the commands of several clients at once.
  private static readonly SemaphoreSlim s_changes = new(1, 1);

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IUserInteraction _userInteraction = userInteraction;
  private readonly Func<string> _workspaceRoot =
      workspaceRoot ?? fileSystem.Directory.GetCurrentDirectory;

  /// Removes the key from the file of its scope, so that it takes its default or has no value.
  public async Task Delete(string key, bool workspace = false) {
    Key configKey = CheckKey(key, workspace);
    await s_changes.WaitAsync();
    try {
      Dictionary<string, string> values = await Load(workspace);
      RunTrace.Write("config", $"deleting {key}");
      values.Remove(configKey.Name);
      await Save(workspace, values);
    }
    finally {
      s_changes.Release();
    }
  }

  /// The value of the key, or null when it has no value.
  public async Task<string?> Get(string key, bool workspace = false) {
    Key configKey = CheckKey(key, workspace);
    return ValueOf(configKey, await Load(workspace));
  }

  /// The value of github_proxy: a URI, "" (GitHub is reached directly), or null when it is not
  /// configured. A dry run (protocol/lip.md, "Dry run") reads a liprc.json without migrating it.
  public async Task<string?> GetGithubProxy(bool dryRun = false) {
    return ValueOf(_githubProxy, await Load(workspace: false, dryRun));
  }

  /// The value of language: one of Languages, or null when it has no value. A dry run reads a
  /// liprc.json without migrating it.
  public async Task<string?> GetLanguage(bool dryRun = false) {
    return ValueOf(_language, await Load(workspace: false, dryRun));
  }

  /// The value of restart in the workspace configuration of the current directory.
  public async Task<string> GetRestart() {
    return ValueOf(_restart, await Load(workspace: true))!;
  }

  /// A github_proxy value for the run trace: only its scheme and host, since its user info, path
  /// or query may carry a token.
  public static string DescribeProxy(string? githubProxy) =>
      githubProxy switch {
        null => "(not configured)",
        "" => "(empty: GitHub is reached directly)",
        _ when Uri.TryCreate(githubProxy, UriKind.Absolute, out Uri? uri) && uri.Host != "" =>
            $"{uri.Scheme}://{uri.Host}/...",
        _ => "(a value that is not an absolute URL)",
      };

  /// Every key of the scope with its value, null when it has none.
  public async Task<IDictionary<string, string?>> List(bool workspace = false) {
    Dictionary<string, string> values = await Load(workspace);
    return KeysOf(workspace).ToDictionary(key => key.Name, key => ValueOf(key, values));
  }

  /// Fails, changing nothing, when the value is not valid for the key: a file with it would be
  /// invalid against the schema of the scope.
  public async Task Set(string key, string value, bool workspace = false) {
    Key configKey = CheckKey(key, workspace);
    if (!configKey.IsValid(value)) {
      throw new ArgumentException(
          $"'{value}' is not a valid value of {key}: set {configKey.Expected}"
          + (configKey.Default is null
              ? $", or run 'lip config delete {key}{Flag(workspace)}' to remove it."
              : $", or run 'lip config delete {key}{Flag(workspace)}' to reset it to"
                  + $" {configKey.Default}."));
    }

    await s_changes.WaitAsync();
    try {
      Dictionary<string, string> values = await Load(workspace);
      // The value is not traced: a proxy URL may carry a token.
      RunTrace.Write("config", $"setting {key}");
      values[configKey.Name] = value;
      await Save(workspace, values);
    }
    finally {
      s_changes.Release();
    }
  }

  /// A configuration key: its default, or null when it has none; whether a string is one of its
  /// values; and how to describe its values and give one in a TOML file.
  private sealed record Key(
      string Name, string? Default, Func<string, bool> IsValid, string Expected, string Example);

  private static Key[] KeysOf(bool workspace) => workspace ? [_restart] : [_githubProxy, _language];

  private static string ScopeName(bool workspace) =>
      workspace ? "the workspace configuration" : "the user configuration";

  private static string Flag(bool workspace) => workspace ? " --workspace" : "";

  private static string? ValueOf(Key key, Dictionary<string, string> values) =>
      values.TryGetValue(key.Name, out string? value) ? value : key.Default;

  /// Whether a string is a URI (RFC 3986), as the format "uri" of config.schema.toml requires: a
  /// scheme, then ":" and the rest, of the characters a URI may hold. A Windows or Unix path is
  /// not one.
  private static bool IsUri(string value) =>
      Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9+.-]*:[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]*$")
      && Uri.TryCreate(value, UriKind.Absolute, out _);

  // An unknown key is a mistake of the user, not of lip: ArgumentException, not
  // KeyNotFoundException, which the CLI reports as a bug.
  private static Key CheckKey(string key, bool workspace) {
    if (KeysOf(workspace).FirstOrDefault(k => k.Name == key) is { } configKey) {
      return configKey;
    }

    Key[] known = KeysOf(workspace);
    string keys = (known.Length == 1 ? "whose only key is " : "whose keys are ")
        + string.Join(" and ", known.Select(k => k.Name));
    string hint = KeysOf(!workspace).Any(k => k.Name == key)
        ? workspace
            ? $" {key} is a key of the user configuration: run the command without --workspace."
            : $" {key} is a key of the workspace configuration: add --workspace."
        : "";
    throw new ArgumentException(
        $"'{key}' is not a key of {ScopeName(workspace)}, {keys}.{hint}");
  }

  private string PathOf(bool workspace) =>
      workspace
          ? _fileSystem.Path.GetFullPath(
              _fileSystem.Path.Join(_workspaceRoot(), ".lip", "config.toml"))
          : _configPath;

  /// The keys that the file of the scope gives a value, with their values; none when the file is
  /// missing, unreadable or invalid. A missing user configuration is read from liprc.json.
  private async Task<Dictionary<string, string>> Load(bool workspace, bool dryRun = false) {
    string path = PathOf(workspace);
    if (!_fileSystem.File.Exists(path)) {
      RunTrace.Write("config", $"{path} does not exist");
      return workspace ? [] : await MigrateLiprc(dryRun);
    }

    Key[] keys = KeysOf(workspace);
    string example = string.Join(
        " and ",
        [$"'{PackageFormat.FormatKey} = {PackageFormat.Format}'",
          .. keys.Select(k => $"'{k.Name} = {k.Example}'")]);
    try {
      RunTrace.Write("config", $"reading {path}");
      using JsonDocument document =
          TomlJson.Parse(await _fileSystem.File.ReadAllBytesAsync(path));
      JsonElement root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new InvalidDataException(
            $"It is not a TOML table of configuration keys to their values, like {example} on"
            + " separate lines.");
      }

      if (PackageFileParser.Format(root) != PackageFormat.Format) {
        throw new InvalidDataException(
            $"Its field {PackageFormat.FormatKey} is missing or not {PackageFormat.Format}; it "
            + $"must be '{PackageFormat.FormatKey} = {PackageFormat.Format}'.");
      }

      // Keys that are not configuration keys of the scope are ignored, whatever their value.
      Dictionary<string, string> values = [];
      foreach (Key key in keys) {
        if (!root.TryGetProperty(key.Name, out JsonElement value)) {
          continue;
        }

        if (value.ValueKind != JsonValueKind.String || !key.IsValid(value.GetString()!)) {
          throw new InvalidDataException(
              $"Its field {key.Name} is not valid; it must be {key.Expected}.");
        }

        values[key.Name] = value.GetString()!;
      }

      if (!workspace) {
        RunTrace.Write(
            "config",
            $"{GithubProxyKey} = {DescribeProxy(values.GetValueOrDefault(GithubProxyKey))}");
      }

      return values;
    }
    catch (Exception ex) {
      RunTrace.Write("config", $"cannot read {path}: {ex}");
      string key = keys[0].Name;
      await _userInteraction.PrintError(
          $"Cannot read the configuration file {path}: {ex.Message}");
      await _userInteraction.PrintInfo(
          $"lip continues as if the file were empty. Fix the file so that it is TOML with the"
          + $" lines {example}, or run 'lip config set {key} <VALUE>{Flag(workspace)}' or"
          + $" 'lip config delete {key}{Flag(workspace)}' to rewrite it.");

      return [];
    }
  }

  /// Migrates liprc.json to config.toml (protocol/lip-compatibility.md, "liprc.json"). Returns the
  /// keys it gives a value; none when there is no liprc.json or it is not one that Section 4
  /// reads, which is kept. A dry run only reads it: it writes no config.toml and keeps liprc.json.
  private async Task<Dictionary<string, string>> MigrateLiprc(bool dryRun) {
    if (!_fileSystem.File.Exists(_liprcPath)) {
      RunTrace.Write("config", $"{_liprcPath} does not exist either; using the defaults");
      return [];
    }

    Dictionary<string, string> values = [];
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

      if (root.TryGetProperty(GithubProxyKey, out JsonElement value)
          && value.ValueKind == JsonValueKind.String) {
        values[GithubProxyKey] = value.GetString()!;
      }
    }
    catch (Exception ex) {
      RunTrace.Write("config", $"cannot read {_liprcPath}: {ex}");
      await _userInteraction.PrintError(
          $"Cannot read the lip v0.x configuration file {_liprcPath}: {ex.Message}");
      await _userInteraction.PrintInfo(
          $"lip continues with the default configuration (no {GithubProxyKey}) and keeps "
          + $"{_liprcPath} as it is. Run 'lip config set {GithubProxyKey} <URL>' to write "
          + $"{_configPath}, or delete {_liprcPath} if you no longer need it.");

      return [];
    }

    RunTrace.Write(
        "config",
        $"{GithubProxyKey} = {DescribeProxy(values.GetValueOrDefault(GithubProxyKey))}");
    if (dryRun) {
      RunTrace.Write("config", $"dry run: keeping {_liprcPath} and writing no {_configPath}");
      return values;
    }

    await Save(workspace: false, values);
    RunTrace.Write("config", $"deleting {_liprcPath}");
    _fileSystem.File.Delete(_liprcPath);
    await _userInteraction.PrintInfo(
        $"Converted the lip v0.x configuration file {_liprcPath} to {_configPath}.");

    return values;
  }

  /// Writes the file of the scope atomically (protocol/config.toml.md, protocol/README.md): with
  /// "format", without the keys at their default; the whole content goes to a uniquely named .tmp
  /// file in the same directory, which is then renamed over the file. Writing the workspace
  /// configuration creates .lip, hidden on Windows (protocol/lip-workspace.md, "The .lip
  /// directory"), when it is missing.
  private async Task Save(bool workspace, Dictionary<string, string> values) {
    string path = PathOf(workspace);
    string directory = _fileSystem.Path.GetDirectoryName(path)!;
    JsonObject document = new() { [PackageFormat.FormatKey] = PackageFormat.Format };
    foreach (Key key in KeysOf(workspace)) {
      if (values.TryGetValue(key.Name, out string? value) && value != key.Default) {
        document[key.Name] = value;
      }
    }

    string tempPath = _fileSystem.Path.Join(directory, $"{Guid.NewGuid():N}.tmp");
    try {
      bool created = !_fileSystem.Directory.Exists(directory);
      _fileSystem.Directory.CreateDirectory(directory);
      if (workspace && created) {
        _fileSystem.SetHidden(directory, true);
      }

      RunTrace.Write("config", $"writing {tempPath}");
      using (Stream writeStream = _fileSystem.File.Create(tempPath))
      using (StreamWriter writer = new(writeStream)) {
        await writer.WriteAsync(TomlJson.Serialize(document));
      }

      RunTrace.Write("config", $"renaming {tempPath} to {path}");
      _fileSystem.File.Move(tempPath, path, overwrite: true);
    }
    catch (IOException ex) {
      throw new IOException(
          $"Cannot write the configuration file {path}; it was not changed.", ex);
    }
    catch (UnauthorizedAccessException ex) {
      throw new UnauthorizedAccessException(
          $"lip is not allowed to write the configuration file {path}; it was not changed.", ex);
    }
    finally {
      if (_fileSystem.File.Exists(tempPath)) {
        _fileSystem.File.Delete(tempPath);
      }
    }
  }
}
