# Mod config

After this page you can change the settings of your mods from the interface, without looking for their files or editing JSON by hand.

The Mod config page lists the configuration files of the installed mods. A JSON file opens as a tree of names and values that you change one at a time. A file in another format is shown, to be edited in a text editor.

## Which files the page lists

Each mod has a folder of its own in the server, `mods/<mod>/`, where `<mod>` is the name of the mod. For each installed mod, lip lists:

- every file in `mods/<mod>/config/`, and in the folders below it; or,
- when there is none: the `.json`, `.ini`, `.toml`, `.yml` and `.yaml` files directly in `mods/<mod>/` (but not `manifest.json`), and `mods/<mod>/data/config.json`.

Most mods write their configuration file when the server first starts with them. lip creates no configuration file.

## The files

Open the page: in the sidebar, move to **Mod config** and press `Enter`.

![The Mod config page: the list of files at the left with six rows, and the file of GMLIB open at the right with its three values.](/screenshots/en/mod-config-03-files.svg)

1. A file: the name of the mod's folder, then the path of the file inside the folder of the mod. A mod with several files has several rows.
2. A mod without a configuration file, dimmed.
3. The selected file. The page opens on the first file of the list.

Move with `↑` and `↓`: the right side follows. In a list that is too narrow for a long name, the end of the name is cut; the right side shows the mod in its title and the path of the file in its first line.

A mod without a file has nothing to show:

![LegacyRemoteCall is selected in the list. The right side says "No config file. Starting the server may create one."](/screenshots/en/mod-config-04-no-file.svg)

1. lip found no file of this mod where it looks. The mod may write one when the server next starts.

When no installed mod has a folder of its own, or no mod is installed at all, the page says **No configurable mods.** `Enter` on **Get mods** opens the [Get mods](get_mods.md) page.

![The Mod config page of a server without mods: the text "No configurable mods." and the button "Get mods".](/screenshots/en/mod-config-02-empty.svg)

## Edit a JSON file

Press `Enter` or `→` on a file to go into it. The page shows one level of the file at a time: a row for each name, with its value.

![The file of LeviOptimize, inside its group "features": the group optPushEntity with 3 items, and five values that are true.](/screenshots/en/mod-config-01-tree.svg)

1. The mods and their files.
2. The values of the file, one level at a time.
3. **Save** and **Discard**: nothing is written until you save.

| A value shown as | Is | `Enter` |
| --- | --- | --- |
| `{…} 3 items` | A group of named values (a JSON object). | Opens it. |
| `[…] 1 item` | A list of values (a JSON array). | Opens it. |
| `[x] true`, `[ ] false` | True or false. | Switches it; so does `Space`. |
| `3`, `0.05` | A number. | Asks for another number. |
| `"en"` | A text. | Asks for another text. |
| `null` | Empty. | Asks for a value of any kind. |

The line below the rows says what kind of value the selected row holds.

### Move through the file

1. Move to a group or a list and press `Enter` to open it.
2. Press `Esc` or `←` to go back up one level. At the top level, `Esc` goes back to the list of files.

![Two levels down in the file of LeviOptimize. The first line reads "… › features › optPushEntity", and the rows are enabled, maxPushTimes and a third name that is cut.](/screenshots/en/mod-config-06-location.svg)

1. Where you are: the file, then each level that you opened. When the line is too long, its start is replaced by `…`.
2. A value that is true or false.
3. A number.

A name that is too long for its column is cut with `…`.

### Change a value

On a number or a text, `Enter` opens a field with the value. Type the new one and press `Enter`; `Esc` closes the field without a change.

A value keeps its kind. A number takes a number only; a text takes what you type as it is, spaces included, so `5` typed into a text stays the text `"5"`.

![The field of def_money, labelled Number, holds "a lot". Below it the message "Enter a number." is shown.](/screenshots/en/mod-config-10-number.svg)

1. The kind of value that the row holds.
2. Why the value is not taken. Correct it and press `Enter` again.

On a value that is true or false, `Space` or `Enter` switches it at once.

### Change a list

A list shows its values with their positions, `[0]`, `[1]`, and so on, and ends with the row **Add value…**.

![The list ignoredDimensions with one value, "the_end", at position 0, and the row "Add value…" below it.](/screenshots/en/mod-config-07-array.svg)

1. A value of the list.
2. The row that adds one.

To add a value, press `Enter` on **Add value…** and type it. What you type is read as JSON: `42` is a number, `true` is true, `{"a": 1}` is a group. Anything that is not JSON becomes a text, so you need no quotes around a word.

![The Add value dialog with one field, Value, which holds the word nether.](/screenshots/en/mod-config-08-add-value.svg)

The value is added at the end of the list.

To change or remove a value of a list, press `Enter` on it. Its menu has **Edit…** (or **Toggle**, or **Open**, by the kind of value) and **Remove**.

![The menu of the value at position 1 with two rows, "Edit…" and "Remove".](/screenshots/en/mod-config-09-element-menu.svg)

::: info What the page cannot do
Only lists grow and shrink. The page does not add a name to a group, remove one, or rename one: the mod decides which names its file has. For such a change, edit the file in a text editor.
:::

### The version row

Many mods keep a whole number named `version` at the top of their file, to know which layout the file has. The page shows it dimmed and does not let you change it: `Enter` does nothing there, and the line below the rows says why.

![The top level of the file of LeviOptimize. The row "version 4" is selected, and the line below the rows says "The mod keeps this version itself."](/screenshots/en/mod-config-05-version.svg)

1. Why `Enter` does nothing on this row.

## Save or discard your changes

Nothing is written while you edit. The page marks a file that has changes:

![The file money.json of LegacyMoney with a changed value. "Unsaved" stands at the top right, a dot follows the file in the list, and the Save and Discard buttons are bright.](/screenshots/en/mod-config-11-unsaved.svg)

1. **Unsaved**, at the right of the first line.
2. A dot `●` after the file in the list, when the list has room for it.
3. **Save** and **Discard**, which can be chosen now.

To save:

1. Press `Tab` until the buttons have the focus. From the last row, `↓` goes there too.

   ![The Save button of the file has the focus.](/screenshots/en/mod-config-12-save.svg)

2. Press `Enter` on **Save**. lip writes the file and says **Saved.** at the bottom right.

   ![The file after saving: "Unsaved" and the dot are gone, and the bottom line ends with "Saved."](/screenshots/en/mod-config-13-saved.svg)

lip writes the whole file again: the values as you left them, each name where it was, indented with four spaces. Comments in the file are not kept. A file that was deleted meanwhile is not created again: lip says **The file is gone.**

**Discard** asks first, then reads the file again, which drops your changes.

### Go elsewhere with unsaved changes

You edit one file at a time. When you select another file, go to another page, select another workspace, or quit lip while the file has changes, lip asks what to do with them.

![The dialog "Unsaved changes" names "LeviOptimize · config/config.json", asks "Save the changes?", and has the buttons Save, Discard and Cancel.](/screenshots/en/mod-config-17-unsaved-question.svg)

| Button | Does |
| --- | --- |
| **Save** | Saves the file, then goes on. |
| **Discard** | Drops the changes, then goes on. |
| **Cancel**, or `Esc` | Stays in the file, with the changes as they are. |

### A file that changed on disk

Mods write their configuration files themselves, some of them while the server runs. When the file is no longer what lip read, saving does not overwrite it unasked.

![The dialog "Save config/config.json" says "Changed on disk since it was read. Overwrite?" and "Reload drops your changes.", with the buttons Overwrite, Reload and Cancel.](/screenshots/en/mod-config-16-changed-on-disk.svg)

1. **Overwrite** writes your version over the one on disk. What the mod wrote is lost.
2. **Reload** shows the file as it is on disk now. Your changes are lost.
3. **Cancel**, which `Enter` presses first, writes nothing and keeps your changes on the screen.

## Files that are only shown

A file that is not JSON is shown as text, below the line **Read-only. Use a text editor.** Change it in a text editor, outside lip.

![The file ranking.yml of LegacyMoney, shown as text below the line "Read-only. Use a text editor."](/screenshots/en/mod-config-14-read-only.svg)

A `.json` file that is not valid JSON is shown the same way, below **Read-only: not valid JSON.** and what the JSON reader says about it. Comments (`//` and `/* */`) and a comma after the last value do not make a file invalid: lip reads them.

![The file money.json of LegacyMoney, shown as text. The line above it starts with "Read-only: not valid JSON." and goes on in English with what is wrong.](/screenshots/en/mod-config-15-invalid-json.svg)

1. Why the file is not a tree. What the JSON reader says is in English in every language.
2. The mistake, here a missing colon. Correct it in a text editor and open the page again.

When the text is longer than the area, `Enter` goes into it; `↑`, `↓`, `PgUp`, `PgDn`, `Home` and `End` scroll, and `Esc` goes back to the list.

## While the server runs

Unlike the [Game config](game_config.md) page, this page lets you change and save while the server runs. The last line of the page then says **Changes may need a server restart.**: many mods read their configuration only when the server starts.

![The Mod config page while the server runs. The last line of the page reads "Changes may need a server restart.", and the bottom line ends with "Saved. May need a restart."](/screenshots/en/mod-config-18-server-running.svg)

1. The note, shown for as long as the server runs.
2. What saving says meanwhile.

Whether a change needs a restart depends on the mod. When in doubt, restart the server on the [Server](server.md) page.

## In a narrow terminal

When the terminal is too narrow to show the list and the file side by side, under about 90 columns, the page shows one of them at a time.

1. The list of files fills the page. Press `Enter` on a file.

   ![The Mod config page in a terminal of 72 columns: only the list of files is shown, and the sidebar shows icons.](/screenshots/en/mod-config-19-narrow-files.svg)

2. The file fills the page. Its first line names the mod and the file. Press `Esc` at the top level to go back to the list.

   ![The same terminal with the file of GMLIB open: its first line reads "GMLIB · config/config.json", and its three values follow.](/screenshots/en/mod-config-20-narrow-file.svg)

Everything else works as in a wide terminal. [Small terminals](tour.md#small-terminals) says more about narrow terminals.
