using System.Reflection;
using Lip.Cli;
using Lip.Cli.Commands;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Lip.Core.PublicApi;
using Semver;
using Spectre.Console.Cli;

RunTrace.TraceHttp();

ConsoleUserInteraction userInteraction = new();

// Ctrl+C while an operation changes a workspace cancels it, which undoes its changes. Otherwise,
// and on a second Ctrl+C, lip ends at once: nothing is changed yet, or the next operation on the
// workspace undoes the changes (see WorkspaceTransaction).
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) => {
  if (!WorkspaceTransaction.AnyOpen || cancellation.IsCancellationRequested) {
    return;
  }

  e.Cancel = true;
  Console.Error.WriteLine("Cancelling... (press Ctrl+C again to exit at once)");
  cancellation.Cancel();
};

// The configuration is read when a command first needs it, not before the arguments are parsed.
LipClient lipClient = LipClient.Create(userInteraction);

CommandApp app = new();

app.Configure(config => {
  config.Settings.Registrar.RegisterInstance(lipClient);
  config.Settings.Registrar.RegisterInstance<IUserInteraction>(userInteraction);

  // An unknown option is a usage error (protocol/lip.md, "Terms").
  // Strict parsing is not used for this: when parsing fails, it retries with the placeholder
  // "__default_command" inserted between the arguments, which then fills a missing option value.
  config.Settings.Registrar.RegisterInstance<ICommandInterceptor>(
      new UnexpectedArgumentsInterceptor());

  config.SetApplicationName("lip");

  config.SetApplicationVersion(SemVersion.Parse(Assembly
      .GetEntryAssembly()?
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion!).ToString());

  // With a resolver, the arguments are parsed, and an unexpected one is the cause of the failure
  // even when binding them failed first.
  config.SetExceptionHandler((ex, resolver) =>
      ErrorReporter.Report(
          resolver?.Resolve(typeof(IRemainingArguments)) is IRemainingArguments remaining
              ? UnexpectedArgumentsInterceptor.Find(remaining) ?? ex
              : ex,
          args,
          cancellation.IsCancellationRequested));

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

  config.AddCommand<InitCommand>("init")
      .WithDescription("Creates a lip.toml for a new package");

  config.AddCommand<InstallCommand>("install")
      .WithDescription("Installs packages");

  config.AddCommand<ListCommand>("list")
      .WithDescription("Lists installed packages");

  config.AddCommand<MigrateCommand>("migrate")
      .WithDescription("Migrates the tooth.json in the current directory to lip.toml");

  config.AddCommand<PackCommand>("pack")
      .WithDescription("Packs a directory into a ZIP archive");

  config.AddCommand<UninstallCommand>("uninstall")
      .WithDescription("Uninstalls packages");

  config.AddCommand<UpdateCommand>("update")
      .WithDescription("Updates packages");

  config.AddCommand<ViewCommand>("view")
      .WithDescription("Views package details");

  config.AddCommand<VersionsCommand>("versions")
      .WithDescription("Shows available versions for a package");
});

// "--" is a usage error too, found before parsing (see
// UnexpectedArgumentsInterceptor.FindSeparator).
if (UnexpectedArgumentsInterceptor.FindSeparator(args) is { } separatorError) {
  return ErrorReporter.Report(separatorError, args, cancelled: false);
}

try {
  return await app.RunAsync(args, cancellation.Token);
}
catch (Exception ex) {
  return ErrorReporter.Report(ex, args, cancellation.IsCancellationRequested);
}
