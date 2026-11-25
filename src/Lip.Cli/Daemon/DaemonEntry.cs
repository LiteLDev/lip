using System.Collections;
using System.Runtime.InteropServices;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
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
        IdleTimeout, CarryOut, () => ServerSessions.Shared.AnyRunning);
    try {
      await host.Run();
      return 0;
    }
    catch (EndpointInUseException) {
      // Another daemon was started at the same time, and listens.
      return 0;
    }
  }

  /// Carries out the command of a client in the daemon, writing as its consoles would; without
  /// arguments, on a terminal, the interface (protocol/lip-tui.md), on the terminal of the client.
  public static Task<int> CarryOut(
      string[] arguments, Start start, Invocation invocation, ClientTerminal terminal) =>
      RunsInterface(arguments, start.IsInputRedirected, start.IsOutputRedirected)
          ? Task.Factory.StartNew(
              () => {
                try {
                  // Written to standard error at once, which the client writes once its console
                  // is back to its screen: also when a signal, such as one from an installer
                  // that replaces lip, ends the client, or when the daemon ends first.
                  return LipApp.RunInterface(
                      terminal, invocation.WorkingDirectory, Languages.Parse(start.Language),
                      message => {
                        invocation.Error.WriteLine(message);
                        invocation.Error.Flush();
                      },
                      LinkOf(arguments));
                }
                catch (Exception ex) {
                  return ErrorReporter.Report(
                      ex, [], cancelled: false, start.Error.CreateConsole(invocation.Error));
                }
              },
              CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
          : LipApp.Run(
              arguments,
              start.Output.CreateConsole(invocation.Out),
              start.Error.CreateConsole(invocation.Error));

  /// Whether lip runs the interface: without an argument, or with --uri and a link, when its
  /// standard input and its standard output are both terminals (protocol/lip.md, OPTIONS).
  public static bool RunsInterface(
      string[] arguments, bool inputRedirected, bool outputRedirected) =>
      (arguments.Length == 0 || IsLink(arguments)) && !inputRedirected && !outputRedirected;

  /// Whether the arguments are --uri and a link (protocol/lip-uri.md), with which the shell
  /// starts lip when the user opens a lip: link.
  public static bool IsLink(string[] arguments) => arguments is [LinkOption, _];

  /// The option that gives the link.
  public const string LinkOption = "--uri";

  /// The link of the arguments, or null when they are not --uri and a link. Throws
  /// FormatException when the link is not valid.
  private static LipUri? LinkOf(string[] arguments) =>
      IsLink(arguments) ? LipUri.Parse(arguments[1]) : null;

  /// lip --uri with a link: runs the interface for it, as lip without arguments runs it. A link
  /// that is not valid, or standard streams that are not terminals, are a failure, which a
  /// console that only this process has, such as the one that Windows opens for lip when the
  /// browser starts it, keeps on the screen until the user presses a key.
  public static async Task<int> RunLink(string[] args) {
    string? failure = null;
    if (!LipUri.TryParse(args[1], out _, out string? error)) {
      failure = $"lip cannot open the link '{args[1]}': {error}.";
    } else if (Console.IsInputRedirected || Console.IsOutputRedirected) {
      failure = "lip opens a link only when its standard input and its standard output are"
          + " terminals.";
    }

    if (failure is null) {
      return await RunInterfaceClient(args);
    }

    await Console.Error.WriteLineAsync(failure);
    if (!Console.IsInputRedirected && ConsoleOfThisProcessOnly()) {
      await Console.Error.WriteLineAsync("Press any key to close this window.");
      Console.ReadKey(intercept: true);
    }

    return 1;
  }

  /// Whether this process is the only one attached to its console: Windows made the console
  /// for it, and closes it when it exits.
  private static bool ConsoleOfThisProcessOnly() {
    if (!OperatingSystem.IsWindows()) {
      return false;
    }

    uint[] processes = new uint[2];
    return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

  /// Has the daemon carry out the interface on the console of this process, and returns the exit
  /// code; when it cannot, runs the interface in this process (protocol/lip-daemon.md,
  /// "Fallback").
  /// args are none, or --uri and a valid link.
  public static async Task<int> RunInterfaceClient(string[]? args = null) {
    args ??= [];
    DaemonClient client = new(
        DaemonEndpoint.ForCurrentUser(), DaemonProcess.Version, DaemonProcess.Executable,
        DaemonProcess.Start);
    ClientOutcome outcome;
    // What reaches standard error while the alternate screen is shown is written once the
    // console is back to its screen, where the user can read it.
    MemoryStream error = new();
    StringWriter errorText = new();
    // The console is switched to the alternate screen before its streams are taken, so that they
    // write UTF-8.
    using (ConsoleTerminal terminal = new()) {
      ClientStreams streams = StreamsOf() with { Error = error, ErrorText = errorText };
      using (Signals.Listen(client.Signal)) {
        outcome = await client.Run(StartOf(args), streams, TerminalInput.Start);
      }
    }

    await Console.Error.WriteAsync(errorText.ToString());
    await Console.Error.FlushAsync();
    using (Stream standardError = Console.OpenStandardError()) {
      standardError.Write(error.ToArray());
    }

    if (outcome.ExitCode is int code) {
      return code;
    }

    if (outcome.Signal is PosixSignal ended) {
      await Console.Out.FlushAsync();
      return Signals.EndBy(ended);
    }

    await Console.Error.WriteLineAsync(
        $"lip could not have its daemon run the interface ({outcome.Fallback}); it runs it"
        + " itself.");
    // A failure, and what the interface has the user read once it has ended, are written once
    // the console is back to its screen.
    StringWriter farewell = new();
    int exitCode;
    try {
      using (ConsoleTerminal own = new()) {
        exitCode = LipApp.RunInterface(
            own, Environment.CurrentDirectory, Languages.OfThisProcess(),
            message => {
              lock (farewell) {
                farewell.WriteLine(message);
              }
            },
            LinkOf(args));
      }
    }
    catch (Exception ex) {
      exitCode = ErrorReporter.Report(ex, [], cancelled: false);
    }

    lock (farewell) {
      Console.Error.Write(farewell.ToString());
    }

    await Console.Error.FlushAsync();
    return exitCode;
  }

  /// Has the daemon carry out the command of this process, and returns the exit code; when it
  /// cannot, carries the command out in this process (protocol/lip-daemon.md, "Fallback").
  public static async Task<int> RunClient(string[] args) {
    DaemonClient client = new(
        DaemonEndpoint.ForCurrentUser(), DaemonProcess.Version, DaemonProcess.Executable,
        DaemonProcess.Start) {
      KeepsBusyDaemon = IsManagedRun(args),
    };
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

  /// Whether the arguments are those of lip run --managed, which a daemon of another version
  /// that runs a managed server carries out (protocol/lip-daemon.md, "Carrying out a command").
  public static bool IsManagedRun(string[] args) =>
      args.Length > 1 && args[0] == "run" && args.Contains("--managed");

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
        ConsoleProfile.Of(ConsoleProfile.CreateStandardError()),
        Languages.OfThisProcess() is { } language ? Languages.Code(language) : "");
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
