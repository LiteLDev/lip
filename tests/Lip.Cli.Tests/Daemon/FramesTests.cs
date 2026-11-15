using Lip.Cli.Daemon;

namespace Lip.Cli.Tests.Daemon;

public class FramesTests {
  [Fact]
  public async Task Frames_RoundTrip_InOrder() {
    // Arrange
    using MemoryStream stream = new();
    Frames.Write(stream, FrameType.Exit, Messages.Int32(42));
    Frames.Write(stream, FrameType.Input, []);
    Frames.Write(stream, FrameType.Text, Messages.Output(Messages.StandardError, "é\n"u8));
    stream.Position = 0;

    // Act
    Frame? exit = await Frames.Read(stream, TestContext.Current.CancellationToken);
    Frame? input = await Frames.Read(stream, TestContext.Current.CancellationToken);
    Frame? text = await Frames.Read(stream, TestContext.Current.CancellationToken);
    Frame? end = await Frames.Read(stream, TestContext.Current.CancellationToken);

    // Assert
    Assert.Equal(FrameType.Exit, exit!.Value.Type);
    Assert.Equal(42, Messages.ReadInt32(exit.Value.Payload));
    Assert.Equal(FrameType.Input, input!.Value.Type);
    Assert.Empty(input.Value.Payload);
    Assert.Equal([Messages.StandardError, .. "é\n"u8.ToArray()], text!.Value.Payload);
    Assert.Null(end);
  }

  [Fact]
  public async Task Read_ConnectionEndsInsideAFrame_Throws() {
    // Arrange
    using MemoryStream full = new();
    Frames.Write(full, FrameType.Exit, Messages.Int32(1));
    using MemoryStream stream = new(full.ToArray()[..^2]);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidDataException>(
        () => Frames.Read(stream, TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Read_TooLargeFrame_Throws() {
    // Arrange
    using MemoryStream stream = new([0xFF, 0xFF, 0xFF, 0x7F, (byte)FrameType.Exit]);

    // Act & Assert
    await Assert.ThrowsAsync<InvalidDataException>(
        () => Frames.Read(stream, TestContext.Current.CancellationToken));
  }

  [Fact]
  public void Start_RoundTrips() {
    // Arrange
    Start start = new(
        ["install", "x", "--dry-run", ""],
        "/w s",
        [new("A", "1"), new("B", "")],
        1234,
        IsInputRedirected: true,
        IsOutputRedirected: false,
        IsErrorRedirected: true,
        new ConsoleProfile(true, true, 4, true, false, true, 120, 40),
        new ConsoleProfile(false, false, 0, false, false, false, 80, 24));

    // Act
    Start decoded = Start.Decode(start.Encode());

    // Assert
    Assert.Equal(start.Arguments, decoded.Arguments);
    Assert.Equal(start.WorkingDirectory, decoded.WorkingDirectory);
    Assert.Equal(start.Environment, decoded.Environment);
    Assert.Equal(
        start with { Arguments = [], Environment = [] },
        decoded with { Arguments = [], Environment = [] });
  }

  [Fact]
  public void Welcome_RoundTrips() {
    Welcome welcome = new("1.2.3-beta.1", "/opt/lip/lip", 99, 2);

    Assert.Equal(welcome, Welcome.Decode(welcome.Encode()));
    Assert.Throws<InvalidDataException>(() => Welcome.Decode([1]));
  }
}
