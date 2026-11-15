using System.Runtime.InteropServices;

namespace Lip.Cli;

/// The signals that interrupt lip or ask it to stop (protocol/lip.md, "Workspace operations";
/// protocol/lip-run.md, "Stopping"): SIGINT, SIGQUIT, SIGTERM and SIGHUP, which .NET also raises
/// for the console control signals of Windows.
public static class Signals {
  private static readonly PosixSignal[] _signals = [
    PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM, PosixSignal.SIGHUP,
  ];

  /// Calls handle for each of the signals that this process receives, until the returned
  /// registration is disposed. When handle returns false, the signal ends the process as it
  /// would without the registration.
  public static IDisposable Listen(Func<PosixSignal, bool> handle) =>
      new Registrations([
        .. _signals.Select(signal => PosixSignalRegistration.Create(signal, context => {
          context.Cancel = handle(context.Signal);
        })),
      ]);

  /// Ends this process as the signal ends a process that does not handle it, with no
  /// registration left; returns the exit code to exit with should it not end it.
  public static int EndBy(PosixSignal signal) {
    if (OperatingSystem.IsWindows()) {
      // A console control signal ends a process with STATUS_CONTROL_C_EXIT.
      return unchecked((int)0xC000013A);
    }

    int number = signal switch {
      PosixSignal.SIGHUP => 1,
      PosixSignal.SIGINT => 2,
      PosixSignal.SIGQUIT => 3,
      _ => 15,
    };
    _ = kill(Environment.ProcessId, number);
    Thread.Sleep(TimeSpan.FromSeconds(1));
    return 128 + number;
  }

  [DllImport("libc", SetLastError = true)]
  private static extern int kill(int pid, int signal);

  private sealed class Registrations(List<PosixSignalRegistration> registrations) : IDisposable {
    public void Dispose() {
      foreach (PosixSignalRegistration registration in registrations) {
        registration.Dispose();
      }
    }
  }
}
