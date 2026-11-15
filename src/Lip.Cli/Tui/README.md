# The interface of lip

This directory is the interactive interface that `lip` runs without arguments
(`protocol/lip-tui.md`). This guide is for whoever writes a page.

```
Tui/
  Terminal.cs, Screen.cs      primitives: ITerminal, ScreenBuffer, Rect, Style, Theme, TextWidth
  App.cs, App.Operations.cs   the shell: event loop, drawing, sidebar, focus, dialogs, operations
  AppContext.cs               what a page may use of the shell (Context)
  Page.cs                     the base class of a page, and Hint
  Pages.cs                    PageId, PageRequest, PageRegistry: the only list of the pages
  Operation.cs                Operation, ProgressItem, DaemonState, ToastKind
  ServerSetup.cs              IServerSetup: game versions, loader versions, lip setup
  I18n/L.cs                   Language, L.T, Languages
  I18n/CommonText.cs          shared text: buttons, hints, common words
  Widgets/                    ListView, ButtonRow, Form, TextField, TextViewer, dialogs, Ui
  Pages/<Name>/<Name>Page.cs  one directory per page
  Pages/<Name>/<Name>Text.cs  the text of that page
```

`Servers/` (managed servers, `AnsiLog`) and `Workspaces/` (`WorkspaceList`) are not part of the
interface: `lip run --managed` and the daemon use them too.

## The screen

```
 lip 1.2.3 · D:\servers\survival                         ● Running     top bar (shell)
┌────────────┐┌─ Mods ──────────────────────────────────────────────┐
│ Home       ││                                                     │
│ Server   ● ││   the page draws here: Draw(screen, area, focused)  │
│ Mods       ││                                                     │
│ …          ││                                                     │
└────────────┘└─────────────────────────────────────────────────────┘
 ↑↓ Move  Enter Actions  Space Check  Esc Back            Saved.       hints (shell) · toast
```

The shell draws the top bar, the sidebar, the box of the content area with the title of the
page, the bottom bar, and the dialogs. A page draws only inside `area`.

- The terminal is at least 60×16; below that the shell shows a message, no page is drawn, and
  the only key that works is Ctrl+C, which quits.
- On a terminal narrower than 80 columns the sidebar shows icons while the page has the focus,
  and names while the sidebar has it. So `area` is at least **51×12** while the page has the
  focus, and may be as narrow as about 36 columns while the sidebar has it. Lay out for the
  size you are given; never assume 120×36.
- Write with `screen.WriteFit(x, y, width, text, style)` whenever the text may be longer than
  its room, and measure with `TextWidth.Of`, never `string.Length`: CJK characters take two
  columns.

## Interaction rules

These hold on every page. Tests enforce some of them; reviewers check the rest.

| Key | Does |
|---|---|
| ↑ ↓ | Move in a list or a menu |
| ← → | Sidebar ↔ content, between panes or buttons side by side, change the value of an option |
| Enter | Open or carry out the selected item; on a row of a list, open its action menu |
| Space | Check (multi-select), switch a toggle |
| Esc | Back one level: dialog → content → sidebar; in the sidebar, ask to quit |
| Tab / Shift+Tab | Next / previous focus area, round the sidebar and the areas of the page |
| PgUp PgDn Home End | Page through a list or a text |
| Characters | Only where a text field has the focus |

- **Nothing is reachable only by a letter key.** There are no letter shortcuts at all, not even
  as an extra. Every action is a visible row or button that the arrow keys reach. The one
  exception is Ctrl+C: on the Server page while the server runs it stops the server, and
  everywhere else it asks to quit (the shell does that when the page does not take the key).
- Esc goes back one level, never further. Inside a page that is from an inner area to the one
  it was entered from (the tree of a file → the files, the input line of the server → its
  output), and only from the outermost one to the sidebar.
- Tab goes round: sidebar → first area of the page → … → last area → sidebar; Shift+Tab the
  other way. So a page returns false for Tab at its last area and for Shift+Tab at its first,
  and overrides `OnTabIn` to take the focus at its first or last area when Tab comes from the
  sidebar. Enter and → in the sidebar give the page the focus where it was.
- The bottom bar lists exactly the keys that work now. A page returns them from `Hints`; a
  dialog from its own `Hints`. Take them from `CommonText.Hint…` where one fits.
  - Every key that does something of its own is listed, and no key that does nothing now:
    `↑↓ Scroll` only while the text scrolls, `Tab Next` only while the page has more than one
    area that takes the focus.
  - Two keys that do the same are one hint: `Enter/Space Toggle`, `↑↓/Tab Move`,
    `←→/Tab Switch`. Not listed apart: Space where it presses what Enter presses (a button, a
    row of a menu), and ← or Tab where they leave the page as `Esc Back` does.
  - Order them by how much they matter. The bar shows a hint whole or not at all; where they
    do not all fit, it keeps the last one, Esc, and leaves the others out from the end. So the
    keys that page (`ListView.PageHints`, `PgUp/PgDn Page`, `Home/End Ends`) go last.
- Every workspace operation goes through `Context.PlanAndRun`: preview (dry run) →
  `[Apply] [Cancel]` → the license when needed → progress → result. Cancel is "preview only".

### Copy rules

- Titles are at most three words.
- A label carries no explanation.
- An explanation is one sentence, shown only in a details area for the selected item (for
  example `ChoiceItem.Detail`).
- A placeholder gives one example (`my-server`).
- Only the text of the interface is translated. What a command says — the messages of
  `Lip.Core`, the lines of a dry run, errors — is shown as it is, in English.

## Writing a page

A page is a class in `Pages/<Name>/` in the namespace `Lip.Cli.Tui.Pages.<Name>`, derived from
`Page`. The registry in `Pages.cs` already lists all nine; do not edit it.

```csharp
namespace Lip.Cli.Tui.Pages.Mods;

public sealed class ModsPage(AppContext context) : Page(context) {
  private readonly ListView<InstalledPackage> _list = new(Row) {
    MultiSelect = true,
    Empty = () => ModsText.Empty,
  };
  private bool _loaded;

  public override string Title => ModsText.Title;

  public override IReadOnlyList<Hint> Hints =>
      [.. _list.Hints, CommonText.HintActions, .. _list.PageHints];

  public override void OnShown() => Load();

  public override void OnWorkspaceChanged() {
    _loaded = false;
    if (Context.CurrentPage == PageId.Mods) {
      Load();
    }
  }

  public override void Draw(ScreenBuffer screen, Rect area, bool focused) =>
      _list.Draw(screen, area, focused);

  public override bool OnKey(ConsoleKeyInfo key) {
    if (key.Key == ConsoleKey.Enter && _list.HasCurrent) {
      Context.ShowMenu(
          _list.Current.Identity,
          new MenuItem(ModsText.Update, Update),
          new MenuItem(ModsText.Uninstall, Uninstall, Enabled: _list.Current.Explicit));
      return true;
    }

    return _list.OnKey(key);
  }

  private void Load() =>
      Context.Background(() => Context.Client.ListPackages(), packages => {
        _list.Items = packages;
        _loaded = true;
      });
}
```

What `Page` gives you (all called on the thread of the interface):

| Member | When / what |
|---|---|
| `Title` | The entry of the sidebar and the title of the content box. From `XxxText`. |
| `Badge` | A short mark after the sidebar entry (`●` while the server runs). |
| `Hints` | The keys that work now, the most important first. Leave out Esc when Esc just goes back: the shell adds `Esc Back`. |
| `Draw(screen, area, focused)` | Draw inside `area`. `focused` is false while the sidebar or a dialog takes the keys: draw selections with `focused` so that they look inactive then. |
| `OnKey(key)` | Return true when the page took the key. What it does not take, the shell handles: Esc, ← and Tab go back to the sidebar; Ctrl+C asks to quit. So return false for ← at your leftmost pane, and for Tab at your last focus area. |
| `OnTabIn(backward)` | Tab (or Shift+Tab, `backward`) brought the focus from the sidebar: give it to your first (or last) focus area. A page with one area needs nothing. |
| `OnStart()` | Once, when the interface starts, for every page, before anything is drawn. The Home page starts the GitHub check here. |
| `OnShown()` / `OnHidden()` | The page becomes / stops being the one shown. Moving through the sidebar shows each page it passes, so `OnShown` must be cheap: start loading in the background. |
| `OnTick()` | Four times a second, for every page, shown or not. The screen is drawn again after each tick. |
| `OnWorkspaceChanged()` | Another workspace was selected, or an operation changed the selected one (`PlanAndRun` succeeded, or a page called `NotifyWorkspaceChanged`). For every page. Drop what you read. |
| `OnRequest(request)` | Another page navigated here with a `PageRequest`. |
| `CanLeave` / `ConfirmLeave(leave)` | Return false from `CanLeave` while there are unsaved changes. The shell then calls `ConfirmLeave` instead of leaving (for another page, another workspace, or quitting): show a dialog, and call `leave()` once the changes are saved or discarded. |

Rules that keep ten pages working together:

- Touch only your own `Pages/<Name>/` directory and `tests/Lip.Cli.Tests/Tui/<Name>PageTests.cs`.
  Put helper types in your page's namespace. When the shell lacks something, say so in the PR
  instead of changing shared files.
- **Never block the thread of the interface.** No `.Result`, no `.Wait()`, no file or network
  access in `Draw`, `OnKey` or `OnTick`. Use `Context.Background` (no dialog) or
  `Context.RunOperation` (progress dialog); both call you back on the thread of the interface.
  Do not write `Task.Run` + `Post` yourself: work that is of no workspace is
  `Context.Background(…, anyWorkspace: true)`.
- **`Context` is for the thread of the interface.** The work that `Background` and
  `RunOperation` run is not on it: read `Context.Client`, `Context.Workspace` and whatever
  else the work needs into locals first, and use only those in the work. `Post`,
  `SelfUpdate` and `DaemonStatus` are the members that any thread may call.
- A page lives as long as the interface, and the language may change while it runs: never
  store the result of `L.T` (or of an `XxxText` member) in a field. Persistent widgets take
  `Func<string>` for that reason.
- Business logic lives in `Lip.Core`. Call `Context.Client` (`LipClient`) and the other
  services; do not reimplement.
- An exception that escapes `OnKey`, a callback or `Draw` does not end the interface: the shell
  shows it as a failure (a page whose `OnTick` throws gets no more ticks, and a dialog whose
  `Draw` throws is closed). Do not rely on that; catch what you expect.
- Change the user configuration through `Context.Client.ConfigSet` / `ConfigDelete`; they are
  serialized within the process, so the keys do not overwrite each other.
- No reflection: lip is published trimmed, and a trim warning fails the publish.
- In a page namespace, `ModConfig` is the namespace `Lip.Cli.Tui.Pages.ModConfig`, not the type
  of `Lip.Core.Config`: write `Lip.Core.Config.ModConfig`. In tests, `AppContext` is ambiguous
  with `System.AppContext`: write `System.AppContext.BaseDirectory`, or alias
  `using TuiContext = Lip.Cli.Tui.AppContext;`.

### Requests between pages

`Context.Navigate(PageId.X, PageRequest.Y)` shows page X with the focus and then calls its
`OnRequest`. The requests are fixed, so that pages written at the same time agree:

| Request | To | The page then |
|---|---|---|
| `PageRequest.NewServer` | Workspaces | starts the new server wizard |
| `PageRequest.StartServer` | Server | starts the server of the selected workspace |
| `PageRequest.InstallFromSource` | Mods | asks for a package to install from a source |
| `PageRequest.GithubProxy` | Settings | asks for the GitHub proxy, tests it and saves it |

The Home page offers "a proxy" of its GitHub question by navigating to Settings with
`PageRequest.GithubProxy`; "direct" and "later" it handles itself.

## AppContext

`Context` in a page or a dialog. Everything is for the thread of the interface, but `Post`,
`SelfUpdate` and `DaemonStatus`, which any thread may call.

**Workspace**

| Member | |
|---|---|
| `Client` | The `LipClient` of the selected workspace. A new object after a switch: capture it in a local before a background task. |
| `Workspace` | The selected workspace, an absolute path. |
| `Workspaces` | The `WorkspaceList` (`Read`, `Add`, `Remove`, `Same`, `Normalize`). |
| `FileSystem` | The `IFileSystem` of the workspaces. Use it, not `System.IO.File`: tests run on a `MockFileSystem`. |
| `FullPath(path)` | A path, absolute or relative to the selected workspace, in canonical form. |
| `SwitchWorkspace(path, switched?)` | Selects a workspace of the list (after `ConfirmLeave` when needed). |
| `AddWorkspace(path, out error, switched?)` | Adds an existing directory to the list and selects it; false with a localized `error` otherwise. |
| `NotifyWorkspaceChanged()` | Tells every page that the workspace changed, after you changed it by other means than `PlanAndRun`. |
| `ClientFor(directory)` | A client for another directory (the new server wizard reads versions with it). |

**Parts of lip** (each is a seam that tests replace)

| Member | |
|---|---|
| `Version` | The version of lip. |
| `Catalog` | `ModCatalogClient` for levimc.org. |
| `GithubProbe` | The reachability check, or null when the interface makes none. |
| `ServerSetup` | `IServerSetup`: `GameVersions`, `LoaderVersions`, `Setup`. Use it instead of `LipClient.Setup` so that tests can fake it. |
| `SelfUpdate(client, checkOnly)` | `lip self-update [--check]` with a client that you read from `Client` before the operation started. For any thread. |
| `DaemonStatus()` | `lip daemon status`: a `DaemonState`, or null when no daemon listens. For any thread. |
| `OpenUrl(url)` | Opens an `http`/`https` URL in the browser; anything else is refused with a toast. |

**Server**

| Member | |
|---|---|
| `Servers` | The `ServerSessions` of the process: `Find(workspace)?.Running` for any workspace. |
| `Server` / `ServerRunning` | The `WorkspaceServer` of the selected workspace (null when none ever ran): `Running`, `Restarting`, `Stopping`, `ExitCode`, `StartedAt`, `LastLines(n)`, `Send(line)`, `Stop()`. |
| `StartServer(title)` | Starts it as the protocol defines; a failure before it starts is shown in a dialog titled `title`. |
| `ManagedServers` | Whether servers keep running when the interface quits. |

The shell draws the screen again whenever a server changes or writes; a page just reads the
server in `Draw`.

**Navigation and dialogs**

| Member | |
|---|---|
| `Navigate(page, request?)` | Shows a page with the focus in its content area. |
| `CurrentPage`, `ContentFocused` | What is shown, and where the focus is. |
| `FocusContent()`, `FocusSidebar()` | Moves the focus. The interface starts with the focus in the sidebar; the Home page may call `FocusContent()` from `OnStart`. |
| `Show(dialog)` / `Close(dialog)` | Pushes / removes a dialog. A dialog closes itself with `Close()`. |
| `Confirm(title, question, confirm, confirmed, cancel?)` | A question with two buttons. |
| `Message(title, message, style?)` | A message with a Close button. |
| `ShowMenu(title, items…)` | The action menu of a row. |
| `Toast(text, kind?)` | A message at the right of the bottom bar for six seconds. |
| `Idle` | No dialog and no operation is open. Ask unprompted questions only when it is true. |
| `RequestQuit()` | Asks to quit, as Esc in the sidebar does. |

**Operations**

| Member | |
|---|---|
| `Post(action)` | Runs `action` on the thread of the interface. For any thread. |
| `Background(work, done, failed?, anyWorkspace?)` | Runs `work` off the thread without a dialog, then `done(result)` on it. Without `failed`, a failure is a toast. What the work read is taken to be of the selected workspace: `done` and `failed` are dropped when another workspace was selected meanwhile. |
| `Background(…, anyWorkspace: true)` | The same for work that is of no workspace (the workspace list, the catalogue, the user configuration, the daemon, the GitHub check): its callbacks are called whatever is selected by then. |
| `RunOperation(title, action, succeeded?, cancellable?)` | Runs a command with a progress dialog; then `succeeded(operation)`, or the failure dialog. Returns false when another operation runs. `operation.Log` holds what the command said. |
| `RunOperation<T>(title, action, succeeded, cancellable?)` | The same for a command with a result. |
| `PlanAndRun(title, (dryRun, token) => …, done?)` | The standard flow of a workspace operation. Pass `confirmed: true` to `LipClient` where it has it. |
| `PlanAndRun(title, (dryRun, acceptEula, token) => …, done?)` | The same for what may be a setup (`install`, `setup`). |
| `ShowResult(title, operation, done?)` | The messages of a finished operation, ending with `✓ done`. |
| `ShowFailure(title, exception, cancelled?, log?)` | The failure dialog; the report goes to `.lip/logs` of the selected workspace. |

The messages of a command (`IUserInteraction`) go to the log of the operation that runs, or to
the toast when none runs; a question of a command (`Ask`) opens a prompt.

```csharp
// The workspace list is of no workspace: it is shown whichever one is selected by then.
WorkspaceList workspaces = Context.Workspaces;
Context.Background(
    () => Task.FromResult(workspaces.Read()), paths => _list.Items = paths,
    anyWorkspace: true);

// lip self-update: the client is read here, not in the operation.
LipClient client = Context.Client;
Context.RunOperation(
    SettingsText.CheckUpdates, _ => Context.SelfUpdate(client, checkOnly: true), Checked);
```

```csharp
// lip install, as every workspace operation goes.
LipClient client = Context.Client;
Context.PlanAndRun(
    ModsText.InstallTitle(package),
    (dryRun, acceptEula, token) =>
        client.Install([package], dryRun, token, acceptEula, confirmed: true));
```

**Language**

| Member | |
|---|---|
| `Language` | The interface language now. |
| `LanguageSetting` | The value of the configuration key `language`, null for "follow the system". |
| `ClientLanguage` | The language of the client's locale, or null. |
| `SetLanguage(setting)` | Sets or (null) removes the key; the screen changes at once. |

## Widgets

All in `Lip.Cli.Tui.Widgets`. A widget is a plain object that a page owns: call its `Draw`
from your `Draw`, pass it keys from your `OnKey` (it returns whether it took the key), and
include its `Hints` in yours.

| Widget | For | Keys it takes |
|---|---|---|
| `ListView<T>(render)` | A scrolling list. `Items`, `Selected`, `Current`/`HasCurrent`, `Select(match)`. `MultiSelect` + `Checked` + `CanCheck` + `Targets` (the checked rows, or the selected one). `Wrap`. `Empty`. `Hints` (↑↓, Space) and `PageHints` (PgUp/PgDn, Home/End, while it has more rows than it shows: put them last). | ↑ ↓ PgUp PgDn Home End, Space with `MultiSelect`. Without `Wrap`, ↑ on the first row and ↓ on the last return false, so that you can move the focus to what is above or below. Enter is yours. |
| `ButtonRow(buttons…)` | Buttons side by side: `new Button(() => Text.Start, Start) { Enabled = () => … }`. `Draw(screen, x, y, focused)`. | ← → (false at the ends), Enter, Space |
| `Form(fields…)` | Rows of `TextRow`, `ChoiceRow`, `ToggleRow`; `Stacked` puts labels above values, and wraps a label that is longer than the form is wide (`HeightFor(width)`). | ↑ ↓ Tab Shift+Tab between fields (false at the ends); the rest goes to the field. Enter is yours. Its `Hints` name ↑↓; name Tab yourself. |
| `TextRow` | A text with a placeholder. `Value` is trimmed. | characters, ← → Home End Backspace Delete |
| `ChoiceRow` | One of several options, drawn `‹ value ›`. `Selected`. | ← → and Space |
| `ToggleRow` | On or off, drawn `[x]`. `On`. | Space |
| `TextField` | The bare text field, for a search box or an input line. | as `TextRow` |
| `TextViewer` | Scrolling text: `SetText(string)` or `Lines` (styled). Wraps at spaces, keeps indentation. `ScrollToEnd()`. `Hints` are empty unless it scrolls. | ↑ ↓ PgUp PgDn Home End while `Scrollable` |
| `FocusRing(count)` | Which area of the page has the focus. | Tab / Shift+Tab (false at the ends) |
| `Panes.Columns(area, left)`, `Panes.Rows(area, top)` | Split an area in two. | — |
| `Ui.Panel(screen, rect, title, focused)` | A titled box inside the page; returns its inside. | — |

Dialogs (`Context.Show(new …)`); each takes every key until it closes:

| Dialog | For |
|---|---|
| `ConfirmDialog(title, lines, buttons, chosen?, border?, fromEnd?)` | Lines that scroll above buttons. Enter presses the chosen button; **Esc presses the last one**, so put the button that changes nothing last. |
| `PromptDialog(title, fields, submit, cancel?, validate?)` | One or more text fields. A long label, such as a question of a command, is wrapped. `validate` returns a localized error to keep the dialog open. |
| `ChoiceDialog(title, items, chosen, cancel?, selected?)` | Choose one of a list; `ChoiceItem.Detail` is the one-sentence explanation in the details area. |
| `Menu(title, items)` | The action menu of a row (`Context.ShowMenu`). An item with `Enabled: false` is shown muted and skipped. |
| `ViewerDialog(title, text or lines)` | A large scrolling text: a README, a JSON text. |
| `ProgressDialog` | Shown by `RunOperation`; you do not create it. |

To write your own dialog, derive from `Dialog`: `Hints`, `Draw(screen)` (use
`Ui.DialogFrame`), `OnKey(key)`, `Close()`, and `Context`.

### The details of a mod

`ModDetails` shows a mod of the catalogue as the Get mods page does: its description, then
Author, Downloads, Stars, ID, Latest, Platform, Game, Tags, License, Depends, Updated. Use it
wherever a mod is shown, so that it reads the same everywhere.

- In a pane of a page: keep one `ModDetails`; in `Draw` call
  `Show(mod, detail, failed)` (the `CatalogDetail` once you read it, `failed` when it cannot
  be read, neither while it is on its way) and `Draw(screen, rect)`. While the pane has the
  focus pass it the keys (`OnKey` scrolls, and returns false when nothing scrolls) and list
  its `Hints` and, last, its `PageHints`. Give the pane the focus only while `Scrollable`.
- In a dialog: `Context.Show(new ModDetailsDialog(mod))` shows the same details above
  `[ README ] [ Open in browser ] [ Close ]`. It reads the file of the mod itself; pass the
  `CatalogDetail` as the second argument when you have it.
- For an installed package, find its mod with `CatalogIndex.Find(package.Identity)`; null
  means that the catalogue has no such mod.

```csharp
ModCatalogClient catalog = Context.Catalog;
string identity = _list.Current.Identity;
Context.Background(
    () => catalog.GetIndex(),
    index => {
      if (index.Find(identity) is { } mod) {
        Context.Show(new ModDetailsDialog(mod));
      } else {
        Context.Toast(ModsText.NotInCatalogue);   // a text of your page
      }
    },
    anyWorkspace: true);
```

`Cells` has what such rows are written with: `Pad`, `PadLeft`, `Date`, `BareName`, and
`Compact` (55.9k) and `Count` (55,912) for numbers.

A page with two areas:

```csharp
private readonly FocusRing _focus = new(2);   // 0: the actions, 1: the list

public override bool OnKey(ConsoleKeyInfo key) {
  if (_focus.OnKey(key)) {
    return true;                              // Tab moved between the areas
  }

  if (_focus.Index == 0) {
    if (_actions.OnKey(key)) {
      return true;
    }

    return key.Key == ConsoleKey.DownArrow && _focus.Next();
  }

  if (_list.OnKey(key)) {
    return true;
  }

  return key.Key == ConsoleKey.UpArrow && _focus.Previous();   // ↑ on the first row
}

// Tab from the sidebar starts at the actions, Shift+Tab at the list.
public override void OnTabIn(bool backward) => _focus.Index = backward ? 1 : 0;
```

## Text and languages

The interface has three languages: English, Simplified Chinese, Traditional Chinese
(`protocol/lip-tui.md`, "Language"). There are no resource files; the compiler checks that
every text has all three:

```csharp
public static class ModsText {
  public static string Title => L.T("Mods", "模组", "模組");
  public static string Empty => L.T("No mods yet.", "还没有模组。", "還沒有模組。");
  public static string InstallTitle(string package) =>
      L.T($"Install {package}", $"安装 {package}", $"安裝 {package}");
}
```

- Every text that the interface itself shows goes through `L.T(en, zhHans, zhHant)`, in your
  `XxxText` class, as properties or methods (never fields).
- Reuse `CommonText` for buttons (`Ok`, `Cancel`, `Apply`, `Save`, `Discard`, …), hints and
  common words, so that they read the same everywhere. What two pages name is there too:
  `NewServer`, `GetMods`, `AutoRestart`, `ConnectDirectly`, `NoMods`, `Saved`,
  `SavedWhileRunning`, `DiscardTitle`, `DiscardQuestion`. When your page needs a text that
  another page has, move it to `CommonText` instead of writing it again.
- One word for one thing: a directory is a folder (文件夹, 資料夾), and a URL is 网址 (網址).
- Use the real Traditional Chinese terms (伺服器, 設定, 檔案, 模組, 工作區), not a character by
  character conversion of the Simplified text.
- `L.T` reads the language of the interface that the calling thread or task belongs to. That
  is set up for the thread of the interface and everything it starts. Several interfaces with
  different languages run in one daemon, so there is no process-wide language.
- Keep keys (`Enter`, `Esc`, `Space`, `Tab`) and names (`lip`, `LeviLamina`, `GitHub`,
  `server.properties`) untranslated.

## Tests

`tests/Lip.Cli.Tests/Tui/TuiHarness.cs` runs the interface in the test process on a fake
terminal. Drive it only with the keys a user has; assert on the text of the screen.

```csharp
[Fact]
public async Task Uninstall_AsksForThePlan_ThenRemovesThePackage() {
  using TuiHarness tui = TuiHarness.Start(new TuiHarnessOptions { Language = Language.ZhHans });
  await tui.GoTo(PageId.Mods);                 // Esc, arrows and Enter, as a user would
  await tui.WaitFor("github.com/acme/Alpha");

  tui.Press(ConsoleKey.Enter);                 // the action menu
  await tui.WaitFor(tui.Text(() => ModsText.Uninstall));
  tui.Press(ConsoleKey.DownArrow);
  tui.Press(ConsoleKey.Enter);
  await tui.WaitFor($"[ {CommonText.Apply} ]");
  tui.Snapshot("mods-uninstall-plan");         // written only when LIP_TUI_DUMP is set
  …
}
```

| Member | |
|---|---|
| `TuiHarness.Start(options?)` | Starts the interface. Not async on purpose: afterwards `L.T` on the test's own flow is in the language of the interface, so `CommonText.Apply` and your `XxxText` members match the screen. |
| `Press(key, times?, modifiers?)`, `Type(text)`, `Interrupt()` | Keys. They return at once. |
| `WaitFor(text)`, `WaitUntilGone(text)`, `WaitFor(predicate)` | Wait for the screen (15 s). Use these for anything that runs in the background. |
| `Settle()` | Waits until every key pressed so far is handled and drawn. Use it before asserting that something did *not* happen, and before `Invoke` when keys are still on their way. |
| `Screen` | The screen as text, one row per line, without trailing spaces. |
| `GoTo(page)` | Goes to a page through the sidebar and waits until its content has the focus. |
| `Invoke(func)` | Runs code on the thread of the interface (to read page or context state). |
| `Text(() => XxxText.Y)` | A text in the language of the interface, wherever it is called. |
| `Quit()` | Ctrl+C, then Enter; returns the exit code. `Dispose` ends the interface too. |
| `Resize(w, h)` | Another terminal size. |
| `Dump(writer or path, name?)`, `Snapshot(name)` | Write the current frame as text for visual review. |
| `Fs`, `Root`, `Workspaces`, `Servers`, `Context`, `Config`, `OpenedUrls`, `ClientWorkspaces`, `InteractionOf(client)`, `WorkspaceOf(client)` | The fake world. |

What `TuiHarnessOptions` replaces:

| Option | Fakes |
|---|---|
| `Language`, `Width`, `Height` | The client language (null: none) and the terminal size. |
| `FileSystem` | The `MockFileSystem` of the workspaces and the configuration; fill it before `Start` (for example a `.lip/state.json`: `TuiHarness.StateWithGame`). |
| `CreateClient` | The `LipClient` of a workspace. Wrap `LipClient.Create(fs, interaction, hookOutput, workspace)` or build a client with mocked services to fake or record commands; `InteractionOf(client)` is how a fake command talks to the user. |
| `Catalog` | The JSON text of a catalogue URL (`ModCatalogClient.BaseUrl + "index.json"`, `ModCatalogClient.DetailUrl(id)`). |
| `GithubProbe` | `new GithubProbe((url, token) => Task.FromResult(200))`. |
| `ServerSetup` | `harness => new FakeServerSetup(harness)`: two games, two loaders, a setup that records its calls and asks for the license. |
| `SelfUpdate`, `DaemonStatus` | `lip self-update` and `lip daemon status`. |
| `ManagedServers`, `Servers` | Whether servers outlive the interface, and the `ServerSessions`. |
| `Pages` | Other pages than those of lip (the shell tests use their own). |
| `DescribeFailure` | The failure report; by default nothing is written. |

`TestClients.CmdWorkspace()` and `TestClients.CmdClient(…)` give a workspace on the disk whose
server is `cmd.exe` (Windows only), for tests of the Server page.

Each page has `tests/Lip.Cli.Tests/Tui/<Name>PageTests.cs`; extend that file. What leads from
one page to another is tested with the pages of lip in `FlowTests.cs`. Run at least one flow
in `zh-Hans` and one in `zh-Hant`.

`Press` and `Type` return at once. Before asserting on the screen, wait for what the keys
lead to (`WaitFor` its text), or `Settle()` when nothing new is shown; a test that reads the
screen right after `GoTo` reads it before the page has loaded.

### Looking at the frames

```
set LIP_TUI_DUMP=%TEMP%\lip-frames
dotnet test tests/Lip.Cli.Tests --filter "FullyQualifiedName~ModsPageTests"
```

Every `tui.Snapshot("name")` then writes `name-<language>-<W>x<H>.txt`. Take snapshots at
120×36 and 60×16 in all three languages and read each frame: nothing cut off or misaligned,
CJK columns aligned, the bottom bar listing exactly the keys that work, nothing reachable only
by a letter. Delete the directory afterwards; do not commit frames.

### Before you push

```
dotnet test tests/Lip.Cli.Tests --filter "FullyQualifiedName~Tui|FullyQualifiedName~Daemon"
dotnet test
dotnet format --verify-no-changes
dotnet build -warnaserror -p:EnforceCodeStyleInBuild=true
dotnet publish src/Lip.Cli -o <temp dir> -r win-x64 --self-contained -p:DebugType=embedded
```

The last one fails on a trim warning. For a manual run never touch the user's daemon: use a
Debug build with `LIP_DAEMON_ENDPOINT=<unique name>`, and `lip daemon stop` afterwards.
