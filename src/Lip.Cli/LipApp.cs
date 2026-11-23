using System.Runtime.InteropServices;
using Lip.Cli.Commands;
using Lip.Cli.Daemon;
using Lip.Cli.Servers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli;

/// Carries out a command (protocol/lip.md) for the current invocation (Invocation): in this
/// process, or in the daemon for a client (protocol/lip-daemon.md). Results go to output, the
/// console of standard output; messages to error, the console of standard error.
public static class LipApp {
  /// The command groups and commands that only this process carries out
  /// (protocol/lip-daemon.md, "Commands").
  public const string DaemonGroup = "daemon";

  /// Whether a client has the daemon carry out what the arguments ask for: a command, or what
  /// may be one, other than those of the command group daemon. Without arguments lip runs the
  /// interface or writes its help, and with an option first, its help, its version or a usage
  /// error.
  public static bool RunsInDaemon(string[] args) =>
      args.Length > 0 && !args[0].StartsWith('-') && args[0] != DaemonGroup;

  /// Carries out the command in this process, as lip always did: interruptions and stop requests
  /// are the signals that this process receives.
  public static async Task<int> RunInProcess(string[] args) {
    using IDisposable signals = Signals.Listen(Invocation.Process.Signal);
    return await Run(
        args, ConsoleProfile.CreateStandardOutput(), ConsoleProfile.CreateStandardError());
  }

  /// Runs the interface (protocol/lip-tui.md) on terminal for the current invocation, with the
  /// workspace list of the state directory and the servers of this process, until the user quits
  /// it; returns the exit code of lip. clientLanguage is the client language, or null when there
  /// is none. farewell gets what is to reach the terminal once the interface has ended
  /// (AppOptions.Farewell). Throws when the interface fails.
  public static int RunInterface(
      ITerminal terminal,
      string workingDirectory,
      Language? clientLanguage,
      Action<string> farewell) {
    System.IO.Abstractions.FileSystem fileSystem = new();
    return new App(new AppOptions {
      Terminal = terminal,
      CreateClient = (interaction, hookOutput, workspace) =>
          LipClient.Create(fileSystem, interaction, hookOutput, workspace),
      Catalog = new ModCatalogClient(),
      Version = DaemonProcess.Version,
      WorkingDirectory = workingDirectory,
      Workspaces = WorkspaceList.InStateDirectory(fileSystem),
      Servers = ServerSessions.Shared,
      GithubProbe = new GithubProbe(),
      ClientLanguage = clientLanguage,
      DaemonStatus = DaemonStatus,
      Farewell = farewell,
    }).Run();
  }

  /// What lip daemon status finds (protocol/lip-daemon.md, "Commands"): null when no daemon
  /// listens on the endpoint.
  private static async Task<DaemonState?> DaemonStatus(CancellationToken cancellationToken) {
    DaemonEndpoint endpoint = DaemonEndpoint.ForCurrentUser();
    await using Stream? connection = await endpoint.TryConnect(cancellationToken);
    if (connection is null) {
      return null;
    }

    Welcome daemon = await DaemonStatusCommand.Client(endpoint).Greet(connection);
    return new DaemonState(daemon.Version, daemon.ProcessId, daemon.Commands);
  }

  /// Carries out the command for the current invocation and returns its exit code.
  public static async Task<int> Run(string[] args, IAnsiConsole output, IAnsiConsole error) {
    Invocation invocation = Invocation.Current;
    ConsoleUserInteraction userInteraction = new(error, invocation);

    // The configuration is read when a command first needs it, not before the arguments are
    // parsed.
    LipClient lipClient = LipClient.Create(userInteraction);

    CommandApp app = new();
    app.Configure(config => {
      config.ConfigureConsole(output);
      config.Settings.Registrar.RegisterInstance(lipClient);
      config.Settings.Registrar.RegisterInstance<IUserInteraction>(userInteraction);
      config.Settings.Registrar.RegisterInstance(new CommandOutput(invocation.Out, output));

      // An unknown option is a usage error (protocol/lip.md, "Terms").
      // Strict parsing is not used for this: when parsing fails, it retries with the placeholder
      // "__default_command" inserted between the arguments, which then fills a missing option
      // value.
      config.Settings.Registrar.RegisterInstance<ICommandInterceptor>(
          new UnexpectedArgumentsInterceptor());

      config.SetApplicationName("lip");
      config.SetApplicationVersion(DaemonProcess.Version);

      // With a resolver, the arguments are parsed, and an unexpected one is the cause of the
      // failure even when binding them failed first.
      config.SetExceptionHandler((ex, resolver) =>
          ErrorReporter.Report(
              resolver?.Resolve(typeof(IRemainingArguments)) is IRemainingArguments remaining
                  ? UnexpectedArgumentsInterceptor.Find(remaining) ?? ex
                  : ex,
              args,
              invocation.IsCancellationRequested,
              error));

      config.AddBranch("cache", cache => {
        cache.SetDefaultCommand<MissingSubcommandCommand>();
        cache.AddCommand<CacheCleanCommand>("clean")
            .WithDescription("Cleans the local cache");
      });

      config.AddBranch("config", config => {
        config.SetDefaultCommand<MissingSubcommandCommand>();
        config.AddCommand<ConfigGetCommand>("get")
            .WithDescription("Gets a configuration value");
        config.AddCommand<ConfigSetCommand>("set")
            .WithDescription("Sets a configuration value");
        config.AddCommand<ConfigListCommand>("list")
            .WithDescription("Lists all configuration values");
        config.AddCommand<ConfigDeleteCommand>("delete")
            .WithDescription("Resets a configuration value to its default");
      });

      // The daemon that carries out the other commands (protocol/lip-daemon.md).
      config.AddBranch(DaemonGroup, daemon => {
        daemon.SetDefaultCommand<MissingSubcommandCommand>();
        daemon.AddCommand<DaemonStatusCommand>("status")
            .WithDescription("Shows whether the daemon of lip runs");
        daemon.AddCommand<DaemonStopCommand>("stop")
            .WithDescription("Stops the daemon of lip");
      });

      // The commands for developing packages (protocol/lip-pkg.md).
      config.AddBranch("pkg", pkg => {
        pkg.SetDefaultCommand<MissingSubcommandCommand>();
        pkg.AddCommand<PkgInitCommand>("init")
            .WithDescription("Creates a lip.toml for a new package");
        pkg.AddCommand<PkgPackCommand>("pack")
            .WithDescription("Packs a directory into a ZIP archive");
        pkg.AddCommand<PkgViewCommand>("view")
            .WithDescription("Shows the package file and the versions of a package, as JSON");
        pkg.AddCommand<PkgMigrateCommand>("migrate")
            .WithDescription("Migrates the tooth.json in the current directory to lip.toml");
      });

      config.AddCommand<SetupCommand>("setup")
          .WithDescription("Sets up the game and the loader of the server");

      config.AddCommand<InstallCommand>("install")
          .WithDescription("Installs packages");

      config.AddCommand<ListCommand>("list")
          .WithDescription("Lists installed packages");

      config.AddCommand<RunCommand>("run")
          .WithDescription("Runs the server of the workspace");

      config.AddCommand<SelfUpdateCommand>("self-update")
          .WithDescription("Updates lip itself to its latest release");

      config.AddCommand<UninstallCommand>("uninstall")
          .WithDescription("Uninstalls packages");

      config.AddCommand<UpdateCommand>("update")
          .WithDescription("Updates packages");
    });

    // "--" is a usage error too, found before parsing (see
    // UnexpectedArgumentsInterceptor.FindSeparator).
    if (UnexpectedArgumentsInterceptor.FindSeparator(args) is { } separatorError) {
      return ErrorReporter.Report(separatorError, args, cancelled: false, error);
    }

    // So is an empty option value, even with -h (see UnexpectedArgumentsInterceptor.FindEmptyValue).
    if (UnexpectedArgumentsInterceptor.FindEmptyValue(args) is { } emptyValueError) {
      return ErrorReporter.Report(emptyValueError, args, cancelled: false, error);
    }

    try {
      return await app.RunAsync(args, invocation.Cancellation);
    }
    catch (Exception ex) {
      return ErrorReporter.Report(ex, args, invocation.IsCancellationRequested, error);
    }
  }
}
