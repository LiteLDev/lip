using Lip.Cli.Daemon;

namespace Lip.Cli.Tests.Daemon;

public class TerminalSizeTests {
  [Theory]
  [InlineData(0)]
  [InlineData(123456789)]
  [InlineData(long.MaxValue - 1000)]
  public void Changed_GivesTheSizeAtOnce_AndAgainWheneverItChanges(long start) {
    long time = start;
    (int, int) terminal = (120, 30);
    TerminalSize size = new(() => time, () => terminal);

    Assert.Equal((120, 30), size.Changed());
    Assert.Null(size.Changed());

    // The terminal is made larger, then smaller: each size is given once it is read again.
    terminal = (186, 47);
    time += 200;
    Assert.Equal((186, 47), size.Changed());
    time += 200;
    Assert.Null(size.Changed());
    terminal = (104, 25);
    time += 200;
    Assert.Equal((104, 25), size.Changed());
  }

  [Fact]
  public void Changed_ReadsTheSizeNoSoonerThanAfter200Milliseconds() {
    long time = 5000;
    int reads = 0;
    TerminalSize size = new(() => time, () => (80 + reads++, 24));

    Assert.Equal((80, 24), size.Changed());
    time += 199;
    Assert.Null(size.Changed());
    Assert.Equal(1, reads);
    time += 1;
    Assert.Equal((81, 24), size.Changed());
  }
}
