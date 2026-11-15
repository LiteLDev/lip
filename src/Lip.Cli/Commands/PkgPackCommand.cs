using System.ComponentModel;
using System.IO.Abstractions;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli.Commands;

public class PkgPackCommand(IUserInteraction userInteraction) : AsyncCommand<PkgPackCommand.Settings> {
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
            ? ValidationResult.Error(
                "-o, --output <FILE> is required: name the ZIP archive to write, e.g. "
                + "'lip pkg pack -o package.zip'.")
            : ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    // Relative to the working directory of the invocation (protocol/lip.md, "Working directory").
    string workingDirectory = Invocation.Current.WorkingDirectory;
    await Packer.WriteArchive(
        new FileSystem(),
        Path.GetFullPath(settings.Directory ?? ".", workingDirectory),
        Path.GetFullPath(settings.Output!, workingDirectory));
    await _userInteraction.PrintSuccess($"Packed {settings.Output}.");
    return 0;
  }
}
