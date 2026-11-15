using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Flurl;
using Flurl.Http;
using Lip.Core.Git;
using Semver;

namespace Lip.Core.Infrastructure;

/// How lip was installed (protocol/lip-self-update.md, "Terms").
public enum InstallationKind {
  Portable,
  Msi,
}

/// A release of lip: its version and its assets, by name, with their download URLs.
public sealed record LipRelease(SemVersion Version, IReadOnlyDictionary<string, Url> Assets);

/// Why lip self-update failed, other than the network or the file system.
public sealed class SelfUpdateException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// What lip self-update did.
public enum SelfUpdateOutcome {
  /// lip is up to date; nothing changed.
  UpToDate,

  /// --check found a newer release; nothing changed.
  Available,

  /// The installer of the newer release was started.
  InstallerStarted,

  /// The files of the installation directory were replaced.
  Replaced,
}

public sealed record SelfUpdateResult(SelfUpdateOutcome Outcome, SemVersion? Latest);

/// Starts a program elevated, as administrator, without waiting for it.
public interface IElevatedStarter {
  /// Throws when the program does not start, or the user declines the elevation.
  void Start(string fileName, IReadOnlyList<string> arguments);
}

/// Starts a program through the shell with the verb "runas", which asks the user for consent.
public sealed class ShellElevatedStarter : IElevatedStarter {
  // ERROR_CANCELLED: the user declined the elevation.
  private const int _errorCancelled = 1223;

  public void Start(string fileName, IReadOnlyList<string> arguments) {
    ProcessStartInfo info = new(fileName) {
      UseShellExecute = true,
      Verb = "runas",
    };
    foreach (string argument in arguments) {
      info.ArgumentList.Add(argument);
    }

    try {
      using Process? process = Process.Start(info);
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == _errorCancelled) {
      throw new SelfUpdateException(
          $"The installer {fileName} was not started: running it as administrator was declined."
          + " Run 'lip self-update' again and allow it to make changes.", ex);
    }
    catch (Win32Exception ex) {
      throw new SelfUpdateException($"Could not start the installer {fileName}.", ex);
    }
  }
}

/// Where and how lip runs, as lip self-update needs to know.
/// InstallDirectory: the installation directory (protocol/lip-self-update.md, "Terms").
/// Platform: the platform of lip, or null on a system that has none.
/// LinuxArchives: whether the portable archives are .tar.xz rather than .zip.
public sealed record SelfUpdateHost(
    string InstallDirectory,
    string CacheDirectory,
    string? Platform,
    bool IsWindows,
    bool LinuxArchives) {
  /// The host lip runs on.
  public static SelfUpdateHost Current => new(
      AppContext.BaseDirectory,
      LipDirectories.Cache,
      CurrentPlatform(),
      OperatingSystem.IsWindows(),
      OperatingSystem.IsLinux());

  /// The platform of lip: the operating system and the architecture of the process.
  private static string? CurrentPlatform() {
    string? os = OperatingSystem.IsWindows() ? "win"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "osx"
        : null;
    string? architecture = RuntimeInformation.ProcessArchitecture switch {
      Architecture.X64 => "x64",
      Architecture.Arm64 => "arm64",
      _ => null,
    };
    return os is null || architecture is null ? null : $"{os}-{architecture}";
  }
}

/// lip self-update (protocol/lip-self-update.md): finds the latest release of lip on GitHub and
/// installs it, by the installer that installed lip, or by replacing the files of a portable
/// installation directory.
public sealed class SelfUpdater {
  /// The releases of lip (protocol/lip-self-update.md, "Terms").
  public static readonly Url DefaultReleasesUrl =
      "https://api.github.com/repos/LiteLDev/lip/releases?per_page=100";

  public const string InstallFileName = "install.json";

  public const string ChecksumsAssetName = "SHA256SUMS";

  private const string _stagingDirectoryName = "self-update";

  private const string _oldSuffix = ".old";

  private readonly IFileSystem _fileSystem;
  private readonly IFileDownloader _downloader;
  private readonly IElevatedStarter _elevatedStarter;
  private readonly SelfUpdateHost _host;
  private readonly Url? _githubProxy;
  private readonly Url _releasesUrl;
  private readonly Func<Url, Task<string>> _getReleases;
  private readonly Func<string, string, Task> _extractTarXz;

  public SelfUpdater(
      IFileSystem fileSystem,
      IFileDownloader downloader,
      IElevatedStarter elevatedStarter,
      SelfUpdateHost host,
      Url? githubProxy,
      Url? releasesUrl = null,
      Func<Url, Task<string>>? getReleases = null,
      Func<string, string, Task>? extractTarXz = null) {
    _fileSystem = fileSystem;
    _downloader = downloader;
    _elevatedStarter = elevatedStarter;
    _host = host;
    _githubProxy = githubProxy;
    _releasesUrl = releasesUrl ?? DefaultReleasesUrl;
    _getReleases = getReleases ?? GetReleases;
    _extractTarXz = extractTarXz ?? ExtractTarXz;
  }

  /// Checks for, or with checkOnly false installs, the latest release newer than running.
  public async Task<SelfUpdateResult> Run(SemVersion running, bool checkOnly) {
    if (_host.Platform is null) {
      throw new SelfUpdateException(
          $"lip self-update does not support this system ({RuntimeInformation.OSDescription},"
          + $" {RuntimeInformation.ProcessArchitecture}): lip is released only for Windows,"
          + " Linux and macOS on x64 and ARM64.");
    }

    RunTrace.Write("self-update", $"reading the releases at {_releasesUrl}");
    string json = await _getReleases(_releasesUrl);
    LipRelease? latest = FindLatest(ParseReleases(json), running);
    if (latest is null || SemVersion.ComparePrecedence(latest.Version, running) <= 0) {
      return new(SelfUpdateOutcome.UpToDate, latest?.Version);
    }

    if (checkOnly) {
      return new(SelfUpdateOutcome.Available, latest.Version);
    }

    InstallationKind kind = ReadKind(_fileSystem, _host.InstallDirectory);
    RunTrace.Write("self-update", $"updating a {kind} installation to {latest.Version}");
    if (kind != InstallationKind.Portable && !_host.IsWindows) {
      throw new SelfUpdateException(
          $"{InstallFileName} in {_host.InstallDirectory} says that a Windows installer installed"
          + " lip, but lip does not run on Windows. Delete that file to update the files of lip"
          + " in place.");
    }

    string assetName = GetAssetName(kind, latest.Version, _host.Platform, _host.LinuxArchives);
    if (!latest.Assets.TryGetValue(assetName, out Url? assetUrl)) {
      throw new SelfUpdateException(
          $"The release {latest.Version} of lip has no file {assetName}. Try again later, or"
          + " install it from https://github.com/LiteLDev/lip/releases yourself.");
    }
    if (!latest.Assets.TryGetValue(ChecksumsAssetName, out Url? checksumsUrl)) {
      throw new SelfUpdateException(
          $"The release {latest.Version} of lip has no file {ChecksumsAssetName} to verify the"
          + " download with. Try again later, or install it from"
          + " https://github.com/LiteLDev/lip/releases yourself.");
    }

    string staging = _fileSystem.Path.Join(_host.CacheDirectory, _stagingDirectoryName);
    ResetStaging(staging);

    // Directly from GitHub, never through github_proxy: the checksums guard against a proxy that
    // serves something else.
    using MemoryStream checksums = new();
    await _downloader.Download(checksumsUrl, checksums);
    string? expected = FindChecksum(
        System.Text.Encoding.UTF8.GetString(checksums.ToArray()), assetName);

    string assetPath = _fileSystem.Path.Join(staging, assetName);
    string temporary = assetPath + ".tmp";
    await Download(
        assetUrl, url => _downloader.DownloadFile(url, _fileSystem.FileInfo.New(temporary)));
    _fileSystem.File.Move(temporary, assetPath, overwrite: true);
    VerifyChecksum(assetPath, assetName, expected);

    switch (kind) {
      case InstallationKind.Msi:
        _elevatedStarter.Start("msiexec.exe", ["/i", assetPath, "/qn", "/norestart"]);
        return new(SelfUpdateOutcome.InstallerStarted, latest.Version);
      default:
        string extracted = _fileSystem.Path.Join(
            staging, $"lip-{latest.Version}-{_host.Platform}");
        await Extract(assetPath, extracted);
        ReplaceFiles(extracted, _host.InstallDirectory, _host.IsWindows);
        TryDeleteDirectory(staging);
        return new(SelfUpdateOutcome.Replaced, latest.Version);
    }
  }

  /// The releases in the body of the response of the releases URL: the elements that are not
  /// drafts and whose tag is "v" followed by a version; the others are ignored.
  public static IReadOnlyList<LipRelease> ParseReleases(string json) {
    List<LipRelease> releases = [];
    JsonDocument document;
    try {
      document = JsonDocument.Parse(json);
    }
    catch (JsonException ex) {
      throw new SelfUpdateException(
          "GitHub answered the request for the releases of lip with something that is not"
          + " JSON. Try again later.", ex);
    }

    using (document) {
      if (document.RootElement.ValueKind != JsonValueKind.Array) {
        throw new SelfUpdateException(
            "GitHub answered the request for the releases of lip with something that is not a"
            + " list of releases. Try again later.");
      }

      foreach (JsonElement element in document.RootElement.EnumerateArray()) {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("draft", out JsonElement draft)
            || draft.ValueKind != JsonValueKind.False
            || !element.TryGetProperty("tag_name", out JsonElement tag)
            || tag.ValueKind != JsonValueKind.String
            || tag.GetString() is not ['v', .. string versionText]
            || !SemVersion.TryParse(versionText, SemVersionStyles.Strict, out SemVersion? version)) {
          continue;
        }

        Dictionary<string, Url> assets = new(StringComparer.Ordinal);
        if (element.TryGetProperty("assets", out JsonElement assetList)
            && assetList.ValueKind == JsonValueKind.Array) {
          foreach (JsonElement asset in assetList.EnumerateArray()) {
            if (asset.ValueKind == JsonValueKind.Object
                && asset.TryGetProperty("name", out JsonElement name)
                && name.ValueKind == JsonValueKind.String
                && asset.TryGetProperty("browser_download_url", out JsonElement url)
                && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out _)) {
              assets[name.GetString()!] = Url.Parse(url.GetString());
            }
          }
        }
        releases.Add(new(version, assets));
      }
    }
    return releases;
  }

  /// The latest release (protocol/lip-self-update.md, "Terms"): the release of the highest
  /// precedence, skipping pre-release versions unless the running version is one.
  public static LipRelease? FindLatest(IEnumerable<LipRelease> releases, SemVersion running) {
    LipRelease? latest = null;
    foreach (LipRelease release in releases) {
      if (release.Version.IsPrerelease && !running.IsPrerelease) {
        continue;
      }
      if (latest is null || SemVersion.ComparePrecedence(release.Version, latest.Version) > 0) {
        latest = release;
      }
    }
    return latest;
  }

  /// The installation kind, from install.json in the installation directory: portable without
  /// it; a failure when it is not a JSON object whose member kind is "msi".
  public static InstallationKind ReadKind(IFileSystem fileSystem, string installDirectory) {
    string path = fileSystem.Path.Join(installDirectory, InstallFileName);
    if (!fileSystem.File.Exists(path)) {
      return InstallationKind.Portable;
    }

    string invalid = $"{path} is not valid: it must be {{\"kind\": \"msi\"}}. Reinstall lip with its Windows installer, or delete that file"
        + " if lip was not installed by one.";
    try {
      using JsonDocument document = JsonDocument.Parse(fileSystem.File.ReadAllBytes(path));
      if (document.RootElement.ValueKind == JsonValueKind.Object
          && document.RootElement.TryGetProperty("kind", out JsonElement kind)) {
        if (kind.ValueKind == JsonValueKind.String && kind.GetString() == "msi") {
          return InstallationKind.Msi;
        }
      }
    }
    catch (JsonException ex) {
      throw new SelfUpdateException(invalid, ex);
    }
    throw new SelfUpdateException(invalid);
  }

  /// The name of the asset that updates an installation of the kind.
  public static string GetAssetName(
      InstallationKind kind, SemVersion version, string platform, bool linuxArchives) =>
      kind switch {
        InstallationKind.Msi => $"lip-{version}-{platform}.msi",
        _ => $"lip-{version}-{platform}{(linuxArchives ? ".tar.xz" : ".zip")}",
      };

  /// The lowercase hexadecimal SHA-256 of the asset in a SHA256SUMS file, or null when it has no
  /// line for the asset. A line is "&lt;hex&gt;  &lt;name&gt;" or "&lt;hex&gt; *&lt;name&gt;".
  public static string? FindChecksum(string checksums, string assetName) {
    foreach (string rawLine in checksums.Split('\n')) {
      string line = rawLine.TrimEnd('\r');
      if (line.Length < 66 || line[64] != ' ' || line[65] is not (' ' or '*')) {
        continue;
      }
      string hex = line[..64];
      if (line[66..] == assetName && hex.All(Uri.IsHexDigit)) {
        return hex.ToLowerInvariant();
      }
    }
    return null;
  }

  /// Fails, deleting the file, when its SHA-256 is not the expected one.
  private void VerifyChecksum(string path, string assetName, string? expected) {
    string actual;
    using (Stream stream = _fileSystem.File.OpenRead(path)) {
      actual = Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    if (actual == expected) {
      return;
    }

    _fileSystem.File.Delete(path);
    throw new SelfUpdateException(expected is null
        ? $"{ChecksumsAssetName} of the release has no line for {assetName}, so the download"
          + " cannot be verified. Try again later."
        : $"The download of {assetName} is corrupt: its SHA-256 is {actual}, but"
          + $" {ChecksumsAssetName} of the release says {expected}. Try again; if a GitHub proxy"
          + " is set, try without it ('lip config delete github_proxy').");
  }

  /// Deletes what an earlier lip self-update left in the staging directory, as far as it can (an
  /// installer that still runs cannot be deleted), and creates the directory.
  private void ResetStaging(string staging) {
    if (_fileSystem.Directory.Exists(staging)) {
      foreach (string file in _fileSystem.Directory.EnumerateFiles(
          staging, "*", SearchOption.AllDirectories)) {
        TryDeleteFile(file);
      }
      foreach (string directory in _fileSystem.Directory
          .EnumerateDirectories(staging, "*", SearchOption.AllDirectories)
          .OrderByDescending(d => d.Length)) {
        TryDeleteDirectory(directory);
      }
    }
    _fileSystem.Directory.CreateDirectory(staging);
  }

  /// Downloads a GitHub URL through github_proxy, then directly when that fails
  /// (protocol/config.toml.md).
  private async Task Download(Url url, Func<Url, Task> download) {
    if (GithubProxy.Rewrite(_githubProxy, url) is Url proxied) {
      try {
        await download(proxied);
        return;
      }
      catch (Exception ex) when (ex is HttpRequestException or FlurlHttpException
          or UriFormatException) {
        RunTrace.Write("self-update", $"{proxied} failed, trying {url}: {ex.Message}");
      }
    }
    await download(url);
  }

  private async Task Extract(string archive, string destination) {
    if (_fileSystem.Directory.Exists(destination)) {
      _fileSystem.Directory.Delete(destination, recursive: true);
    }
    _fileSystem.Directory.CreateDirectory(destination);

    if (archive.EndsWith(".tar.xz", StringComparison.Ordinal)) {
      await _extractTarXz(archive, destination);
      return;
    }

    string root = _fileSystem.Path.GetFullPath(destination);
    using Stream stream = _fileSystem.File.OpenRead(archive);
    using ZipArchive zip = new(stream, ZipArchiveMode.Read);
    foreach (ZipArchiveEntry entry in zip.Entries) {
      if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) {
        continue;
      }
      string target = _fileSystem.Path.GetFullPath(_fileSystem.Path.Join(root, entry.FullName));
      if (!target.StartsWith(root + _fileSystem.Path.DirectorySeparatorChar,
          StringComparison.Ordinal)) {
        throw new SelfUpdateException(
            $"The archive {archive} holds a file outside its root ({entry.FullName}).");
      }
      _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(target)!);
      using (Stream source = entry.Open())
      using (Stream file = _fileSystem.File.Create(target)) {
        await source.CopyToAsync(file);
      }

      // The permissions the archive records, such as the execute bit of lip on macOS, which a
      // ZIP written on Unix keeps in the high 16 bits of the external attributes.
      int mode = (entry.ExternalAttributes >> 16) & 0x1FF;
      if (!OperatingSystem.IsWindows() && !_host.IsWindows && mode != 0) {
        _fileSystem.File.SetUnixFileMode(target, (UnixFileMode)mode);
      }
    }
  }

  /// Extracts a .tar.xz archive with tar, which the systems that have one read xz with.
  private static async Task ExtractTarXz(string archive, string destination) {
    ProcessStartInfo info = new("tar") { UseShellExecute = false, RedirectStandardError = true };
    foreach (string argument in new[] { "-xJf", archive, "-C", destination }) {
      info.ArgumentList.Add(argument);
    }

    Process process;
    try {
      process = Process.Start(info)
          ?? throw new SelfUpdateException("Could not start tar to extract the update.");
    }
    catch (Win32Exception ex) {
      throw new SelfUpdateException(
          "Could not start tar to extract the update. Install tar and xz, then try again.", ex);
    }

    using (process) {
      string error = await process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync();
      if (process.ExitCode != 0) {
        throw new SelfUpdateException(
            $"tar could not extract {archive} (exit code {process.ExitCode}): {error.Trim()}."
            + " Check that xz is installed, then try again.");
      }
    }
  }

  /// Replaces the files of the installation directory with the files of the extracted archive:
  /// each existing file is renamed to "&lt;name&gt;.old" first, which Windows allows for a
  /// running executable, then the new file is written atomically in its place. On failure, the
  /// renamed files are restored. A .old file that cannot be deleted, the running executable on
  /// Windows, is deleted when lip next starts (DeleteOldExecutable).
  public void ReplaceFiles(string extracted, string installDirectory, bool isWindows) {
    List<(string Target, string Old, bool HadOld, bool Placed)> done = [];
    try {
      foreach (string source in _fileSystem.Directory.EnumerateFiles(
          extracted, "*", SearchOption.AllDirectories)) {
        string relative = _fileSystem.Path.GetRelativePath(extracted, source);
        string target = _fileSystem.Path.Join(installDirectory, relative);
        string old = target + _oldSuffix;
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(target)!);

        bool hadOld = _fileSystem.File.Exists(target);
        if (hadOld) {
          _fileSystem.File.Move(target, old, overwrite: true);
        }
        done.Add((target, old, hadOld, false));

        string temporary = target + ".tmp";
        _fileSystem.File.Copy(source, temporary, overwrite: true);
        _fileSystem.File.Move(temporary, target);
        done[^1] = (target, old, hadOld, true);
      }
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      for (int i = done.Count - 1; i >= 0; i--) {
        (string target, string old, bool hadOld, bool placed) = done[i];
        try {
          // Deleting a file that does not exist does nothing.
          _fileSystem.File.Delete(target + ".tmp");
          if (placed) {
            _fileSystem.File.Delete(target);
          }
          if (hadOld) {
            _fileSystem.File.Move(old, target, overwrite: true);
          }
        }
        catch (Exception restoreException) when (FileSystemExtensions.IsFileError(restoreException)) {
          RunTrace.Write("self-update", $"could not restore {target}: {restoreException.Message}");
        }
      }
      throw FileSystemExtensions.WrapFileError(
          $"Could not replace the files of lip in {installDirectory}; they were restored."
          + (ex is not UnauthorizedAccessException
              ? " Close the other programs that use them, such as another lip, and try again."
              : isWindows
                  ? " If lip is installed below Program Files, run 'lip self-update' as"
                    + " administrator."
                  : " Check that you may write to that directory."),
          ex);
    }

    foreach ((_, string old, bool hadOld, _) in done) {
      if (hadOld) {
        TryDeleteFile(old);
      }
    }
  }

  /// Deletes "&lt;executable&gt;.old", which a portable update on Windows could not delete while
  /// it ran, ignoring a failure: it may still be in use by another lip (protocol/
  /// lip-self-update.md, "Starting").
  public static void DeleteOldExecutable(IFileSystem fileSystem, string? executablePath) {
    if (string.IsNullOrEmpty(executablePath)) {
      return;
    }
    try {
      fileSystem.File.Delete(executablePath + _oldSuffix);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      // In use, or the directory cannot be written to.
    }
  }

  private void TryDeleteFile(string path) {
    try {
      _fileSystem.File.Delete(path);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      RunTrace.Write("self-update", $"could not delete {path}: {ex.Message}");
    }
  }

  private void TryDeleteDirectory(string path) {
    try {
      _fileSystem.Directory.Delete(path, recursive: true);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      RunTrace.Write("self-update", $"could not delete {path}: {ex.Message}");
    }
  }

  /// The body of a GET of the GitHub API, which requires a User-Agent.
  private static async Task<string> GetReleases(Url url) {
    try {
      return await url
          .WithHeader("User-Agent", "lip")
          .WithHeader("Accept", "application/vnd.github+json")
          .GetStringAsync();
    }
    catch (FlurlHttpException ex) when (ex.StatusCode is int status) {
      throw new HttpRequestException(
          $"Could not read the releases of lip from {url}: GitHub answered HTTP {status}."
          + (status is 403 or 429
              ? " GitHub limits how many requests can be sent; wait an hour and try again."
              : " Try again later."),
          ex,
          (System.Net.HttpStatusCode)status);
    }
    catch (FlurlHttpException ex) {
      throw new HttpRequestException(
          $"Could not read the releases of lip from {url}: the connection failed.", ex);
    }
  }
}
