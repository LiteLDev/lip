using Lip.Cli;
using Lip.Cli.Daemon;
using Lip.Cli.Tui;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;

// The executable that lip self-update could not delete while it ran (protocol/
// lip-self-update.md, "Starting").
SelfUpdater.DeleteOldExecutable(new System.IO.Abstractions.FileSystem(), Environment.ProcessPath);

RunTrace.TraceHttp();

// The daemon, which carries out the commands of the other lip processes of the user
// (protocol/lip-daemon.md).
if (args is [DaemonProcess.Argument]) {
  return await DaemonEntry.RunDaemon();
}

// Without an argument, on a terminal, lip runs the interface (protocol/lip-tui.md); otherwise
// it writes its help, as below.
if (args.Length == 0 && !Console.IsInputRedirected && !Console.IsOutputRedirected) {
  try {
    using ConsoleTerminal terminal = new();
    return new TuiApp(
        terminal,
        (interaction, hookOutput) => LipClient.Create(
            new System.IO.Abstractions.FileSystem(), interaction, hookOutput),
        new ModCatalogClient(),
        DaemonProcess.Version,
        Environment.CurrentDirectory).Run();
  }
  catch (Exception ex) {
    return ErrorReporter.Report(ex, args, cancelled: false);
  }
}

// A command is carried out by the daemon (protocol/lip-daemon.md, "Carrying out a command").
if (LipApp.RunsInDaemon(args)) {
  return await DaemonEntry.RunClient(args);
}

return await LipApp.RunInProcess(args);
