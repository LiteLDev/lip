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

// The console shows what lip and the servers it runs write, in UTF-8.
ConsoleEncoding.UseUtf8();

// With --uri and a link, which the shell gives when the user opens a lip: link, lip runs the
// interface for the link (protocol/lip-uri.md).
if (DaemonEntry.IsLink(args)) {
  return await DaemonEntry.RunLink(args);
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
