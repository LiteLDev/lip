using System.Text;
using Spectre.Console;

namespace Lip.Cli;

/// What a standard stream of lip is, as Spectre.Console finds it: whether it is a terminal, what
/// that terminal can show, and its size. The daemon writes for a client as its consoles would
/// (protocol/lip-daemon.md, "Carrying out a command").
public sealed record ConsoleProfile(
    bool IsTerminal,
    bool Ansi,
    int ColorSystem,
    bool Unicode,
    bool Links,
    bool Interactive,
    int Width,
    int Height) {
  /// The console of the standard output of this process, as lip has always written results.
  public static IAnsiConsole CreateStandardOutput() =>
      AnsiConsole.Create(new AnsiConsoleSettings());

  /// The console of the standard error of this process, as lip has always written messages.
  public static IAnsiConsole CreateStandardError() =>
      AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });

  /// The size that Spectre.Console takes for a console whose size it cannot find.
  private const int DefaultWidth = 80;
  private const int DefaultHeight = 24;

  /// The profile of a console of this process. A size that is not positive, as .NET finds for
  /// no terminal with TERM=dumb, is the default size, which a console of the daemon can take.
  public static ConsoleProfile Of(IAnsiConsole console) {
    Profile profile = console.Profile;
    return new ConsoleProfile(
        profile.Out.IsTerminal,
        profile.Capabilities.Ansi,
        (int)profile.Capabilities.ColorSystem,
        profile.Capabilities.Unicode,
        profile.Capabilities.Links,
        profile.Capabilities.Interactive,
        profile.Width > 0 ? profile.Width : DefaultWidth,
        profile.Height > 0 ? profile.Height : DefaultHeight);
  }

  /// A console that writes to writer as a console of this profile would.
  public IAnsiConsole CreateConsole(TextWriter writer) {
    IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
      Ansi = Ansi ? AnsiSupport.Yes : AnsiSupport.No,
      ColorSystem = (ColorSystemSupport)ColorSystem,
      Interactive = Interactive ? InteractionSupport.Yes : InteractionSupport.No,
      Out = new Output(writer, this),
      // What the client found is final: the variables and the enrichers of the process that
      // carries the command out must not change it.
      EnvironmentVariables = new Dictionary<string, string>(),
      Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
    });
    console.Profile.Capabilities.Unicode = Unicode;
    console.Profile.Capabilities.Links = Links;
    console.Profile.Width = Width;
    console.Profile.Height = Height;
    return console;
  }

  private sealed class Output(TextWriter writer, ConsoleProfile profile) : IAnsiConsoleOutput {
    public TextWriter Writer => writer;

    public bool IsTerminal => profile.IsTerminal;

    public int Width => profile.Width;

    public int Height => profile.Height;

    public void SetEncoding(Encoding encoding) {
    }
  }
}
