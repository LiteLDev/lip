using System.ComponentModel;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.PublicApi;
using Semver;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

/// lip setup (protocol/lip-setup.md).
public class SetupCommand(LipClient lipClient, IUserInteraction userInteraction)
    : AsyncCommand<SetupCommand.Settings> {
  private const string LoaderName = "levilamina";

  private readonly LipClient _lipClient = lipClient;
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandOption("--game <VERSION>")]
    [Description("The version of the game (Minecraft Bedrock Dedicated Server), e.g. 1.21.0")]
    public string? Game { get; init; }

    [CommandOption("--build <BUILD>")]
    [Description("The build of the server: windows (the default) or linux")]
    public string? Build { get; init; }

    [CommandOption("--loader <LOADER>")]
    [Description("The loader: levilamina (the newest version for the game) or"
        + " levilamina@<version>; the default for the build windows")]
    public string? Loader { get; init; }

    [CommandOption("--no-loader")]
    [Description("Set up the game without a loader; the default for the build linux")]
    public bool NoLoader { get; init; }

    [CommandOption("--accept-eula")]
    [Description("Accept the Minecraft EULA and the Privacy Policy without being asked")]
    public bool AcceptEula { get; init; }

    [CommandOption("-n|--dry-run")]
    [Description("Run without making any changes")]
    public bool DryRun { get; init; }

    public override ValidationResult Validate() {
      try {
        _ = Request();
        return ValidationResult.Success();
      }
      catch (FormatException ex) {
        return ValidationResult.Error(ex.Message);
      }
    }

    /// The setup the options ask for (protocol/lip-setup.md, OPTIONS). Throws FormatException
    /// for a usage error.
    public SetupRequest Request() {
      if (string.IsNullOrEmpty(Game)) {
        throw new FormatException(
            "--game <VERSION> is required: name the version of the game, e.g."
            + " 'lip setup --game 1.21.0'.");
      }

      if (!SemVersion.TryParse(Game, SemVersionStyles.Strict, out SemVersion? game)) {
        throw new FormatException(
            $"--game '{Game}' is not a version: write it like 1.21.0, without a leading 'v'.");
      }

      GameBuild build = Build switch {
        null or "windows" => GameBuild.Windows,
        "linux" => GameBuild.Linux,
        _ => throw new FormatException(
            $"--build '{Build}' is not a build: it is windows or linux."),
      };

      if (Loader is not null && NoLoader) {
        throw new FormatException("--loader and --no-loader cannot be given together.");
      }

      if (Loader is null) {
        return new SetupRequest(game, build, Loader: !NoLoader && build == GameBuild.Windows);
      }

      string[] parts = Loader.Split('@', 2);
      SemVersion? loaderVersion = null;
      if (!parts[0].Equals(LoaderName, StringComparison.OrdinalIgnoreCase)
          || parts.Length == 2
          && !SemVersion.TryParse(parts[1], SemVersionStyles.Strict, out loaderVersion)) {
        throw new FormatException(
            $"--loader '{Loader}' is not a loader: it is levilamina, or levilamina@<version>"
            + " such as levilamina@1.0.0.");
      }

      return new SetupRequest(game, build, Loader: true, loaderVersion);
    }
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await _lipClient.Setup(
        settings.Request(), settings.DryRun, cancellationToken, settings.AcceptEula);
    if (settings.DryRun) {
      await _userInteraction.PrintInfo("Dry run: nothing was changed.");
    } else {
      await _userInteraction.PrintSuccess(
          "The server is set up. Run it with 'lip run', or install mods with 'lip install'.");
    }

    return 0;
  }
}
