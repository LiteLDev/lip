using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Abstractions;
using System.IO.Compression;
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

/// The files of the Visual C++ runtime that lip extracts on Windows: Name, the directory below
/// vc-runtime/ in the data directory they are extracted into; Archive, a zip file (a .vsix of
/// Visual Studio) whose entries below a directory Microsoft.VC143.CRT of its x64 redistributable
/// files are the DLLs of the runtime.
public sealed record VcRuntimeFiles(string Name, PinnedFile Archive);

/// The files lip downloads to install the system dependencies (protocol/lip-workspace.md, "System
/// dependencies"): the Wine of lip for Linux on x64 and for macOS, and the files of the Visual C++
/// runtime that lip extracts on Windows. Pinned holds those of this version of lip.
public sealed record SystemDependencyFiles(
    WineBuild LinuxX64Wine,
    WineBuild MacOsWine,
    VcRuntimeFiles VcRuntimeX64) {
  public static SystemDependencyFiles Pinned { get; } = new(
      PinnedSystemDependencies.LinuxX64Wine,
      PinnedSystemDependencies.MacOsWine,
      PinnedSystemDependencies.VcRuntimeX64);

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

  /// The redistributable files of the Visual C++ runtime 14.44.35211 for x64, of the Microsoft
  /// Visual C++ 2015–2022 Redistributable 14.44, as Visual Studio 17.14 installs them: the package
  /// Microsoft.VC.14.44.17.14.CRT.Redist.X64.base of its channel
  /// https://aka.ms/vs/17/release/channel.
  public static readonly VcRuntimeFiles VcRuntimeX64 = new(
      "14.44.35211",
      new PinnedFile(
          "https://download.visualstudio.microsoft.com/download/pr/"
              + "45d3b8dd-bced-4b37-9974-142f748d710c/"
              + "4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5/"
              + "Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix",
          "4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5"));
}

/// Where the Visual C++ runtime that lip installs on Windows is in the data directory: the runtime
/// directory vc-runtime/&lt;name&gt;/, which appears only once its files are extracted completely
/// (protocol/lip-workspace.md, "System dependencies").
public static class ManagedVcRuntime {
  /// The runtime directory of the files.
  public static string Directory(string dataDirectory, VcRuntimeFiles files) =>
      Path.Join(dataDirectory, "vc-runtime", files.Name);

  /// The runtime directory that the programs of lip get first in PATH: on Windows, when the
  /// runtime is installed for the user and not for the system; otherwise null
  /// (protocol/lip-workspace.md, "Running programs").
  public static string? ForPrograms(string dataDirectory, ISystemProbe probe) {
    if (!probe.System.IsWindows || probe.IsVcRuntimeInstalled()) {
      return null;
    }

    string directory = Directory(dataDirectory, SystemDependencyFiles.Pinned.VcRuntimeX64);
    return System.IO.Directory.Exists(directory) ? directory : null;
  }
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
public sealed record ProgramResult(int ExitCode, string Output) {
  /// The last lines that the program wrote to its standard error, for messages.
  public string Error { get; init; } = "";
}

/// What preparing the system asks of the system lip runs on, so that tests can stand in for it.
public interface ISystemProbe {
  SystemInfo System { get; }

  /// Whether Rosetta 2 is installed (macOS only).
  bool IsRosettaInstalled();

  /// Whether the Visual C++ runtime is installed for the system, in the registry of Windows
  /// (Windows only).
  bool IsVcRuntimeInstalled();

  /// Whether the dynamic linker of the system loads the shared library of the name, as Wine
  /// loads it (Linux and macOS only).
  bool CanLoadLibrary(string name);

  /// The content of the os-release file of the system, or null when it has none (Linux only).
  string? ReadOsRelease();

  /// Runs the program, with its standard input empty, and returns its exit code and standard
  /// output; its standard error goes to the trace, and its last lines to Error. Throws when it
  /// cannot be started.
  Task<ProgramResult> Run(ProgramStartInfo start, CancellationToken cancellationToken);
}

/// Prepares the system to run the programs of a workspace (protocol/lip-workspace.md, "System
/// dependencies").
public interface ISystemPreparer {
  /// Prepares the system for the workspace whose root is workspaceRoot and whose environment has
  /// the platform; does nothing for a platform that is not a Windows platform.
  /// acceptRosettaLicense: the user accepts the license of Rosetta 2 (--accept-rosetta-license),
  /// so that it is installed without asking. Throws when a system dependency cannot be installed.
  Task Prepare(
      string platform,
      string workspaceRoot,
      bool acceptRosettaLicense,
      CancellationToken cancellationToken);
}

/// The system lip runs on.
public sealed class SystemProbe : ISystemProbe {
  private const string VcRuntimeKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

  // How many of the last lines of the standard error of a program Run returns.
  private const int ErrorLines = 5;

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
    Queue<string> error = new();
    process.ErrorDataReceived += (_, e) => {
      if (e.Data is string line) {
        RunTrace.Write("system", $"| {line}");
        lock (error) {
          error.Enqueue(line);
          if (error.Count > ErrorLines) {
            error.Dequeue();
          }
        }
      }
    };
    process.BeginErrorReadLine();
    try {
      string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      lock (error) {
        return new ProgramResult(process.ExitCode, output) {
          Error = string.Join('\n', error),
        };
      }
    }
    catch (OperationCanceledException) {
      process.Kill(entireProcessTree: true);
      throw;
    }
  }
}

/// Thrown when a system dependency cannot be installed.
public class SystemDependencyException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// Installs the system dependencies (protocol/lip-workspace.md, "System dependencies"): off
/// Windows, the Wine of lip, extracted below the data directory, which has a Visual C++ runtime of
/// its own; on Windows, the Visual C++ runtime, for the user.
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
    SystemDependencyFiles? files = null,
    Func<Stream, Stream>? decompress = null) : ISystemPreparer {
  /// The DLLs that the files of the Visual C++ runtime must have; its archive holds more.
  private static readonly string[] _vcRuntimeRequired =
      ["msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"];

  private readonly IFileSystem _fs = fileSystem;
  private readonly IFileDownloader _downloader = downloader;
  private readonly IUserInteraction _ui = userInteraction;
  private readonly ISystemProbe _probe = probe;
  private readonly string _data = dataDirectory;
  private readonly SystemDependencyFiles _files = files ?? SystemDependencyFiles.Pinned;
  private readonly Func<Stream, Stream> _decompress =
      decompress ?? (stream => new XZStream(stream));

  // Whether this process found the Visual C++ runtime installed on Windows.
  private bool _vcRuntimePrepared;

  /// The system dependencies of the system lip runs on, below the data directory of lip.
  public static SystemDependencies Create(
      IFileSystem fileSystem, IFileDownloader downloader, IUserInteraction userInteraction) =>
      new(fileSystem, downloader, userInteraction, new SystemProbe(), LipDirectories.Data);

  public async Task Prepare(
      string platform,
      string workspaceRoot,
      bool acceptRosettaLicense,
      CancellationToken cancellationToken) {
    if (!WorkspaceProgram.IsWindowsPlatform(platform)) {
      return;
    }

    SystemInfo system = _probe.System;
    if (system.IsWindows) {
      await EnsureVcRuntime(cancellationToken);
    } else {
      await EnsureWine(system, acceptRosettaLicense, cancellationToken);
    }
  }

  /// Installs the Wine of lip for the system when it is not installed; on macOS on arm64, installs
  /// Rosetta 2 first when it is not installed (EnsureRosetta). Throws SystemDependencyException
  /// when the system has none, or when Rosetta 2 is not installed.
  private async Task EnsureWine(
      SystemInfo system, bool acceptRosettaLicense, CancellationToken cancellationToken) {
    WineBuild build = _files.WineFor(system)
        ?? throw new SystemDependencyException(
            $"lip runs the server with Wine on systems other than Windows, but has no Wine for"
            + $" {system.Describe()}. Run the server on Windows, on Linux on x64 or on macOS.");
    if (system is { Os: HostOs.MacOS, Architecture: Architecture.Arm64 }) {
      await EnsureRosetta(acceptRosettaLicense, cancellationToken);
    }

    if (system.Os == HostOs.Linux) {
      CheckLinuxLibraries();
    }

    string directory = ManagedWine.BuildDirectory(_data, build);
    if (_fs.Directory.Exists(directory)) {
      return;
    }

    using DataLock dataLock = await DataLock.Take(_fs, _data, _ui, cancellationToken);
    if (_fs.Directory.Exists(directory)) {
      RunTrace.Write("system", $"Another lip process installed Wine in {directory}");
      return;
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
  }

  /// The program that installs Rosetta 2, and its arguments.
  private const string RosettaInstaller = "/usr/sbin/softwareupdate";

  private static readonly string[] _rosettaInstallerArguments =
      ["--install-rosetta", "--agree-to-license"];

  /// Why lip needs Rosetta 2, which is not installed.
  private const string RosettaMissing = "lip runs the server with Wine, which runs on this Mac"
      + " only through Rosetta 2, and Rosetta 2 is not installed.";

  /// Installs Rosetta 2 with softwareupdate when it is not installed (protocol/lip-workspace.md,
  /// "System dependencies", "Installing Rosetta 2"), asking the user first unless
  /// acceptRosettaLicense. Throws SystemDependencyException, naming the command that installs it,
  /// when the user does not accept, cannot be asked, or softwareupdate does not install it.
  /// Processes of lip ask and install one at a time, under the file lock of the data directory,
  /// and look again once they hold it.
  private async Task EnsureRosetta(bool acceptRosettaLicense, CancellationToken cancellationToken) {
    if (_probe.IsRosettaInstalled()) {
      return;
    }

    using DataLock dataLock = await DataLock.Take(_fs, _data, _ui, cancellationToken);
    if (_probe.IsRosettaInstalled()) {
      RunTrace.Write("system", "Another lip process installed Rosetta 2");
      return;
    }

    await _ui.PrintInfo(
        RosettaMissing + " lip installs it with softwareupdate, which accepts the license of"
        + " Rosetta 2: the software license agreement of Apple that"
        + " 'softwareupdate --install-rosetta' shows"
        + (acceptRosettaLicense
            ? ", and which you accepted with --accept-rosetta-license."
            : ". Read it before you go on."));
    if (!acceptRosettaLicense) {
      string? answer;
      try {
        answer = await _ui.Ask("yes to install Rosetta 2 and accept its license");
      }
      catch (CannotAskException ex) {
        throw RosettaNotInstalled(
            "Installing it needs you to accept its license, but lip cannot ask you here. If you"
            + " accept it, run the command again with --accept-rosetta-license.",
            ex);
      }
      catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
        // The question was closed without an answer, as an interface allows.
        throw RosettaNotInstalled("You did not accept the license of Rosetta 2.", ex);
      }

      if (answer?.Trim().ToLowerInvariant() is not ("y" or "yes")) {
        throw RosettaNotInstalled("You did not accept the license of Rosetta 2.");
      }
    }

    await _ui.PrintInfo("Installing Rosetta 2, which may take a few minutes.");
    RunTrace.Write(
        "system", $"Running {RosettaInstaller} {string.Join(' ', _rosettaInstallerArguments)}");
    ProgramResult result;
    try {
      result = await _probe.Run(
          new ProgramStartInfo(
              RosettaInstaller,
              _rosettaInstallerArguments,
              VerbatimArguments: false,
              WorkingDirectory: "/",
              EnvironmentOverrides: new Dictionary<string, string>()),
          cancellationToken);
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) {
      throw RosettaNotInstalled($"lip could not run {RosettaInstaller}: {ex.Message}", ex);
    }

    RunTrace.Write("system", $"{RosettaInstaller} exited with {result.ExitCode}");
    if (result.ExitCode != 0) {
      // What it said of the failure: its standard error, or else the last line of its output.
      string said = result.Error.Trim() is { Length: > 0 } error
          ? error
          : result.Output.Trim().Split('\n')[^1].Trim();
      throw RosettaNotInstalled(
          $"{RosettaInstaller} failed with the exit code {result.ExitCode}"
          + (said.Length == 0 ? "." : $": {said.TrimEnd('.')}."));
    }

    if (!_probe.IsRosettaInstalled()) {
      throw RosettaNotInstalled(
          $"{RosettaInstaller} succeeded, but Rosetta 2 is still not installed.");
    }

    await _ui.PrintInfo("Rosetta 2 is installed.");
  }

  /// Rosetta 2 could not be installed: why, and how the user installs it.
  private static SystemDependencyException RosettaNotInstalled(
      string reason, Exception? inner = null) =>
      new(RosettaMissing + $" {reason} To install it yourself, run"
          + " 'softwareupdate --install-rosetta --agree-to-license', then run lip again; on some"
          + " versions of macOS, that needs an administrator.",
          inner);

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

  /// Installs the Visual C++ runtime on Windows when it is not installed, for the user, by
  /// extracting its files into the runtime directory.
  private async Task EnsureVcRuntime(CancellationToken cancellationToken) {
    if (_vcRuntimePrepared) {
      return;
    }

    if (!IsVcRuntimeInstalled()) {
      using DataLock dataLock = await DataLock.Take(_fs, _data, _ui, cancellationToken);
      if (!IsVcRuntimeInstalled()) {
        await ExtractVcRuntime(cancellationToken);
      }
    }

    _vcRuntimePrepared = true;
  }

  /// Extracts the files of the runtime into the runtime directory, through a directory of its
  /// own that is renamed into place, so that partly extracted files are never used.
  private async Task ExtractVcRuntime(CancellationToken cancellationToken) {
    VcRuntimeFiles files = _files.VcRuntimeX64;
    string directory = ManagedVcRuntime.Directory(_data, files);
    await _ui.PrintInfo(
        "The server needs the Microsoft Visual C++ runtime, which is not installed: lip installs"
        + $" it for you, into {directory}.");
    string archive = directory + ".vsix.tmp";
    string extracting = directory + ".tmp";
    Stream? held = null;
    try {
      held = await DownloadVerified(files.Archive, archive);
      DeleteDirectory(extracting);
      Stream stream = held;
      await _ui.RunWithProgress(
          "Installing the Microsoft Visual C++ runtime",
          _ => Task.Run(() => ExtractVcRuntimeFiles(stream, extracting), cancellationToken));
      _fs.Directory.Move(extracting, directory);
    }
    catch (Exception ex)
        when (ex is InvalidDataException || FileSystemExtensions.IsFileError(ex)) {
      throw new SystemDependencyException(
          $"lip could not install the Microsoft Visual C++ runtime into {directory}: {ex.Message}"
          + " Or install it yourself from https://aka.ms/vc14/vc_redist.x64.exe.", ex);
    }
    finally {
      held?.Dispose();
      DeleteFile(archive);
      DeleteDirectory(extracting);
    }
  }

  /// Writes the DLLs of the archive below a directory Microsoft.VC143.CRT of its x64
  /// redistributable files, such as Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.CRT/,
  /// to the directory, by their names. Throws InvalidDataException when the archive has not
  /// every DLL of _vcRuntimeRequired.
  private void ExtractVcRuntimeFiles(Stream archive, string directory) {
    _fs.Directory.CreateDirectory(directory);
    HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
    using ZipArchive zip = new(archive, ZipArchiveMode.Read, leaveOpen: true);
    foreach (ZipArchiveEntry entry in zip.Entries) {
      if (IsVcRuntimeFile(entry.FullName) && written.Add(entry.Name)) {
        using Stream input = entry.Open();
        using Stream output = _fs.File.Create(_fs.Path.Join(directory, entry.Name));
        input.CopyTo(output);
      }
    }

    if (_vcRuntimeRequired.FirstOrDefault(name => !written.Contains(name)) is string missing) {
      throw new InvalidDataException($"The archive of the runtime has no {missing}.");
    }
  }

  /// Whether the entry of the archive is a DLL of the x64 runtime: its directory is
  /// Microsoft.VC143.CRT, its parent x64, and it is not below debug_nonredist.
  public static bool IsVcRuntimeFile(string entryName) {
    string[] parts = entryName.Split('/');
    return parts.Length >= 4
        && parts[0] == "Contents"
        && parts[^1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && parts[^2] == "Microsoft.VC143.CRT"
        && parts[^3] == "x64"
        && !parts.Contains("debug_nonredist")
        && !parts[^1].Contains('\\')
        && parts[^1] is not ("" or "." or "..");
  }

  /// Whether the runtime is installed on Windows: for the system in the registry of Windows, or
  /// for the user in the runtime directory.
  private bool IsVcRuntimeInstalled() {
    if (_probe.IsVcRuntimeInstalled()) {
      return true;
    }

    string directory = ManagedVcRuntime.Directory(_data, _files.VcRuntimeX64);
    if (_fs.Directory.Exists(directory)) {
      RunTrace.Write("system", $"The Visual C++ runtime is installed in {directory}");
      return true;
    }

    return false;
  }

  /// Downloads the file to path and checks its SHA-256, and returns it opened for reading with
  /// only reading shared, so that it cannot change while the caller holds it. The downloader
  /// sends a request to GitHub through github_proxy first, then directly when what the proxy
  /// serves has another SHA-256. On a mismatch, deletes it and throws SystemDependencyException.
  private async Task<Stream> DownloadVerified(PinnedFile file, string path) {
    _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(path)!);
    Stream? held = null;
    await _downloader.DownloadFile(file.Url, _fs.FileInfo.New(path), downloadUrl => {
      Stream stream = _fs.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.Read);
      string actual;
      try {
        actual = Convert.ToHexStringLower(SHA256.HashData(stream));
      }
      catch {
        stream.Dispose();
        throw;
      }

      RunTrace.Write("system", $"SHA-256 of {downloadUrl}: {actual}");
      if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase)) {
        stream.Dispose();
        DeleteFile(path);
        throw new SystemDependencyException(
            $"The download of {downloadUrl} is not the file lip expects: its SHA-256 is {actual},"
            + $" not {file.Sha256}. It may have been changed on its way, e.g. by a proxy. Try"
            + " again later, or on another network.");
      }

      stream.Position = 0;
      held = stream;
    });
    return held!;
  }

  /// Extracts the .tar.xz archive into the directory, keeping the permissions of its files
  /// outside Windows. Every entry must stay inside the directory, symbolic links included: no
  /// entry is written through a symbolic link of the archive, and no link points through one, so
  /// that the paths checked are the paths written.
  private void Extract(
      string archive, string directory, IProgress<double> progress, CancellationToken token) {
    string root = _fs.Path.GetFullPath(directory);
    string rootWithSeparator = _fs.Path.WithTrailingSeparator(root);
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
