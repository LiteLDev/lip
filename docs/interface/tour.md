# A tour of the interface

After this page you can find your way around the interface of lip: you know what each part of the screen is, which keys do what, and what happens when you change something. Every other page of this section builds on it.

The pictures on these pages are drawn from what lip really shows. Orange marks, numbers and key badges are added to explain them; they are not part of lip.

## Starting it

Open a terminal in the folder of your server, or in an empty folder where you want one, and run `lip` without anything after it:

```shell
lip
```

lip takes over the whole terminal window and shows its Home page. The folder you started it in is the **workspace**: the folder that every page works on.

![The Home page of lip in an empty folder. The first step, "Create a server", is highlighted, and the bottom line lists the keys that work.](/screenshots/en/home-01-empty.svg)

You use the keyboard only. The mouse does nothing.

::: tip
`lip` followed by a word, such as `lip install`, is a command: it does one thing and ends. The commands are described in [Running a server from the commands](../guides/server_from_commands.md) and in the [reference](../reference/cli.md).
:::

## What is on the screen

![The Home page of a server that runs, with numbers on its five parts: the top line, the server state, the sidebar, the page and the bottom line.](/screenshots/en/tour-01-layout.svg)

1. **The top line** shows the version of lip and the workspace.
2. At its right end, the top line says whether the server of this workspace runs. While the server is stopped, nothing is shown there.
3. **The sidebar** lists the nine pages: [Home](./home.md), [Server](./server.md), [Mods](./mods.md), [Get mods](./get_mods.md), [Game config](./game_config.md), [Mod config](./mod_config.md), [Workspaces](./workspaces.md), [Tools](./tools.md) and [Settings](./settings.md). A green `●` after Server means that the server runs.
4. **The page** is the large box. Its name is written into its frame.
5. **The bottom line** lists the keys that work right now, each with a word that says what it does. It changes whenever you move. When in doubt, read it.

## Where the focus is

At any moment, either the sidebar or the page takes your keys. That part has the **focus**. lip starts with the focus in the Home page.

Press `Esc` to move the focus from the page to the sidebar. There, `↑` and `↓` go through the pages, and each page shows at once as you pass it.

![The focus is in the sidebar. The entry "Mods" is bright, the Mods page shows at the right, and the bottom line says "Esc Quit".](/screenshots/en/tour-02-sidebar.svg)

1. The entry of the page shown is bright while the sidebar has the focus.
2. In the sidebar, `Esc` asks whether to quit lip.

Press `Enter` to move the focus into the page.

![The focus is in the Mods page. Its frame is bright, a row of the list is highlighted, and the bottom line says "Esc Back".](/screenshots/en/tour-03-page.svg)

1. The frame and the name of the page are bright while the page has the focus.
2. One row is highlighted. `Enter` and `Space` act on that row.
3. `Esc` now goes back to the sidebar.

### Esc goes back one step

`Esc` always goes back one step, and never further:

| Where you are | `Esc` goes to |
| --- | --- |
| A dialog or a menu | What was below it. |
| An inner part of a page, such as the input line of the server | The part of the page that it was entered from. |
| A page | The sidebar. |
| The sidebar | The question whether to quit lip. |

So pressing `Esc` several times always brings you to the sidebar, and once more to the question about quitting. Nothing is lost on the way: a page with changes that are not saved asks first.

## The keys

The same keys do the same thing on every page.

| Key | Does |
| --- | --- |
| `↑` `↓` | Move in a list or a menu. In the sidebar, show the page above or below. |
| `←` `→` | Move between the sidebar and the page, and between parts or buttons that are side by side. Change the value of an option that is drawn `‹ like this ›`. |
| `Enter` | Opens or carries out what is highlighted. On a row of a list, opens the menu of what you can do with it. |
| `Space` | Checks a row where several can be checked. Switches an option on or off. |
| `Esc` | Goes back one step. |
| `Tab` | Goes to the next part: from the sidebar through the parts of the page and back to the sidebar. `Shift+Tab` goes the other way. |
| `PgUp` `PgDn` | Move a screenful up or down in a list or a text. |
| `Home` `End` | Go to the first or the last row. |
| `Ctrl+C` | Asks whether to quit lip. On the Server page while the server runs, stops the server instead. While lip carries a change out, cancels it. |

There are no letter shortcuts. Letters are typed only where a text field has the focus, such as a search box. Everything else is a row or a button that the arrow keys reach.

A list or a text that is longer than its box shows `▲` and `▼` at its right edge, and the bottom line then names the keys that scroll it:

![The details of a mod on the Get mods page have the focus. Marks at the right edge show that there is more above and below, and the bottom line lists the keys that scroll.](/screenshots/en/get-mods-07-details-scroll.svg)

1. The part that has the focus has a bright title.
2. `▲` and `▼` mean that there are more lines above and below.
3. `↑` `↓`, `PgUp` `PgDn`, `Home` and `End` scroll.

## Menus on rows

On a row of a list, `Enter` opens a small menu of what you can do with that row. Move with `↑` and `↓`, choose with `Enter`, and close the menu with `Esc`.

![The menu of a mod on the Mods page: Details, Update, Update to version…, Uninstall.](/screenshots/en/mods-02-menu.svg)

### Disabled items say why

Something that cannot be done now stays on the screen, in gray. Move to it and lip says why below the menu. `Enter` does nothing on it.

![The menu of a mod that was installed from a URL. "Update to version…" is gray, and the line below says "Only a mod from a repository has versions."](/screenshots/en/mods-11-url-menu.svg)

## Dialogs

A dialog is a box that opens above the page. While it is open, it takes every key. The page behind it does not react until the dialog is closed.

There are a few kinds, and each works the same way wherever it appears.

**A question with buttons.** `←` and `→` choose a button, `Enter` presses it. `Esc` presses the last button, which is always the one that changes nothing.

![The question "Quit lip?" with the buttons Quit and Cancel.](/screenshots/en/dialog-02-quit.svg)

**A choice.** `↑` and `↓` move, and one sentence below the choices explains the highlighted one. `Enter` chooses, `Esc` cancels.

![A choice with two rows, "Browse…" and "Type a source…", and a sentence about the highlighted one.](/screenshots/en/mods-14-install-choice.svg)

**A text field.** Type, then press `Enter`. Gray text in an empty field is an example of what to type, not a value.

![A text field labeled "Source" that holds a repository name typed by the user.](/screenshots/en/mods-18-type-source.svg)

When lip cannot use what you typed, the dialog stays open and says what it expects:

![A text field that holds "tag:". Below it a line says "Give a version, branch:<name>, tag:<name> or rev:<commit>."](/screenshots/en/mods-23-update-to-invalid.svg)

**A message.** It has one button, Close.

![A message: "The catalogue has no entry for this mod.", with a Close button.](/screenshots/en/mods-33-not-in-catalogue.svg)

**A long text**, such as the README of a mod, opens in a window that fills the screen. Scroll with the arrow keys; `Esc` or `Enter` closes it.

![The README of a mod in a window that fills the screen.](/screenshots/en/get-mods-17-readme.svg)

## Preview, then Apply

Nothing that changes your server happens at once. Installing, updating or uninstalling a mod, and setting up a server, all go the same way.

### 1. The plan

lip first works out what it would do, and shows it. This is the plan. The lines that start with `Dry run:` are the changes.

![The plan of an uninstall: two lines that start with "Dry run: would uninstall", and the buttons Apply and Cancel.](/screenshots/en/plan-01-changes.svg)

1. What lip would do. Nothing has changed yet.
2. **Apply** carries the plan out. **Cancel**, or `Esc`, leaves everything as it is.

Because Cancel changes nothing, the plan is also how you find out what an action would do.

::: info
The lines of a plan, and everything else that a command says, are in English in every language of the interface.
:::

Two kinds of plan look different.

**Only lip's record changes.** No file of the game or of a mod would change, only what lip has written down about your mods. The plan says so in its last line. You can apply it or cancel it; your server behaves the same either way.

![A plan whose last line says "Only lip's record of the packages would change.", with the buttons Apply and Cancel.](/screenshots/en/plan-03-record-only.svg)

**Nothing would change.** There is nothing to apply, so the only button is **Close**.

![A plan that says "Nothing would change." and has only a Close button.](/screenshots/en/plan-02-no-changes.svg)

### 2. The license of the game

When the plan installs the game itself, lip then shows the links to the Minecraft End User License Agreement and the Microsoft Privacy Policy. Choose **Accept** to go on. **Decline** stops, and nothing is installed.

![The dialog "Minecraft EULA" with two links and the buttons Accept and Decline.](/screenshots/en/new-server-07-eula.svg)

### 3. Progress

While lip works, it shows what it downloads and what it does.

![The progress of a setup: one download is finished, another is at 62 percent.](/screenshots/en/new-server-08-progress.svg)

Press `Esc` or `Ctrl+C` to cancel. lip writes `Cancelling…`:

![The progress dialog after Esc: a line says "Cancelling…".](/screenshots/en/tour-04-cancelling.svg)

Then lip undoes what it had done so far, so your folder is as it was before, and says so:

![A dialog titled "Set up server · Cancelled" that says "Cancelled. Changes were undone.", with a Close button.](/screenshots/en/tour-05-cancelled.svg)

::: info
A change that you cancelled did not fail. Its dialog has "Cancelled" in its title and the frame of any other dialog, not the red frame of a failure.
:::

### 4. The result

When lip is done, it shows what it did, ending with `✓ Done.` Press `Enter` to close it.

![The result of a setup: the lines of what lip did, then "✓ Done." and an OK button.](/screenshots/en/new-server-09-result.svg)

### When something fails

A failure does not close lip. lip shows a dialog with a red frame and goes on afterwards.

![A failure dialog. It holds an error line, a hint line and the path of the full report.](/screenshots/en/tour-06-failure-hints.svg)

1. `error:` is what went wrong.
2. `hint:` is what you can do about it. Not every failure has a hint.
3. `Full report:` is the path of a file with every detail. It is in the folder `.lip\logs` of the workspace. Attach it when you ask for help.
4. **Close** closes the dialog.

A failure while lip works on the plan changes nothing. A failure after you chose Apply is undone as a cancel is. See [Troubleshooting](../guides/troubleshooting.md) for what the common failures mean.

## The folder and file browser

Whenever lip needs a folder or a file, it opens a browser, so you do not have to type a path.

![The folder browser, with numbers on its path bar, the row "..", the folders, the buttons and the bottom line.](/screenshots/en/tour-07-browser.svg)

1. **The path bar** shows the folder whose content is listed.
2. `..` is the folder one level up. `Enter` or `←` goes there.
3. The folders inside end with `\`. `Enter` or `→` opens the highlighted one.
4. **The buttons.** `Tab` goes to them from the list.
5. `Tab` goes round the path bar, the list and the buttons.

`Esc` closes the browser without choosing anything.

Folders and files that are hidden, or whose name starts with a dot, such as `.lip`, are not listed. A folder that lip is not allowed to read says so in one line.

### Typing a path

You can still type or paste a path. Press `Shift+Tab` in the list to go to the path bar, type, and press `Enter`. lip goes to that folder. `Esc` goes back to the list.

![The path bar has the focus and holds a path that was typed. The bottom line says "Enter Go".](/screenshots/en/tour-08-browser-path.svg)

### The drives

Above the top folder of a drive is the list of the drives. Open one to go on; nothing can be selected in this list itself.

![The list of the drives, C:\ and D:\, under the name "Computer". A line says "Open one of these first."](/screenshots/en/tour-09-browser-drives.svg)

On Linux and macOS there are no drives: the top is `/`.

### Its four forms

The browser looks a little different depending on what lip asks for.

**A folder that exists**, for example when you add a folder on the [Workspaces](./workspaces.md) page. This is the picture at the top of this section. **Select this folder** chooses the folder shown. **New folder…** asks for a name, makes that folder inside the one shown, and opens it.

**A folder that may be new**, for a new server. It looks the same, but **New folder…** makes nothing yet: lip creates the folder when it sets the server up.

![The folder browser of a new server, with the buttons Select this folder, New folder… and Cancel.](/screenshots/en/new-server-02-folder.svg)

![The dialog "New folder" with a name typed into its field.](/screenshots/en/new-server-03-folder-name.svg)

**A file or a folder**, when you install a mod from your disk on the [Mods](./mods.md) page. The list also shows the files that can be chosen, and a line above the buttons names their kind. `Enter` on a file chooses it; **Select this folder** chooses the folder shown.

![The browser with a folder and two .zip files. A line says "Files: .zip", and the buttons are Select this folder and Cancel.](/screenshots/en/mods-15-browse.svg)

**A file to write**, on the [Tools](./tools.md) page. A field below the list holds the name of the file. Go to the folder you want, change the name if you like, and choose **Save**.

![The browser with a field "File name" that holds "package.zip", and the buttons Save, New folder… and Cancel.](/screenshots/en/tools-04-pack-output.svg)

## While the server runs

While the server of the workspace runs, lip changes neither its mods nor the files of the game. This keeps a running server from losing files it has open.

| What | While the server runs |
| --- | --- |
| Installing, updating and uninstalling mods | Off. |
| Setting up the server, changing the game or the loader | Off. |
| `server.properties`, `allowlist.json`, `permissions.json` on the [Game config](./game_config.md) page | Can be read, not saved. |
| The configuration files of the mods on the [Mod config](./mod_config.md) page | Can be changed and saved. |
| Everything else: browsing mods, Tools, Settings | Works as usual. |

What is off stays on the screen in gray, with the reason "Stop the server first.":

![The Mods page while the server runs. A yellow line at the top says so, the two action rows are gray, and the details area says "Stop the server first."](/screenshots/en/mods-36-server-running-page.svg)

1. The page says that the server runs.
2. The rows that would change the mods are gray.
3. The details area says why.

![The menu of a mod while the server runs. "Update" is gray, and the line below the menu says "Stop the server first."](/screenshots/en/mods-04-server-running.svg)

Stop the server on the [Server](./server.md) page, make your change, and start it again.

This holds whoever started the server: this interface, another terminal, or the command `lip run`.

## Messages in the bottom line

A short message sometimes appears at the right of the bottom line. It tells you that something was done, or why nothing happened. It goes away by itself after six seconds, and you do not have to answer it.

![The Home page. At the right of the bottom line is the message "Create a server first."](/screenshots/en/home-06-need-server.svg)

A message is green when something worked, yellow for a warning, red for a failure, and blue for plain information.

## Small terminals

lip needs a terminal of at least 60 columns and 16 rows. In a smaller one it shows only this line, and the only key that works is `Ctrl+C`, which quits at once without asking:

![A terminal that is too small. It shows one line: "Enlarge the terminal to 60×16. Ctrl+C quits."](/screenshots/en/tour-10-too-small.svg)

Make the window larger and lip comes back as it was.

In a terminal narrower than 80 columns, the sidebar shrinks to one symbol for each page while the focus is in the page, so that the page has more room:

![A terminal of 60 columns. The sidebar shows one symbol for each page.](/screenshots/en/narrow-01-home.svg)

Press `Esc`, and the sidebar shows the names again:

![The same terminal with the focus in the sidebar, which shows the names of the pages.](/screenshots/en/narrow-02-sidebar.svg)

In a narrow terminal the bottom line may not have room for every key. `Esc` and the keys that act, such as `Enter`, are always shown; keys that only move are left out first.

## Quitting

Press `Esc` in the sidebar, or `Ctrl+C` anywhere except on the Server page while the server runs. lip asks before it quits:

![The question "Quit lip?" with the buttons Quit and Cancel.](/screenshots/en/dialog-02-quit.svg)

When a server runs, the question also says what happens to it. Normally the server keeps running:

![The question "Quit lip?" with a second line: "Running servers keep running."](/screenshots/en/server-12-quit-keeps-running.svg)

You can close the terminal, come back later, and run `lip` in the same folder to see the server again. The [Server](./server.md#quitting-lip-while-the-server-runs) page explains this, and the rare case in which the servers stop first.

## Languages

The interface is in English, Simplified Chinese or Traditional Chinese. At first it follows the language of your system. To choose one yourself, go to the [Settings](./settings.md) page and press `Enter` on **Language**. The screen changes at once.

![The Language setting: Follow system, English, 简体中文, 繁體中文.](/screenshots/en/settings-02-language.svg)

## Where to go next

- [Your first server](../intro/first_server.md) goes from an empty folder to a running server with mods, step by step.
- [Home](./home.md), [Server](./server.md), [Mods](./mods.md) and [Get mods](./get_mods.md) describe the pages you use most.
- [The interface in brief](../reference/interface.md) is a short reference of every page and key.
