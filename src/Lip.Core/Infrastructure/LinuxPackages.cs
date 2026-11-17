using System.Runtime.InteropServices;

namespace Lip.Core.Infrastructure;

/// A package of Linux distributions, by the name it has with each package manager.
public sealed record LinuxPackage(string Apt, string Dnf, string Zypper, string Pacman);

/// Telling the user how to install packages of the Linux distribution lip runs on.
public static class LinuxPackages {
  /// The content of the os-release file of the system, or null when it has none.
  public static string? ReadOsRelease() {
    foreach (string path in (string[])["/etc/os-release", "/usr/lib/os-release"]) {
      try {
        return File.ReadAllText(path);
      }
      catch (Exception ex) when (FileSystemExtensions.IsFileError(ex)) {
        RunTrace.Write("system", $"Could not read {path}: {ex.Message}");
      }
    }

    return null;
  }

  /// The command that installs the packages with the package manager of the distribution that
  /// the os-release file names by ID or ID_LIKE, or null for one lip does not know.
  public static string? InstallCommand(string? osRelease, IEnumerable<LinuxPackage> packages) {
    HashSet<string> ids = new(StringComparer.Ordinal);
    foreach (string line in (osRelease ?? "").Split('\n')) {
      string[] parts = line.Trim().Split('=', 2);
      if (parts is ["ID" or "ID_LIKE", string value]) {
        ids.UnionWith(value.Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries));
      }
    }

    string Names(Func<LinuxPackage, string> name) =>
        string.Join(' ', packages.Select(name).Distinct());
    bool Is(params string[] any) => any.Any(ids.Contains);
    return Is("debian", "ubuntu") ? $"apt-get install {Names(p => p.Apt)}"
        : Is("suse", "opensuse") ? $"zypper install {Names(p => p.Zypper)}"
        : Is("fedora", "rhel", "centos", "amzn") ? $"dnf install {Names(p => p.Dnf)}"
        : Is("arch") ? $"pacman -S {Names(p => p.Pacman)}"
        : null;
  }

  /// A sentence that tells the user to install what is missing, with the packages, and to run
  /// lip again; several: whether that is more than one thing.
  public static string Advice(
      string? osRelease, IReadOnlyList<LinuxPackage> packages, bool several) {
    string them = several ? "them" : "it";
    return (InstallCommand(osRelease, packages) is string command
        ? $"Install {them} as root, with '{command}'"
        : $"Install {them} with the package manager of the system")
        + ", then run lip again.";
  }
}

/// OpenSSL, with which .NET makes the HTTPS connections of lip on Linux. Without it, .NET ends the
/// process at the first connection, writing only "No usable version of libssl was found" to the
/// standard error, which the daemon has none of; lip checks it before then.
public static class OpenSsl {
  // The versions of libssl that .NET loads.
  private static readonly Lazy<bool> _loadable = new(() =>
      ((string[])["libssl.so.3", "libssl.so.1.1"]).Any(name => {
        if (!NativeLibrary.TryLoad(name, out IntPtr handle)) {
          return false;
        }

        NativeLibrary.Free(handle);
        return true;
      }));

  // ca-certificates depends on OpenSSL in Debian and Ubuntu, and the system needs it as well.
  private static readonly LinuxPackage _package = new(
      "ca-certificates", "openssl", "libopenssl3", "openssl");

  /// Throws SystemDependencyException when lip runs on Linux and cannot load libssl.
  public static void EnsureLoadable() {
    if (!OperatingSystem.IsLinux() || _loadable.Value) {
      return;
    }

    throw new SystemDependencyException(
        "lip makes HTTPS connections with OpenSSL (libssl.so.3 or libssl.so.1.1), which this"
        + " system does not have. "
        + LinuxPackages.Advice(LinuxPackages.ReadOsRelease(), [_package], several: false));
  }
}
