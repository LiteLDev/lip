using System.Text;
using Spectre.Console;

namespace Lip.Cli.Tests;

/// The profile that a client sends the daemon, and the console the daemon writes with.
public class ConsoleProfileTests {
  [Fact]
  public void Of_ConsoleOfNoSize_HasTheDefaultSize() {
    // Arrange: as .NET finds the size with no terminal and TERM=dumb.
    IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
      Out = new SizedOutput(0, 0),
    });

    // Act
    ConsoleProfile profile = ConsoleProfile.Of(console);

    // Assert
    Assert.Equal(80, profile.Width);
    Assert.Equal(24, profile.Height);
    _ = profile.CreateConsole(new StringWriter());
  }

  [Fact]
  public void Of_ConsoleOfASize_KeepsIt() {
    // Arrange
    IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
      Out = new SizedOutput(120, 40),
    });

    // Act
    ConsoleProfile profile = ConsoleProfile.Of(console);

    // Assert
    Assert.Equal(120, profile.Width);
    Assert.Equal(40, profile.Height);
  }

  private sealed class SizedOutput(int width, int height) : IAnsiConsoleOutput {
    public TextWriter Writer { get; } = new StringWriter();

    public bool IsTerminal => false;

    public int Width => width;

    public int Height => height;

    public void SetEncoding(Encoding encoding) {
    }
  }
}
