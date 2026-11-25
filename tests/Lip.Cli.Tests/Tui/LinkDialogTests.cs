using System.IO.Abstractions.TestingHelpers;
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.GetMods;
using Lip.Cli.Tui.Pages.Workspaces;
using Lip.Cli.Workspaces;
using Lip.Core.Catalog;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Mcmod.Lock;
using Lip.Core.PublicApi;
using Lip.Core.Services;
using Moq;

namespace Lip.Cli.Tests.Tui;

/// The interface started for a lip: link (protocol/lip-uri.md), driven by the keys of a fake
/// terminal: it asks before it installs anything, into a folder that the user chooses.
public sealed class LinkDialogTests {
  private const string Index = """
      {
        "mods": [
          {
            "id": "github.com/acme/Alpha", "name": "Alpha", "owner": "acme",
            "description": "An economy core", "side": ["server"], "platform": ["win-x64"],
            "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}, "stars": 7
          }
        ]
      }
      """;

  private const string AlphaDetail = """
      {
        "id": "github.com/acme/Alpha", "latest": {"variant": "", "version": "1.3.0"},
        "versions": [
          {
            "version": "1.3.0",
            "variants": {
              "": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.70.*"}}]
            }
          },
          {
            "version": "1.2.0",
            "variants": {
              "": [{"side": "server", "deps": {"github.com/LiteLDev/LeviLamina": "26.60.*"}}],
              "lua": [{"side": "server", "deps": {
                "github.com/LiteLDev/LeviLamina": "26.60.*",
                "github.com/LiteLDev/LegacyScriptEngine": "0.9.*"
              }}]
            }
          }
        ]
      }
      """;

  private static readonly string Server =
      OperatingSystem.IsWindows() ? @"C:\servers\alpha" : "/servers/alpha";

  private static readonly string ListPath = Path.Join(
      OperatingSystem.IsWindows() ? @"C:\state" : "/state", WorkspaceList.FileName);

  private static string Catalog(string url) =>
      url == ModCatalogClient.BaseUrl + "index.json" ? Index
          : url == ModCatalogClient.DetailUrl("github.com/acme/Alpha") ? AlphaDetail
          : throw new InvalidOperationException($"No such file: {url}");

  /// A file system whose workspace list names the workspaces given; Server has a game.
  private static MockFileSystem Files(params string[] listed) {
    MockFileSystem fs = new(new Dictionary<string, MockFileData>(), TuiHarness.DefaultRoot);
    fs.AddFile(Path.Join(Server, ".lip", "state.json"), new MockFileData(TuiHarness.StateWithGame));
    fs.AddFile(ListPath, new MockFileData(System.Text.Json.JsonSerializer.Serialize(listed)));
    return fs;
  }

  private static TuiHarness Start(
      string link, MockFileSystem fs, Mock<IArchiveProvider> archives,
      Language language = Language.En) =>
      TuiHarness.Start(new TuiHarnessOptions {
        Language = language,
        FileSystem = fs,
        Catalog = Catalog,
        Link = LipUri.Parse(link),
        // A client whose archives are those of the test: nothing is read from the network.
        CreateClient = (interaction, hookOutput, root) => new LipClient(
            fs, interaction, new CacheService(fs, interaction), new ConfigService(fs, interaction),
            new McmodService(
                fs, interaction, new LockStore(fs), archives.Object, new ArchiveReader(),
                new McmodInstaller(fs, new CommandRunner(hookOutput), interaction),
                workspaceRoot: () => root)),
      });

  [Fact]
  public async Task Link_ShowsThePackageBeforeInstalling_AndAddsNoWorkspace() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(
        "lip://install/github.com/acme/Alpha?version=1.2.0&variant=lua", Files(Server), archives);

    await tui.WaitFor(LinkText.Title);
    await tui.WaitFor(LinkText.Listed);
    Assert.Equal(PageId.GetMods, await tui.Invoke(() => tui.Context.CurrentPage));
    Assert.Contains("github.com/acme/Alpha#lua@1.2.0", tui.Screen);
    Assert.Contains("Alpha", tui.Screen);
    Assert.Contains("acme", tui.Screen);
    Assert.Contains("LeviLamina 26.60.*, LegacyScriptEngine 0.9.*", tui.Screen);
    Assert.Contains(Server, tui.Screen);
    // The working directory, which the program that opened the link chose, is not added, and
    // the workspace added last is selected.
    Assert.Equal([Server], tui.Workspaces.Read());
    Assert.Equal(Server, await tui.Invoke(() => tui.Context.Workspace));
    Assert.Empty(archives.Invocations);

    // Install plans lip install of the package: the archives of the test give nothing, so the
    // dry run fails, and nothing was installed.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor("Install github.com/acme/Alpha#lua@1.2.0 · Failed");
    Assert.NotEmpty(archives.Invocations);
  }

  [Fact]
  public async Task Link_WithoutAVersion_IsTheLatest_AndEscInstallsNothing() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start("lip://install/github.com/acme/Alpha/", Files(Server), archives);

    await tui.WaitFor(LinkText.Listed);
    Assert.Contains(LinkText.Latest("1.3.0"), tui.Screen);
    Assert.Contains("LeviLamina 26.70.*", tui.Screen);

    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone(LinkText.Title);
    await tui.Settle();
    Assert.Empty(archives.Invocations);
  }

  [Fact]
  public async Task Link_ToAPackageOutsideTheCatalogue_WarnsAndChoosesCancel() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start("lip://install/github.com/evil/Thing", Files(Server), archives);

    await tui.WaitFor("Not in the LeviMC catalogue.");
    Assert.Contains("github.com/evil/Thing", tui.Screen);
    Assert.Contains(LinkText.Unknown, tui.Screen);

    // Enter presses Cancel, which the dialog chose at first.
    tui.Press(ConsoleKey.Enter);
    await tui.WaitUntilGone(LinkText.Title);
    await tui.Settle();
    Assert.Empty(archives.Invocations);
  }

  [Fact]
  public async Task Link_WithoutAWorkspace_HasTheUserChooseOne() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start("lip://install/github.com/acme/Alpha", Files(), archives);

    await tui.WaitFor(LinkText.Listed);
    Assert.Contains(LinkText.NoWorkspace, tui.Screen);
    Assert.Empty(tui.Workspaces.Read());

    // Install does not install into a folder that the user did not choose: it opens the
    // workspace picker, as Choose folder does.
    tui.Press(ConsoleKey.LeftArrow);
    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor($"─ {WorkspacesText.Title} ─");
    tui.Press(ConsoleKey.Escape);
    await tui.WaitUntilGone($"─ {WorkspacesText.Title} ─");
    Assert.Contains(LinkText.Title, tui.Screen);
    Assert.Empty(archives.Invocations);
  }

  [Fact]
  public async Task Link_InSimplifiedChinese_SaysWhereItInstalls() {
    Mock<IArchiveProvider> archives = new(MockBehavior.Strict);
    using TuiHarness tui = Start(
        "lip://install/github.com/acme/Alpha", Files(Server), archives, Language.ZhHans);

    await tui.WaitFor("从链接安装");
    await tui.WaitFor("已收录于 LeviMC 模组目录。");
    Assert.Contains("安装到", tui.Screen);
  }
}
