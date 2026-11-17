using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using SharpCompress.Compressors.Xz;

namespace Lip.Core.Infrastructure;

/// The operating system lip runs on.
public enum HostOs {
  Windows,
  Linux,
  MacOS,
  Other,
}

/// The operating system lip runs on, and the architecture of the system (not of the process,
/// which may be emulated).
public sealed record SystemInfo(HostOs Os, Architecture Architecture) {
  public static SystemInfo Current { get; } = new(
      OperatingSystem.IsWindows() ? HostOs.Windows
          : OperatingSystem.IsLinux() ? HostOs.Linux
          : OperatingSystem.IsMacOS() ? HostOs.MacOS
          : HostOs.Other,
      RuntimeInformation.OSArchitecture);

  public bool IsWindows => Os == HostOs.Windows;

  /// The system for messages, such as "Linux on Arm64".
  public string Describe() => $"{Os switch {
    HostOs.MacOS => "macOS",
    HostOs.Other => RuntimeInformation.OSDescription,
    _ => Os.ToString(),
  }} on {Architecture}";
}

/// A file lip downloads to install a system dependency: its HTTPS URL, and the lowercase
/// hexadecimal SHA-256 of its content (protocol/lip-workspace.md, "System dependencies").
public sealed record PinnedFile(string Url, string Sha256);

/// A build of Wine that lip installs: Name, the directory below wine/ in the data directory it is
/// extracted into; Version, for messages; Archive, a .tar.xz; Executable, the path of the wine
/// program in the archive, '/'-separated.
public sealed record WineBuild(string Name, string Version, PinnedFile Archive, string Executable);

/// The files lip downloads to install the system dependencies (protocol/lip-workspace.md, "System
/// dependencies"): the Wine of lip for Linux on x64 and for macOS, and the installer of the Visual
/// C++ runtime. Pinned holds those of this version of lip.
public sealed record SystemDependencyFiles(
    WineBuild LinuxX64Wine, WineBuild MacOsWine, PinnedFile VcRedistX64) {
  public static SystemDependencyFiles Pinned { get; } = new(
      PinnedSystemDependencies.LinuxX64Wine,
      PinnedSystemDependencies.MacOsWine,
      PinnedSystemDependencies.VcRedistX64);

  /// The Wine of lip for the system, or null when the system has none: Linux on x64, and macOS
  /// on x64 and on arm64, through Rosetta 2.
  public WineBuild? WineFor(SystemInfo system) => system switch {
    { Os: HostOs.Linux, Architecture: Architecture.X64 } => LinuxX64Wine,
    { Os: HostOs.MacOS, Architecture: Architecture.X64 or Architecture.Arm64 } => MacOsWine,
    _ => null,
  };
}

/// The system dependencies this version of lip installs.
public static class PinnedSystemDependencies {
  /// Wine for Linux on x64, built by Kron4ek (github.com/Kron4ek/Wine-Builds): a WoW64 build,
  /// which runs 32-bit Windows programs such as installers without 32-bit libraries of Linux.
  public static readonly WineBuild LinuxX64Wine = new(
      "wine-11.21-amd64-wow64",
      "11.21",
      new PinnedFile(
          "https://github.com/Kron4ek/Wine-Builds/releases/download/11.21/"
              + "wine-11.21-amd64-wow64.tar.xz",
          "33f3c5b56b1deb8f6725539ea006f27b5d781b3b4ae6a8a1f492d8af4fb9dc83"),
      "wine-11.21-amd64-wow64/bin/wine");

  /// Wine for macOS on x64, built by Gcenx (github.com/Gcenx/macOS_Wine_builds); Rosetta 2 runs it
  /// on arm64. Wine 11.0 does not make its prefix on macOS for x64; wine-devel 11.21 does.
  public static readonly WineBuild MacOsWine = new(
      "wine-devel-11.21-osx64",
      "11.21",
      new PinnedFile(
          "https://github.com/Gcenx/macOS_Wine_builds/releases/download/11.21/"
              + "wine-devel-11.21-osx64.tar.xz",
          "9909b486657e10ab66c7279371ae1f990345b596b3b1b3a0cf21f28af24fa0ce"),
      "Wine Devel.app/Contents/Resources/wine/bin/wine");

  /// vc_redist.x64.exe of the Microsoft Visual C++ 2015–2022 Redistributable 14.44, the file that
  /// https://aka.ms/vs/17/release/vc_redist.x64.exe led to; Microsoft keeps it at this URL.
  public static readonly PinnedFile VcRedistX64 = new(
      "https://download.visualstudio.microsoft.com/download/pr/"
          + "e9604e21-4b79-4096-a4b3-1b63d4b55b4d/"
          + "CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B/VC_redist.x64.exe",
      "cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b");
}

/// Where the Wine of lip is in the data directory: wine/&lt;name&gt;/, which appears only once the
/// build is extracted completely (protocol/lip-workspace.md, "System dependencies").
public static class ManagedWine {
  /// The directory of the Wine builds in the data directory.
  public static string Directory(string dataDirectory) => Path.Join(dataDirectory, "wine");

  /// The directory the build is extracted into.
  public static string BuildDirectory(string dataDirectory, WineBuild build) =>
      Path.Join(Directory(dataDirectory), build.Name);

  /// The wine program of the build.
  public static string Executable(string dataDirectory, WineBuild build) =>
      Path.Join(BuildDirectory(dataDirectory, build), build.Executable);

  /// The wine program of the Wine of lip for the system, or null when it has none; on Windows,
  /// which runs Windows programs itself, null.
  public static string? Executable(string dataDirectory, SystemInfo system) =>
      system.IsWindows || SystemDependencyFiles.Pinned.WineFor(system) is not WineBuild build
          ? null
          : Executable(dataDirectory, build);
}

/// The result of running a program: its exit code and its standard output.
public sealed record ProgramResult(int ExitCode, string Output);

/// What preparing the system asks of the system lip runs on, so that tests can stand in for it.
public interface ISystemProbe {
  SystemInfo System { get; }

  /// Whether Rosetta 2 is installed (macOS only).
  bool IsRosettaInstalled();

  /// Whether the Visual C++ runtime is installed in the registry of Windows (Windows only).
  bool IsVcRuntimeInstalled();

  /// Whether the dynamic linker of the system loads the shared library of the name, as Wine
  /// loads it (Linux and macOS only).
  bool CanLoadLibrary(string name);

  /// The content of the os-release file of the system, or null when it has none (Linux only).
  string? ReadOsRelease();

  /// Runs the program, with its standard input empty, and returns its exit code and standard
  /// output; its standard error goes to the trace. Throws when it cannot be started.
  Task<ProgramResult> Run(ProgramStartInfo start, CancellationToken cancellationToken);

  /// Runs the program as an administrator (Windows only), which may ask the user to allow it,
  /// and returns its exit code. Throws OperationCanceledException when the user does not allow
  /// it, and other exceptions when it cannot be started.
  Task<int> RunAsAdministrator(
      string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// Prepares the system to run the programs of a workspace (protocol/lip-workspace.md, "System
/// dependencies").
public interface ISystemPreparer {
  /// Prepares the system for the workspace whose root is workspaceRoot and whose environment has
  /// the platform; does nothing for a platform that is not a Windows platform. Throws when a
  /// system dependency cannot be installed.
  Task Prepare(string platform, string workspaceRoot, CancellationToken cancellationToken);
}

/// The system lip runs on.
public sealed class SystemProbe : ISystemProbe {
  private const string VcRuntimeKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

  // ERROR_CANCELLED: the user did not allow the program to run as an administrator.
  private const int ErrorCancelled = 1223;

  public SystemInfo System => SystemInfo.Current;

  public bool IsRosettaInstalled() =>
      File.Exists("/Library/Apple/usr/libexec/oah/libRosettaRuntime");

  public bool IsVcRuntimeInstalled() {
    if (!OperatingSystem.IsWindows()) {
      return false;
    }

    foreach (RegistryView view in new[] {
      RegistryView.Registry64, RegistryView.Registry32,
    }) {
      using RegistryKey root = RegistryKey.OpenBaseKey(
          RegistryHive.LocalMachine, view);
      using RegistryKey? key = root.OpenSubKey(VcRuntimeKey);
      if (key?.GetValue("Installed") is int installed
          && key.GetValueKind("Installed") == RegistryValueKind.DWord
          && installed == 1) {
        RunTrace.Write("system", $"The Visual C++ runtime is installed ({view})");
        return true;
      }
    }

    return false;
  }

  public bool CanLoadLibrary(string name) {
    if (!NativeLibrary.TryLoad(name, out IntPtr handle)) {
      return false;
    }

    NativeLibrary.Free(handle);
    return true;
  }

  public string? ReadOsRelease() => LinuxPackages.ReadOsRelease();

  public async Task<ProgramResult> Run(
      ProgramStartInfo start, CancellationToken cancellationToken) {
    ProcessStartInfo info = new(start.FileName) {
      UseShellExecute = false,
      WorkingDirectory = start.WorkingDirectory,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (string argument in start.Arguments) {
      info.ArgumentList.Add(argument);
    }

    Invocation.Current.ApplyEnvironment(info.Environment);
    foreach ((string name, string value) in start.EnvironmentOverrides) {
      info.Environment[name] = value;
    }

    using Process process = Process.Start(info)
        ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
    process.StandardInput.Close();
    process.ErrorDataReceived += (_, e) => {
      if (e.Data is string line) {
        RunTrace.Write("system", $"| {line}");
      }
    };
    process.BeginErrorReadLine();
    try {
      string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      return new ProgramResult(process.ExitCode, output);
    }
    catch (OperationCanceledException) {
      process.Kill(entireProcessTree: true);
      throw;
    }
  }

  public async Task<int> RunAsAdministrator(
      string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) {
    ProcessStartInfo info = new(fileName) {
      UseShellExecute = true,
      Verb = "runas",
    };
    foreach (string argument in arguments) {
      info.ArgumentList.Add(argument);
    }

    Process? process;
    try {
      process = Process.Start(info);
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) {
      throw new OperationCanceledException(
          $"{fileName} was not allowed to run as an administrator.", ex);
    }

    using (process) {
      if (process is null) {
        throw new InvalidOperationException($"Could not start {fileName}.");
      }

      await process.WaitForExitAsync(cancellationToken);
      return process.ExitCode;
    }
  }
}

/// Thrown when a system dependency cannot be installed.
public class SystemDependencyException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// Installs the system dependencies (protocol/lip-workspace.md, "System dependencies"): the Wine
/// of lip, extracted below the data directory, and the Visual C++ runtime, on Windows or in the
/// Wine prefix of the workspace.
///
/// Downloads go to files below the data directory, are checked against their SHA-256, and are
/// deleted afterwards. A Wine build is extracted into a directory of its own and renamed into
/// place, so that a partly extracted build is never run. Processes of lip take the file lock in the
/// data directory while they install, and look again once they hold it. What a process prepared
/// is not checked again by the same process. files defaults to the pinned files, and decompress,
/// which reads an .xz stream, to the decoder of SharpCompress; tests may stand in for them.
public sealed class SystemDependencies(
    IFileSystem fileSystem,
    IFileDownloader downloader,
    IUserInteraction userInteraction,
    ISystemProbe probe,
    string dataDirectory,
    Func<string, string?> getEnvironmentVariable,
    SystemDependencyFiles? files = null,
    Func<Stream, Stream>? decompress = null) : ISystemPreparer {
  /// Exit codes of vc_redist.x64.exe that are a success: done, a newer version is installed
  /// (ERROR_PRODUCT_VERSION), and done but a restart is needed (ERROR_SUCCESS_REBOOT_REQUIRED).
  private static readonly int[] _installerSuccess = [0, 1638, 3010];

  private readonly IFileSystem _fs = fileSystem;
  private readonly IFileDownloader _downloader = downloader;
  private readonly IUserInteraction _ui = userInteraction;
  private readonly ISystemProbe _probe = probe;
  private readonly string _data = dataDirectory;
  private readonly Func<string, string?> _getEnvironmentVariable = getEnvironmentVariable;
  private readonly SystemDependencyFiles _files = files ?? SystemDependencyFiles.Pinned;
  private readonly Func<Stream, Stream> _decompress =
      decompress ?? (stream => new XZStream(stream));

  // The Wine prefixes (or "" for Windows) in which this process found the runtime installed.
  private readonly HashSet<string> _prepared = new(StringComparer.Ordinal);

  /// The system dependencies of the system lip runs on, below the data directory of lip.
  public static SystemDependencies Create(
      IFileSystem fileSystem, IFileDownloader downloader, IUserInteraction userInteraction) =>
      new(fileSystem, downloader, userInteraction, new SystemProbe(), LipDirectories.Data,
          Invocation.Current.GetEnvironmentVariable);

  public async Task Prepare(
      string platform, string workspaceRoot, CancellationToken cancellationToken) {
    if (!WorkspaceProgram.IsWindowsPlatform(platform)) {
      return;
    }

    SystemInfo system = _probe.System;
    string? wine = null;
    if (!system.IsWindows) {
      wine = await EnsureWine(system, cancellationToken);
    }

    ProgramHost host = new(system.IsWindows, platform, workspaceRoot, _getEnvironmentVariable) {
      Wine = wine,
    };
    await EnsureVcRuntime(host, cancellationToken);
  }

  /// Installs the Wine of lip for the system when it is not installed, and returns the path of
  /// its wine program. Throws SystemDependencyException when the system has none, or when it is
  /// macOS on arm64 without Rosetta 2.
  public async Task<string> EnsureWine(SystemInfo system, CancellationToken cancellationToken) {
    WineBuild build = _files.WineFor(system)
        ?? throw new SystemDependencyException(
            $"lip runs the server with Wine on systems other than Windows, but has no Wine for"
            + $" {system.Describe()}. Run the server on Windows, on Linux on x64 or on macOS.");
    if (system is { Os: HostOs.MacOS, Architecture: Architecture.Arm64 }
        && !_probe.IsRosettaInstalled()) {
      throw new SystemDependencyException(
          "lip runs the server with Wine, which runs on this Mac only through Rosetta 2, and"
          + " Rosetta 2 is not installed. Installing it needs an administrator: run"
          + " 'softwareupdate --install-rosetta --agree-to-license', then run lip again.");
    }

    if (system.Os == HostOs.Linux) {
      CheckLinuxLibraries();
    }

    string directory = ManagedWine.BuildDirectory(_data, build);
    string executable = ManagedWine.Executable(_data, build);
    if (_fs.Directory.Exists(directory)) {
      return executable;
    }

    using DataLock dataLock = await DataLock.Take(_fs, _data, _ui, cancellationToken);
    if (_fs.Directory.Exists(directory)) {
      RunTrace.Write("system", $"Another lip process installed Wine in {directory}");
      return executable;
    }

    await _ui.PrintInfo(
        $"lip runs the server with Wine {build.Version}, which it installs once for every"
        + $" workspace, into {directory}.");
    string archive = Path.Join(_data, build.Name + ".tar.xz.tmp");
    string extracting = directory + ".tmp";
    Stream? held = null;
    try {
      held = await DownloadVerified(build.Archive, archive);
      DeleteDirectory(extracting);
      await _ui.RunWithProgress(
          $"Extracting Wine {build.Version}",
          progress => Task.Run(
              () => Extract(archive, extracting, progress, cancellationToken),
              cancellationToken));
      _fs.Directory.Move(extracting, directory);
    }
    catch (Exception ex)
        when (ex is InvalidDataException || FileSystemExtensions.IsFileError(ex)) {
      throw new SystemDependencyException(
          $"lip could not install Wine {build.Version} into {directory}: {ex.Message}", ex);
    }
    finally {
      held?.Dispose();
      DeleteFile(archive);
      DeleteDirectory(extracting);
    }

    RunTrace.Write("system", $"Installed Wine {build.Version} in {directory}");
    return executable;
  }

  /// A shared library of Linux that the Wine of lip needs: Name, the file the dynamic linker
  /// loads; Of, what it is; Why, what fails without it; and Package, the package that has it.
  private sealed record LinuxLibrary(string Name, string Of, string Why, LinuxPackage Package);

  private static readonly LinuxLibrary _gnutls = new(
      "libgnutls.so.30", "GnuTLS",
      "Wine makes TLS connections with it, without which the server cannot connect to the"
      + " services of Minecraft",
      new("libgnutls30", "gnutls", "libgnutls30", "gnutls"));

  private static readonly LinuxLibrary _fontconfig = new(
      "libfontconfig.so.1", "Fontconfig",
      "Wine, which finds FreeType on this system, needs it as well, and without it starts"
      + " processes without end until the system runs out of memory",
      new("libfontconfig1", "fontconfig", "fontconfig", "fontconfig"));

  /// Checks that the system has the shared libraries the Wine of lip for Linux needs
  /// (protocol/lip-workspace.md, "System dependencies"): GnuTLS, and Fontconfig when it has
  /// FreeType. Throws SystemDependencyException naming those it has not, and how to install them.
  private void CheckLinuxLibraries() {
    List<LinuxLibrary> missing = [];
    if (!_probe.CanLoadLibrary(_gnutls.Name)) {
      missing.Add(_gnutls);
    }

    if (_probe.CanLoadLibrary("libfreetype.so.6") && !_probe.CanLoadLibrary(_fontconfig.Name)) {
      missing.Add(_fontconfig);
    }

    if (missing.Count == 0) {
      return;
    }

    RunTrace.Write(
        "system", $"Missing libraries: {string.Join(", ", missing.Select(l => l.Name))}");
    string libraries = string.Join("; ", missing.Select(l => $"{l.Of} ({l.Name}): {l.Why}"));
    throw new SystemDependencyException(
        $"lip runs the server with Wine, which needs libraries that this system does not have."
        + $" {libraries}. "
        + LinuxPackages.Advice(
            _probe.ReadOsRelease(), [.. missing.Select(l => l.Package)], missing.Count > 1));
  }

  /// Installs the Visual C++ runtime when it is not installed: on Windows as an administrator,
  /// otherwise in the Wine prefix of the host.
  private async Task EnsureVcRuntime(ProgramHost host, CancellationToken cancellationToken) {
    string where = host.IsWindows ? "" : WinePrefix(host);
    if (_prepared.Contains(where)) {
      return;
    }

    if (await IsVcRuntimeInstalled(host, cancellationToken)) {
      _prepared.Add(where);
      return;
    }

    using DataLock dataLock = await DataLock.Take(_fs, _data, _ui, cancellationToken);
    if (await IsVcRuntimeInstalled(host, cancellationToken)) {
      _prepared.Add(where);
      return;
    }

    await _ui.PrintInfo(host.IsWindows
        ? "The server needs the Microsoft Visual C++ Redistributable, which is not installed:"
          + " lip installs it now. Windows may ask you to allow it."
        : $"The server needs the Microsoft Visual C++ Redistributable, which is not installed in"
          + $" the Wine prefix {where}: lip installs it there now.");
    string installer = Path.Join(_data, "vc_redist.x64.exe");
    Stream? held = null;
    try {
      // Held open, so that nothing replaces the installer between its check and its run.
      held = await DownloadVerified(_files.VcRedistX64, installer);
      string[] arguments = ["/install", "/quiet", "/norestart"];
      int exitCode = 0;
      await _ui.RunWithProgress(
          "Installing the Microsoft Visual C++ Redistributable",
          async _ => exitCode = host.IsWindows
              ? await RunAsAdministrator(installer, arguments, cancellationToken)
              : (await RunThroughWine(
                  WorkspaceProgram.Start(host, installer, arguments), cancellationToken)).ExitCode);
      RunTrace.Write("system", $"vc_redist.x64.exe exited with code {exitCode}");
      if (!_installerSuccess.Contains(exitCode)) {
        throw new SystemDependencyException(
            $"The installer of the Microsoft Visual C++ Redistributable failed with exit code"
            + $" {exitCode}"
            + (host.IsWindows
                ? ". Install it yourself from https://aka.ms/vc14/vc_redist.x64.exe, then run lip"
                  + " again."
                : $" in the Wine prefix {where}. Delete that directory to let Wine make it again,"
                  + " then run lip again."));
      }
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      throw new SystemDependencyException(
          $"lip could not install the Microsoft Visual C++ Redistributable: {ex.Message}", ex);
    }
    finally {
      held?.Dispose();
      DeleteFile(installer);
    }

    _prepared.Add(where);
  }

  private async Task<int> RunAsAdministrator(
      string installer, string[] arguments, CancellationToken cancellationToken) {
    try {
      return await _probe.RunAsAdministrator(installer, arguments, cancellationToken);
    }
    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
      throw new SystemDependencyException(
          "The Microsoft Visual C++ Redistributable was not installed: Windows did not allow its"
          + " installer to run as an administrator. Run lip again and allow it, or install it"
          + " yourself from https://aka.ms/vc14/vc_redist.x64.exe.", ex);
    }
    catch (Win32Exception ex) {
      throw new SystemDependencyException(
          $"lip could not start the installer of the Microsoft Visual C++ Redistributable:"
          + $" {ex.Message}", ex);
    }
  }

  /// Whether the runtime is installed: in the registry of Windows, or in that of the Wine prefix,
  /// which wine reg reads in the 64-bit and the 32-bit view.
  private async Task<bool> IsVcRuntimeInstalled(
      ProgramHost host, CancellationToken cancellationToken) {
    if (host.IsWindows) {
      return _probe.IsVcRuntimeInstalled();
    }

    foreach (string view in new[] { "/reg:64", "/reg:32" }) {
      ProgramStartInfo query = WorkspaceProgram.Start(host, "reg", [
        "query", @"HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", "/v",
        "Installed", view,
      ]);
      // The query may create the prefix; Wine then would offer to download Wine Mono and Gecko,
      // which no program of the server needs, in a window that waits for the user.
      if (_getEnvironmentVariable("WINEDLLOVERRIDES") is null) {
        query = query with {
          EnvironmentOverrides = new Dictionary<string, string>(query.EnvironmentOverrides) {
            ["WINEDLLOVERRIDES"] = "mscoree,mshtml=",
          },
        };
      }

      ProgramResult result = await RunThroughWine(query, cancellationToken);
      if (result.ExitCode == 0 && ReadsInstalled(result.Output)) {
        RunTrace.Write("system", $"The Visual C++ runtime is installed in {WinePrefix(host)}");
        return true;
      }
    }

    return false;
  }

  /// Whether the output of reg query has the value Installed as the REG_DWORD 1, which it writes
  /// in hexadecimal, such as 0x1.
  public static bool ReadsInstalled(string output) =>
      output.Split('\n').Any(line =>
          line.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries)
              is ["Installed", "REG_DWORD", string value]
          && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
          && uint.TryParse(
              value.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier, null,
              out uint number)
          && number == 1);

  private async Task<ProgramResult> RunThroughWine(
      ProgramStartInfo start, CancellationToken cancellationToken) {
    RunTrace.Write("system", $"Running {start.FileName} {string.Join(' ', start.Arguments)}"
        + string.Concat(start.EnvironmentOverrides.Select(v => $", {v.Key}={v.Value}")));
    try {
      return await _probe.Run(start, cancellationToken);
    }
    catch (Exception ex) when (ex is Win32Exception
        or InvalidOperationException) {
      throw new SystemDependencyException(
          $"lip could not run {start.FileName}: {ex.Message} Delete {ManagedWine.Directory(_data)}"
          + " to let lip install Wine again.", ex);
    }
  }

  /// The Wine prefix programs of the host run in.
  private string WinePrefix(ProgramHost host) =>
      _getEnvironmentVariable("WINEPREFIX") is { Length: > 0 } prefix
          ? prefix
          : Path.Join(host.WorkspaceRoot, ".lip", "wine");

  /// Downloads the file to path and checks its SHA-256, and returns it opened for reading with
  /// only reading shared, so that it cannot change while the caller holds it. On a mismatch,
  /// deletes it and throws SystemDependencyException.
  private async Task<Stream> DownloadVerified(PinnedFile file, string path) {
    _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(path)!);
    await _downloader.DownloadFile(file.Url, _fs.FileInfo.New(path));
    Stream stream = _fs.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    string actual;
    try {
      actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
    catch {
      stream.Dispose();
      throw;
    }

    RunTrace.Write("system", $"SHA-256 of {file.Url}: {actual}");
    if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase)) {
      stream.Dispose();
      DeleteFile(path);
      throw new SystemDependencyException(
          $"The download of {file.Url} is not the file lip expects: its SHA-256 is {actual}, not"
          + $" {file.Sha256}. It may have been changed on its way, e.g. by a proxy. Try again"
          + " later, or on another network.");
    }

    return stream;
  }

  /// Extracts the .tar.xz archive into the directory, keeping the permissions of its files
  /// outside Windows. Every entry must stay inside the directory, symbolic links included: no
  /// entry is written through a symbolic link of the archive, and no link points through one, so
  /// that the paths checked are the paths written.
  private void Extract(
      string archive, string directory, IProgress<double> progress, CancellationToken token) {
    string root = _fs.Path.GetFullPath(directory);
    string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
        ? root
        : root + Path.DirectorySeparatorChar;
    _fs.Directory.CreateDirectory(root);

    using Stream file = _fs.File.OpenRead(archive);
    long length = file.Length;
    using Stream decompressed = _decompress(file);
    using TarReader reader = new(decompressed);
    HashSet<string> links = new(StringComparer.Ordinal);
    while (reader.GetNextEntry() is TarEntry entry) {
      token.ThrowIfCancellationRequested();
      if (length > 0) {
        progress.Report(100.0 * file.Position / length);
      }

      string path = Inside(root, entry.Name, entry.Name, lastMayBeLink: false);
      switch (entry.EntryType) {
        case TarEntryType.Directory:
          _fs.Directory.CreateDirectory(path);
          break;
        case TarEntryType.RegularFile or TarEntryType.V7RegularFile
            or TarEntryType.ContiguousFile:
          _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(path)!);
          using (Stream output = _fs.File.Create(path)) {
            entry.DataStream?.CopyTo(output);
          }

          if (!OperatingSystem.IsWindows()) {
            _fs.File.SetUnixFileMode(path, entry.Mode);
          }

          break;
        case TarEntryType.SymbolicLink:
          if (Path.IsPathRooted(entry.LinkName)) {
            throw Invalid(entry.Name, $"links to the absolute path {entry.LinkName}");
          }

          Inside(
              _fs.Path.GetDirectoryName(path)!, entry.LinkName, entry.Name, lastMayBeLink: true);
          _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(path)!);
          _fs.File.CreateSymbolicLink(path, entry.LinkName);
          links.Add(path);
          break;
        case TarEntryType.HardLink:
          _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(path)!);
          _fs.File.Copy(
              Inside(root, entry.LinkName, entry.Name, lastMayBeLink: false), path);
          break;
        default:
          throw Invalid(entry.Name, $"is of the type {entry.EntryType}");
      }
    }

    progress.Report(100);

    // The full path of relative below the directory start, when it stays inside the directory
    // and goes through no symbolic link extracted before: none of its segments but the last, nor
    // the last unless lastMayBeLink.
    string Inside(string start, string relative, string name, bool lastMayBeLink) {
      string full = start;
      string[] segments = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
      for (int i = 0; i < segments.Length; i++) {
        full = segments[i] switch {
          "." => full,
          ".." => _fs.Path.GetDirectoryName(full) ?? full,
          string segment => _fs.Path.Join(full, segment),
        };
        if (full != root && !full.StartsWith(rootWithSeparator, StringComparison.Ordinal)) {
          throw Invalid(name, "is outside the archive");
        }

        if (links.Contains(full) && (i < segments.Length - 1 || !lastMayBeLink)) {
          throw Invalid(name, "goes through a symbolic link of the archive");
        }
      }

      return full;
    }

    InvalidDataException Invalid(string name, string what) =>
        new($"The archive {archive} is not a Wine build lip can install: its entry {name} {what}.");
  }

  private void DeleteFile(string path) {
    try {
      _fs.File.Delete(path);
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      RunTrace.Write("system", $"Could not delete {path}: {ex.Message}");
    }
  }

  private void DeleteDirectory(string path) {
    try {
      if (_fs.Directory.Exists(path)) {
        _fs.Directory.Delete(path, recursive: true);
      }
    }
    catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
      RunTrace.Write("system", $"Could not delete {path}: {ex.Message}");
    }
  }

  /// The lock of the data directory, the file lock in it, opened without sharing; a process
  /// waits for another that holds it.
  private sealed class DataLock : IDisposable {
    private readonly FileSystemStream _stream;

    private DataLock(FileSystemStream stream) {
      _stream = stream;
    }

    public static async Task<DataLock> Take(
        IFileSystem fs, string dataDirectory, IUserInteraction ui, CancellationToken token) {
      string path = Path.Join(dataDirectory, "lock");
      fs.Directory.CreateDirectory(dataDirectory);
      bool told = false;
      while (true) {
        try {
          return new DataLock(fs.FileStream.New(
              path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
        }
        catch (IOException ex) when (IsLocked(ex, fs, path)) {
          if (!told) {
            told = true;
            RunTrace.Write("system", $"{path} is held by another process: {ex.Message}");
            await ui.PrintInfo(
                "Another lip process is installing what the server needs; waiting for it.");
          }

          await Task.Delay(TimeSpan.FromMilliseconds(500), token);
        }
      }
    }

    /// A sharing or lock violation on Windows; elsewhere the failed flock, told from other
    /// failures by the file opening when it is shared.
    private static bool IsLocked(IOException ex, IFileSystem fs, string path) {
      if (ex is FileNotFoundException or DirectoryNotFoundException) {
        return false;
      }

      if (OperatingSystem.IsWindows()) {
        return (ex.HResult & 0xFFFF) is 32 or 33;
      }

      try {
        fs.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite).Dispose();
        return true;
      }
      catch (Exception probe) when (FileSystemExtensions.IsFileError(probe)) {
        return false;
      }
    }

    public void Dispose() => _stream.Dispose();
  }
}
