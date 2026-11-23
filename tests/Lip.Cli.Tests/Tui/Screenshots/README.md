# Screenshots of the interface

This page is for whoever writes the documentation. The documentation of lip is written in
[LiteLDev/liteldev.github.io](https://github.com/LiteLDev/liteldev.github.io) (`docs/lip/`), and
the pictures of the interface that it shows are committed there, in `docs/lip/public/screenshots/`;
they are made here, by the tests of lip.

The pictures of the interactive interface are not taken of a
terminal. Each is drawn, as an SVG file, from a frame that the interface really rendered: a
scenario starts the interface in the test process, presses keys as a user would, and at chosen
moments captures what the screen shows, cell by cell with its colors. The same scenarios make
the same files again, byte for byte, so the pictures are made again whenever the interface
changes, and never drift from it.

```
tests/Lip.Cli.Tests/Tui/Screenshots/
  Scenarios/*.cs    the scenarios: one class per part of the interface
  Shots.cs          ShotTheory, ShotRun, Shot: how scenarios run and where the files go
  World.cs          World, Installed, SetupOfShots: what the interface of a scenario finds
  Worlds.cs         the worlds most scenarios start from; Catalogue.cs: the mods of levimc.org
  Notes.cs          At (targets) and Note (annotations)
  Frame.cs          a captured frame; SvgRenderer.cs: the picture of a frame
<docs>/<language>/<name>.svg     the pictures: en, zh-Hans, zh-Hant
```

## Making and checking the pictures

The pictures are written to `<docs>`: `docs/lip/public/screenshots` of a checkout of
LiteLDev/liteldev.github.io named `liteldev.github.io` next to the checkout of lip, or the
directory that `LIP_SCREENSHOTS_DIR` names. Without either, the scenarios fail and say so.

From the root of the repository (PowerShell; in another shell, set the variable its way):

```
$env:LIP_SCREENSHOTS = "write"
dotnet test tests/Lip.Cli.Tests --filter "Category=DocsScreenshots"
```

| `LIP_SCREENSHOTS` | The scenarios |
|---|---|
| not set | are skipped: an ordinary `dotnet test` makes no picture. |
| `write` | write `<docs>/<language>/<name>.svg`. A file that would not change is not touched. When every scenario ran and passed, the pictures that no scenario made are deleted. |
| `check` | fail where a file is missing or is not what the interface shows now, and, when every scenario ran and passed, where a picture is left that no scenario makes. Nothing is written. |

- `LIP_SCREENSHOTS_DIR` names another directory than that of the checkout next to lip: another
  checkout, or a directory to look at pictures without touching the committed ones.
- `LIP_TUI_DUMP=<directory>` also writes each captured frame as plain text
  (`<name>-<language>-<W>x<H>.txt`), which is the quickest way to see what a scenario reached.
- Add `&FullyQualifiedName~HomeShots` to the filter to run one class, and
  `&DisplayName~ZhHans` for one language.
- All of them take a few seconds. Run them on Windows: one scenario, the running server, runs
  `cmd.exe` as the server and is skipped elsewhere, and the paths that the interface shows are
  those of Windows there without any rewriting (see "Limits").

After `write`, look at `git diff --stat docs/lip/public/screenshots` in liteldev.github.io: only the pictures of what you
changed should differ. Open the changed files in a browser before you commit them.

## Writing a scenario

A scenario is a test method with `[ShotTheory]`, which is run once for each language:

```csharp
using Lip.Cli.Tui;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Pages.Mods;

namespace Lip.Cli.Tests.Tui.Screenshots.Scenarios;

public sealed class UninstallShots(ShotRun run) {
  [ShotTheory]
  [ClassData(typeof(AllLanguages))]
  public async Task Uninstall(Language language) {
    // What the interface finds: a server with mods, on a terminal of 100×22.
    using Shot shot = run.Start(Worlds.Survival(language, height: 22));
    TuiHarness tui = shot.Tui;

    // Drive the interface with the keys of a user, and wait for what they lead to.
    await tui.GoTo(PageId.Mods);
    await tui.WaitFor(Catalogue.Money);
    tui.Press(ConsoleKey.DownArrow, 3);
    await shot.Capture(
        "uninstall-01-select",
        Note.Box(At.Row(Catalogue.Money)),
        Note.Keys("Enter"));

    tui.Press(ConsoleKey.Enter);
    await tui.WaitFor(ModsText.Uninstall);
    await shot.Capture(
        "uninstall-02-menu",
        Note.Focus(At.Panel(Catalogue.Money)),
        Note.Callout(
            1, At.Row(ModsText.Uninstall),
            shot.T("Removes the mod and its files.", "移除模组及其文件。", "移除模組及其檔案。")),
        Note.Keys("↓", "↓", "↓", "Enter"));
  }
}
```

- `run.Start(world)` starts the interface; `shot.Tui` is the `TuiHarness` that the tests of
  the interface use (`src/Lip.Cli/Tui/README.md`, "Tests"): `Press`, `Type`, `GoTo`,
  `WaitFor`, `WaitUntilGone`, `Settle`, `SetServerRunning`.
- Wait for the text of what runs in the background before a capture. `Capture` itself only
  waits until the keys pressed so far are handled and drawn.
- Take the texts of the interface from its `…Text` classes (`ModsText.Uninstall`,
  `CommonText.Apply`): after `run.Start` they are in the language of the scenario, so the same
  scenario finds its targets in every language.
- The labels of annotations are yours: `shot.T(en, zhHans, zhHant)` gives the one of the
  language. Use the real Traditional Chinese terms, not a conversion of the Simplified text.
- A scenario may take as many captures as it likes; a tutorial is usually one scenario.

### The world

`World` is what the interface finds: a file system in memory, a catalogue, a server setup. It
never touches the disk or the network, and nothing of the machine that runs it shows: a
capture fails when a frame holds the user name, the home or the temp directory of the machine.

| | |
|---|---|
| `Worlds.Empty(language)` | A folder without a server, `D:\servers\survival`. |
| `Worlds.Server(language)` | The game 1.26.60 and LeviLamina 26.60.3, no mods. |
| `Worlds.Survival(language)` | A server with five mods, `server.properties`, `allowlist.json`, `permissions.json`, mod configuration files, and two more workspaces in the list. `configurable: true` makes the Mod config page list the mods; without it lip can plan to uninstall them. |
| `new World(language, width, height, workspace)` | Your own: `Install`, `InstallServer`, `File`, `List`, `Configure`, and the properties `Fs`, `Archives`, `CatalogFile`, `GithubProbe`, `ServerSetup`, `SelfUpdate`, `DaemonStatus`, `ManagedServers`, `CreateClient`, `DescribeFailure`. |

- The size is 100×28 unless you give another. Take fewer rows for a page that shows little
  (`height: 22`), so that the picture is not mostly empty; 60×16 is the smallest terminal.
- Write paths as Windows does and pass them through `World.PathOf(@"D:\servers\creative")`.
- The version of lip is `World.Version`. The time since the server started is always shown as
  `00:12:34`. For anything else that differs from run to run, such as the spinner of a
  progress dialog, call `shot.Scrub(pattern, replacement)` before the capture.
- `SetupOfShots` is the server setup: its `Release` task holds a setup in the middle of a
  download for a capture of the progress dialog, and `Result` is what the setup changes.
- `tui.SetServerRunning(true)` makes the server run as far as the interface can tell. Only
  `ServerShots.Running` has a server with output, a real process.

## Annotations

Annotations are drawn above the frame, in an orange that the interface does not use. They are
the arguments of `Capture` after the name.

A **target** says what an annotation points at. It is found in the frame when the picture is
drawn, so it follows the text wherever a language or a change of the interface moves it, and
the capture fails when the frame does not show it.

| Target | The cells of |
|---|---|
| `At.Text("Create a server")` | the text, in one row; `At.Text(text, 2)` its second place, from the top. |
| `At.Row("Create a server")` | the row that shows the text, between the borders at its left and its right: a row of a list or of a menu. |
| `At.Button(CommonText.Apply)` | the button `[ Apply ]`. |
| `At.Panel(ModsText.Title)` | the box with the title in its top edge: the content area, a panel of a page, a dialog, a menu. |
| `At.Cells(x, y, width, height)`, `At.Rows(y, height)` | cells by position, from 0. A last resort: they do not follow the interface. |
| `a.To(b)` | the smallest rectangle that holds both. |
| `a.Pad(columns, rows)` | the target and that many cells around it. |

| Annotation | Draws |
|---|---|
| `Note.Box(target)` | a rounded box around the target. |
| `Note.Ellipse(target)` | an ellipse around it. |
| `Note.Arrow(target, label, Side.Right)` | a label in the margin at that side of the window, with an arrow to the target. The picture grows by the margin. |
| `Note.Callout(1, target, text, Side.Left)` | a circle with the number at that side of the target. With a text, the legend below the window says what the number stands for. |
| `Note.Keys("↓", "↓", "Enter")` | the keys that the user presses next, as a row of badges below the window. `Note.KeysAfter("Then press", …)` puts a caption before them. |
| `Note.Focus(target)` | dims the frame but for the target; several keep several targets bright. |

```csharp
await shot.Capture(
    "first-server-06-plan",
    Note.Focus(At.Panel(SetupText.SetUpTitle)),
    Note.Callout(
        1, At.Text("Dry run:"),
        shot.T("What lip would do.", "lip 将要做的事。", "lip 將要做的事。")),
    Note.Arrow(
        At.Button(CommonText.Apply), shot.T("Carries it out", "执行计划", "執行計畫"),
        Side.Bottom),
    Note.Keys("Enter"));
```

Keep them few: one picture, one point. An arrow from the left or the right crosses the row of
its target; choose the side with nothing in between, or `Side.Top` and `Side.Bottom`.

## Names and sizes

- A picture is named `<topic>-<nn>-<what>`, in lower case with hyphens:
  `first-server-02-game-version`. The topic is the tutorial or the page, `nn` the order within
  it, from `01`. A name is used once across all scenarios.
- The pictures of the pages, made by the scenarios that are there already, are named after the
  page: `home-`, `new-server-`, `server-`, `mods-`, `get-mods-`, `game-config-`, `mod-config-`,
  `workspaces-`, `tools-`, `settings-`, `plan-`, `dialog-`, `narrow-`.
- A picture of a frame of 100 columns is 944 pixels wide at its own size, more with labels in
  its margins; the site scales it to the width of the text.

## Embedding a picture

How a page of the documentation embeds a picture, in each language, is written in
LiteLDev/liteldev.github.io, with the pages (`docs/lip/`).

The tutorials at [levimc.org/tutorials](https://levimc.org/tutorials/) (Your first server, Install mods on your
server) show the `first-server-` pictures by their full URL, `https://levimc.org/lip/screenshots/<language>/<name>.svg`:
no page of the documentation needs most of them, but do not rename or drop one without changing the tutorial too.

- Always write the alternative text: one sentence in the language of the page that says what
  the picture shows and what matters in it, for whoever cannot see it.
- The picture is a dark terminal window with its own background, on the light and on the dark
  theme of the site alike.

## Limits

- What is drawn is the frame: the cells, their colors, bold, underline and inverse. The font
  is that of the reader (a monospace font, then one with Chinese characters); the picture
  places every run of text at its column, so the columns line up in any font, but the shapes
  of the letters differ between readers. The characters that draw boxes and blocks are drawn
  as lines and rectangles; other symbols (`●`, `✓`, `▼`, the icons of the narrow sidebar) are
  text in the middle of their cells.
- There is no cursor of the terminal, no blinking, and no animation: one frame, one picture.
- The text of the frame is real text, which a reader can select and search; lines and blocks
  are not.
- Whatever a command says is in English in every language, as in the interface.
- The Settings page shows the cache directory of whoever runs the scenarios, which `Capture`
  writes as `C:\Users\Steve\AppData\Local\lip\Cache`; that works while the row has room
  for the path, as it has at 100 columns.
- Off Windows, the scenarios run on paths like `/D/servers/survival`, which `Capture` writes
  back as `D:\servers\survival`. That was not tried on Linux or macOS: make the committed
  pictures on Windows.
- The archives of a world are empty, so installing and updating fail unless a scenario sets
  `World.Archives` up; uninstalling, and the new server wizard with `SetupOfShots`, work.
- `LIP_SCREENSHOTS=check` is not part of the CI.
