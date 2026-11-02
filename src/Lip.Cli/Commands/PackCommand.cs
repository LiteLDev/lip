using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
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

    [CommandOption("--variant <VARIANT>")]
    [Description("The variant to pack (default: \"\")")]
    public string Variant { get; init; } = "";

    [CommandOption("--platform <PLATFORM>")]
    [Description("The platform to pack for: server or client (default: server)")]
    public string Platform { get; init; } = "server";

    [CommandOption("--rid <RID>")]
    [Description("The .NET runtime identifier to pack for (default: the one lip runs on)")]
    public string? Rid { get; init; }

    [CommandOption("-o|--output <FILE>")]
    [Description("Writes a ZIP archive instead of printing the package document")]
    public string? Output { get; init; }

    public override ValidationResult Validate() =>
        McmodPlatformExtensions.TryParse(Platform, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("--platform must be server or client.");
  }

  public override async Task<int> ExecuteAsync(
      CommandContext context, Settings settings, CancellationToken cancellationToken) {
    FileSystem fs = new();
    string path = ManifestFormat.ManifestFileName;
    if (!fs.File.Exists(path)) {
      throw new InvalidOperationException($"There is no {path} in the current directory.");
    }

    _ = McmodPlatformExtensions.TryParse(settings.Platform, out McmodPlatform platform);
    LipEnvironment environment = new(
        platform, settings.Rid is { Length: > 0 } rid ? rid : RuntimeInformation.RuntimeIdentifier);

    System.Text.Json.Nodes.JsonObject document = Packer.Pack(
        await fs.File.ReadAllBytesAsync(path, cancellationToken), settings.Variant, environment);

    if (settings.Output is null) {
      // Written as is: Spectre.Console would wrap long lines, breaking JSON strings apart.
      await Console.Out.WriteAsync(System.Text.Encoding.UTF8.GetString(Packer.Serialize(document)));
      return 0;
    }

    await Packer.WriteArchive(fs, settings.Directory ?? ".", settings.Output, document);
    await _userInteraction.PrintSuccess($"Packed {settings.Output}.");
    return 0;
  }
}
