using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Tools;
using Lip.Core.Catalog;
using Lip.Core.Json;

namespace Lip.Cli.Tests.Tui;

/// The lip.toml editor of the Tools page, driven by the keys of a fake terminal, with a
/// catalogue and a server setup of the test: nothing is read from the network.
public sealed partial class ManifestEditorTests {
  private const string Index = """
      { "mods": [ {
        "id": "github.com/acme/Alpha", "name": "Alpha", "owner": "acme",
        "description": "An economy core", "tags": ["type:mod"], "side": ["server"],
        "platform": ["win-x64"], "stars": 7
      } ] }
      """;

  // Alpha 1.4.3 is for LeviLamina 1.1, 1.4.2 for 1.0.
  private const string AlphaDetail = """
      { "id": "github.com/acme/Alpha", "versions": [
        { "version": "1.4.3", "variants": { "": [ { "side": "server",
          "deps": { "github.com/LiteLDev/LeviLamina": "1.1.*" } } ] } },
        { "version": "1.4.2", "variants": { "": [ { "side": "server",
          "deps": { "github.com/LiteLDev/LeviLamina": "1.0.*" } } ] } },
        { "version": "1.3.0", "variants": { "": [ { "side": "server",
          "deps": { "github.com/LiteLDev/LeviLamina": "1.0.*" } } ] } }
      ] }
      """;

  private const string LoaderDetail = """
      { "id": "github.com/LiteLDev/LeviLamina", "versions": [
        { "version": "1.1.0", "variants": { "": [ { "side": "server",
          "deps": { "github.com/LiteLDev/bds": "1.21.50" } } ] } },
        { "version": "1.0.0", "variants": { "": [ { "side": "server",
          "deps": { "github.com/LiteLDev/bds": "1.21.40" } } ] } }
      ] }
      """;

  // A lip.toml with comments and keys that the editor does not know.
  private const string Commented = """
      # My package.
      format = 4
      id = "github.com/me/pack" # where it lives
      version = "1.0.0"
      homepage = "https://example.com"

      [[variants.""]]
      side = "server"
      platform = "win-x64"
      x-note = "kept"

      # What it needs.
      [variants."".deps]
      "github.com/LiteLDev/LeviLamina" = "~1.0.0"

      """;

  private static string Catalog(string url) =>
      url == ModCatalogClient.BaseUrl + "index.json" ? Index
          : url == ModCatalogClient.DetailUrl("github.com/acme/Alpha") ? AlphaDetail
          : url == ModCatalogClient.DetailUrl("github.com/LiteLDev/LeviLamina") ? LoaderDetail
          : "{}";

  private static string InRoot(string name) => Path.Join(TuiHarness.DefaultRoot, name);

  private static TuiHarness Start(MockFileSystem? fs = null, int width = 120, int height = 36) =>
      TuiHarness.Start(new TuiHarnessOptions {
        FileSystem = fs,
        Width = width,
        Height = height,
        Catalog = Catalog,
        ServerSetup = harness => new FakeServerSetup(harness),
      });

  /// Moves the focus down to the row whose label starts with the text.
  private static async Task Focus(TuiHarness tui, string label) {
    Regex focused = new($@"> +{Regex.Escape(label)}");
    for (int i = 0; i < 60; i++) {
      await tui.Settle();
      if (focused.IsMatch(tui.Screen)) {
        return;
      }

      tui.Press(ConsoleKey.DownArrow);
    }

    Assert.Fail($"No row {label}:\n{tui.Screen}");
  }

  /// Writes the frame at 120 and at 80 columns when LIP_TUI_DUMP is set, and to the output of
  /// the test.
  private static async Task Frames(TuiHarness tui, string name) {
    await tui.Settle();
    TestContext.Current.TestOutputHelper?.WriteLine($"=== {name} ===\n{tui.Screen}");
    if (Environment.GetEnvironmentVariable("LIP_TUI_DUMP") is not { Length: > 0 }) {
      return;
    }

    tui.Snapshot(name);
    tui.Resize(80, 24);
    await tui.WaitFor(_ => tui.App.ScreenText.Split('\n').Length == 24);
    await tui.Settle();
    tui.Snapshot(name);
    tui.Resize(120, 36);
    await tui.WaitFor(_ => tui.App.ScreenText.Split('\n').Length == 36);
    await tui.Settle();
  }

  private static JsonElement Read(string toml) {
    using JsonDocument document = TomlJson.Parse(toml);
    return document.RootElement.Clone();
  }

  [Fact]
  public async Task Create_FillsTheForm_AddsADependencyFromTheMarketplace_AndSaves() {
    using TuiHarness tui = Start();
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ToolsText.Missing);
    Assert.Contains(ToolsText.Init, tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.IdHelp);
    Assert.Contains(ManifestEditorText.CreateTitle, tui.Screen);
    // The package id is required: it is marked until it is typed.
    await tui.WaitFor(ManifestEditorText.Problems(1));
    await Frames(tui, "manifest-create");

    tui.Type("github.com/me/pack");
    await tui.WaitFor(ManifestEditorText.AllValid);

    // A version with a leading v is marked, with why, until it is fixed.
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Backspace, 5);
    tui.Type("v1.0.0");
    await tui.WaitFor(ManifestEditorText.Problem(
        new Core.Mcmod.Editing.DraftProblem(Core.Mcmod.Editing.DraftProblemKind.LeadingV, "1.0.0")));
    Assert.Matches(@"v1\.0\.0\s+✗", tui.Screen);
    await Frames(tui, "manifest-invalid");
    tui.Press(ConsoleKey.Home);
    tui.Press(ConsoleKey.Delete);
    await tui.WaitFor(ManifestEditorText.AllValid);
    Assert.Matches(@"1\.0\.0\s+✓", tui.Screen);

    // A dependency from the Marketplace, at a version, with the recommended range.
    await Focus(tui, ManifestEditorText.Dependencies);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.FromMarketplace);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("An economy core");
    tui.Type("alp");
    await Frames(tui, "manifest-marketplace");
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.VersionTitle("github.com/acme/Alpha"));
    await tui.WaitFor("1.4.3");
    await Frames(tui, "manifest-version");
    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.RangeTitle("github.com/acme/Alpha"));
    Assert.Contains("~1.4.2", tui.Screen);
    Assert.Contains(ManifestEditorText.Matches(2, "1.4.3"), tui.Screen);
    await Frames(tui, "manifest-range");
    tui.Press(ConsoleKey.Enter);

    // The dependency is checked online, in the background.
    await tui.WaitFor(ManifestEditorText.Remote(new Core.Mcmod.Editing.RemoteCheck(
        Core.Mcmod.Editing.RemoteStatus.Valid, Core.Mcmod.Editing.RemoteReason.NewestInRange,
        "1.4.3")));
    Assert.Matches(@"github\.com/acme/Alpha\s+~1\.4\.2\s+✓", tui.Screen);
    await tui.WaitFor(ManifestEditorText.AllValid);
    await Frames(tui, "manifest-dependency");

    // Save asks first, with what is expected for the BDS and the LeviLamina chosen.
    tui.Press(ConsoleKey.S, modifiers: ConsoleModifiers.Control);
    await tui.WaitFor(ManifestEditorText.Prediction);
    await tui.WaitFor("Would install 1.4.3.");
    Assert.Contains("+ id = \"github.com/me/pack\"", tui.Screen);
    Assert.Contains("‹ 1.21.50 ›", tui.Screen);
    Assert.Contains("‹ 1.1.0 ›", tui.Screen);
    await Frames(tui, "manifest-save");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("Would install 1.4.2.");
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("‹ None ›");
    await tui.WaitFor("No version in its range fits these versions (newest 1.4.3).");
    await Frames(tui, "manifest-save-no-loader");
    tui.Press(ConsoleKey.Enter);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ManifestEditorText.Saved("lip.toml"));
    await tui.WaitFor(ManifestEditorText.Edit);
    JsonElement written = Read(tui.Fs.File.ReadAllText(InRoot("lip.toml")));
    Assert.Equal("github.com/me/pack", written.GetProperty("id").GetString());
    Assert.Equal("1.0.0", written.GetProperty("version").GetString());
    JsonElement entry = written.GetProperty("variants").GetProperty("")[0];
    Assert.Equal("server", entry.GetProperty("side").GetString());
    Assert.Equal(
        "~1.4.2",
        entry.GetProperty("deps").GetProperty("github.com/acme/Alpha").GetString());
  }

  [Fact]
  public async Task Edit_ChangesTheVersion_AndKeepsCommentsAndOtherKeys() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData(Commented));
    using TuiHarness tui = Start(fs);
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ManifestEditorText.Edit);
    Assert.Contains(ManifestEditorText.EditAbout, tui.Screen);

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.EditTitle + "  " + InRoot("lip.toml"));
    // The dependency that the file has is checked online too.
    await tui.WaitFor(ManifestEditorText.AllValid);
    Assert.Matches(@"github\.com/LiteLDev/LeviLamina\s+~1\.0\.0\s+✓", tui.Screen);
    await Frames(tui, "manifest-edit");

    tui.Press(ConsoleKey.DownArrow);
    tui.Press(ConsoleKey.Backspace, 5);
    tui.Type("1.1.0");
    await tui.WaitFor(_ => Regex.IsMatch(tui.Screen, @"1\.1\.0\s+✓"), "the new version");
    tui.Press(ConsoleKey.S, modifiers: ConsoleModifiers.Control);
    await tui.WaitFor(ManifestEditorText.KeepsText);
    Assert.Contains("~ version: \"1.0.0\" → \"1.1.0\"", tui.Screen);
    // The LeviLamina offered first is the newest in the range, which is not for BDS 1.21.50;
    // 1.1.0 is not in the range; for BDS 1.21.40, the one in the range fits.
    await tui.WaitFor("This LeviLamina is not for this BDS.");
    Assert.Contains("‹ 1.0.0 ›", tui.Screen);
    await Frames(tui, "manifest-edit-save");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.LeftArrow);
    await tui.WaitFor("Needs LeviLamina ~1.0.0.");
    tui.Press(ConsoleKey.UpArrow);
    tui.Press(ConsoleKey.RightArrow);
    await tui.WaitFor("‹ 1.21.40 ›");
    await tui.WaitFor("This LeviLamina is in ~1.0.0.");
    Assert.Contains("‹ 1.0.0 ›", tui.Screen);
    await Frames(tui, "manifest-edit-save-fits");
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Tab);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ManifestEditorText.Saved("lip.toml"));
    Assert.Equal(
        Commented.Replace("version = \"1.0.0\"", "version = \"1.1.0\""),
        fs.File.ReadAllText(InRoot("lip.toml")));
  }

  [Fact]
  public async Task Edit_EscWithChanges_AsksBeforeDiscarding() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData(Commented));
    using TuiHarness tui = Start(fs);
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ManifestEditorText.Edit);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ManifestEditorText.EditTitle + "  " + InRoot("lip.toml"));

    tui.Type("x");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(CommonText.DiscardQuestion);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(CommonText.DiscardQuestion);
    Assert.Contains(ManifestEditorText.EditTitle + "  " + InRoot("lip.toml"), tui.Screen);
    tui.Press(ConsoleKey.Escape);
    await tui.WaitFor(CommonText.DiscardQuestion);
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ManifestEditorText.EditAbout);
    Assert.Equal(Commented, fs.File.ReadAllText(InRoot("lip.toml")));
  }

  [Fact]
  public async Task Edit_FileThatIsNotTomlOfThePackage_IsNotOpened() {
    MockFileSystem fs = new();
    fs.AddFile(InRoot("lip.toml"), new MockFileData("version = 1"));
    using TuiHarness tui = Start(fs);
    await tui.GoTo(PageId.Tools);
    await tui.WaitFor(ManifestEditorText.Edit);

    tui.Press(ConsoleKey.Enter);

    await tui.WaitFor(ManifestEditorText.CannotEdit);
    Assert.Contains("version is not a string", tui.Screen);
  }
}
