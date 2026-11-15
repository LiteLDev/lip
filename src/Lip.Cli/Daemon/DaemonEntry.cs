using System.Collections;
using System.Runtime.InteropServices;
using Lip.Core.Infrastructure;
using Spectre.Console;

namespace Lip.Cli.Daemon;

/// The two ends of the daemon in this executable (protocol/lip-daemon.md): lip started with
/// --daemon, and lip started with a command that the daemon carries out.
public static class DaemonEntry {
  /// How long the daemon waits for a connection before it exits.
  public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

  // Read by .NET for the proxy of the process, which the daemon takes from each client instead
  // (InvocationProxy).
  private static readonly string[] _proxyVariables = [
    "http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY", "all_proxy", "ALL_PROXY",
    "no_proxy", "NO_PROXY",
  ];

  /// Runs this process as the daemon until it exits.
  public static async Task<int> RunDaemon() {
    DaemonProcess.Detach();
    foreach (string variable in _proxyVariables) {
      Environment.SetEnvironmentVariable(variable, null);
    }

    DaemonHost host = new(
        DaemonEndpoint.ForCurrentUser(), DaemonProcess.Version, DaemonProcess.Executable,
        IdleTimeout, CarryOut);
    try {
      await host.Run();
      return 0;
    }
    catch (EndpointInUseException) {
      // Another daemon was started at the same time, and listens.
      return 0;
    }
  }

  /// Carries out the command of a client in the daemon, writing as its consoles would.
  public static Task<int> CarryOut(string[] arguments, Start start, Invocation invocation) =>
      LipApp.Run(
          arguments,
          start.Output.CreateConsole(invocation.Out),
          start.Error.CreateConsole(invocation.Error));

  /// Has the daemon carry out the command of this process, and returns the exit code; when it
  /// cannot, carries the command out in this process (protocol/lip-daemon.md, "Fallback").
  public static async Task<int> RunClient(string[] args) {
    DaemonClient client = new(
        DaemonEndpoint.ForCurrentUser(), DaemonProcess.Version, DaemonProcess.Executable,
        DaemonProcess.Start);
    ClientOutcome outcome;
    using (Signals.Listen(client.Signal)) {
      outcome = await client.Run(StartOf(args), StreamsOf());
    }

    if (outcome.ExitCode is int code) {
      return code;
    }

    if (outcome.Signal is PosixSignal ended) {
      await Console.Out.FlushAsync();
      await Console.Error.FlushAsync();
      return Signals.EndBy(ended);
    }

    await Console.Error.WriteLineAsync(
        $"lip could not have its daemon carry out the command ({outcome.Fallback}); it carries"
        + " it out itself.");
    return await LipApp.RunInProcess(args);
  }

  /// The command of this process, with everything of it that the daemon carries it out with.
  public static Start StartOf(string[] args) {
    List<KeyValuePair<string, string>> environment = [];
    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
      environment.Add(new((string)entry.Key, (string?)entry.Value ?? ""));
    }

    return new Start(
        args,
        Environment.CurrentDirectory,
        environment,
        Environment.ProcessId,
        Console.IsInputRedirected,
        Console.IsOutputRedirected,
        Console.IsErrorRedirected,
        ConsoleProfile.Of(ConsoleProfile.CreateStandardOutput()),
        ConsoleProfile.Of(ConsoleProfile.CreateStandardError()));
  }

  private static ClientStreams StreamsOf() =>
      new(
          Console.OpenStandardInput(),
          OperatingSystem.IsWindows() && !Console.IsInputRedirected,
          Console.OpenStandardOutput(),
          Console.OpenStandardError(),
          Console.Out,
          Console.Error);
}
