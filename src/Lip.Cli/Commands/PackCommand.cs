using System.ComponentModel;
using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class PackCommand(IUserInteraction userInteraction) : AsyncCommand<PackCommand.Settings> {
  private readonly IUserInteraction _userInteraction = userInteraction;

  public class Settings : CommandSettings {
    [CommandArgument(0, "[DIR]")]
    [Description("The directory whose files go into the archive (default: the current directory)")]
    public string? Directory { get; init; }

    [CommandOption("-o|--output <FILE>")]
    [Description("The ZIP archive to write")]
    public string? Output { get; init; }

    public override ValidationResult Validate() =>
        string.IsNullOrEmpty(Output)
            ? ValidationResult.Error("-o, --output <FILE> is required.")
            : ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    await Packer.WriteArchive(new FileSystem(), settings.Directory ?? ".", settings.Output!);
    await _userInteraction.PrintSuccess($"Packed {settings.Output}.");
    return 0;
  }
}
