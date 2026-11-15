using Lip.Cli;
using Lip.Cli.Daemon;
using Lip.Core.Infrastructure;

// The executable that lip self-update could not delete while it ran (protocol/
// lip-self-update.md, "Starting").
SelfUpdater.DeleteOldExecutable(new System.IO.Abstractions.FileSystem(), Environment.ProcessPath);

RunTrace.TraceHttp();

// The daemon, which carries out the commands of the other lip processes of the user
// (protocol/lip-daemon.md).
if (args is [DaemonProcess.Argument]) {
  return await DaemonEntry.RunDaemon();
}

// Without an argument, on a terminal, lip runs the interface (protocol/lip-tui.md), which the
// daemon carries out like a command; otherwise it writes its help, as below.
if (DaemonEntry.RunsInterface(args, Console.IsInputRedirected, Console.IsOutputRedirected)) {
  return await DaemonEntry.RunInterfaceClient();
}

// A command is carried out by the daemon (protocol/lip-daemon.md, "Carrying out a command").
if (LipApp.RunsInDaemon(args)) {
  return await DaemonEntry.RunClient(args);
}

return await LipApp.RunInProcess(args);
