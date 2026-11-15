using System.ComponentModel;
using System.Reflection;
using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Semver;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip self-update (protocol/lip-self-update.md).
public class SelfUpdateCommand(
    LipClient lipClient, IUserInteraction userInteraction, CommandOutput output)
    : AsyncCommand<SelfUpdateCommand.Settings> {
  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;
  private readonly CommandOutput _output = output;

  public class Settings : CommandSettings {
    [CommandOption("--check")]
    [Description("Only print the version of a newer release, without installing it")]
    public bool Check { get; init; }
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    SemVersion running = SemVersion.Parse(Assembly
        .GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion!);
    SelfUpdateResult result = await _lipClient.SelfUpdate(running, settings.Check);
    switch (result.Outcome) {
      case SelfUpdateOutcome.UpToDate:
        await _userInteraction.PrintSuccess($"lip {running} is up to date.");
        break;
      case SelfUpdateOutcome.Available:
        // The result of --check (protocol/lip-self-update.md, OPTIONS).
        _output.Out.WriteLine(result.Latest!.ToString());
        await _userInteraction.PrintInfo(
            $"lip {result.Latest} is available; run 'lip self-update' to install it.");
        break;
      case SelfUpdateOutcome.InstallerStarted:
        await _userInteraction.PrintSuccess(
            $"The installer of lip {result.Latest} is running; it replaces lip {running} when it"
            + " finishes. Close other running lip processes so that it can.");
        break;
      case SelfUpdateOutcome.Replaced:
        await _userInteraction.PrintSuccess($"Updated lip from {running} to {result.Latest}.");
        break;
    }
    return 0;
  }
}
