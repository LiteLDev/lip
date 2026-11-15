using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Lip.Cli.Tui;

namespace Lip.Cli.Tests.Tui;

/// The workspace list (protocol/lip-tui.md, "Workspaces").
public sealed class WorkspaceListTests {
  private static readonly string _state = OperatingSystem.IsWindows() ? @"C:\state" : "/state";
  private static readonly string _a = OperatingSystem.IsWindows() ? @"C:\ws\a" : "/ws/a";
  private static readonly string _b = OperatingSystem.IsWindows() ? @"C:\ws\b" : "/ws/b";

  private readonly MockFileSystem _fs = new();
  private readonly string _path = Path.Join(_state, WorkspaceList.FileName);

  [Fact]
  public void Add_WritesTheListAtomicallyAsAJsonArrayInOrder() {
    // Arrange
    WorkspaceList list = new(_fs, _path);

    // Act
    list.Add(_b);
    list.Add(_a + Path.DirectorySeparatorChar);
    list.Add(_b);

    // Assert
    Assert.Equal([_b, _a], list.Read());
    Assert.Equal([_b, _a], Parse(_fs.File.ReadAllText(_path)));
    Assert.Equal([_path], _fs.Directory.GetFiles(_state));
    // Another list of the same file, as of another interface, reads it.
    Assert.Equal([_b, _a], new WorkspaceList(_fs, _path).Read());
  }

  [Fact]
  public void Add_OnWindows_IgnoresTheCaseOfAListedPath() {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Paths differ by case elsewhere.");
    WorkspaceList list = new(_fs, _path);
    list.Add(_a);

    string listed = list.Add(_a.ToUpperInvariant());

    Assert.Equal(_a, listed);
    Assert.Equal([_a], list.Read());
  }

  [Fact]
  public void Remove_ChangesTheListAndNoFileOfTheWorkspace() {
    // Arrange
    _fs.AddFile(Path.Join(_a, "lip.toml"), new MockFileData("format = 4\n"));
    WorkspaceList list = new(_fs, _path);
    list.Add(_a);
    list.Add(_b);

    // Act
    list.Remove(_a);

    // Assert
    Assert.Equal([_b], list.Read());
    Assert.Equal("format = 4\n", _fs.File.ReadAllText(Path.Join(_a, "lip.toml")));
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("{\"workspaces\": []}")]
  public void Read_FileThatIsNotAnArray_IsAnEmptyList(string text) {
    _fs.AddFile(_path, new MockFileData(text));

    Assert.Empty(new WorkspaceList(_fs, _path).Read());
  }

  [Fact]
  public void Read_LeavesOutWhatIsNotAnAbsolutePathAndRepeats() {
    _fs.AddFile(_path, new MockFileData(JsonSerializer.Serialize(
        new object[] { _a, "relative", 3, _a, _b })));

    Assert.Equal([_a, _b], new WorkspaceList(_fs, _path).Read());
  }

  [Fact]
  public void Read_WithoutAFile_IsAnEmptyList() {
    Assert.Empty(new WorkspaceList(_fs, _path).Read());
    Assert.False(_fs.Directory.Exists(_state));
  }

  private static string[] Parse(string json) =>
      [.. JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.GetString()!)];
}
