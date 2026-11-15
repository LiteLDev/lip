# Game config

After this page you can change the settings of the game itself from the interface: the properties of the server, who may join it, and what each player is allowed to do.

The Game config page edits three files in the folder of the server. You never open them in a text editor: the page shows each as a list, and writes it back when you save.

| Section | File | Holds |
| --- | --- | --- |
| **Properties** | `server.properties` | The settings of the server: its name, the game mode, the port, and so on. |
| **Allowlist** | `allowlist.json` | The players who may join while the property `allow-list` is on. |
| **Permissions** | `permissions.json` | The players who get their own permission instead of the default one. |

::: info When a change takes effect
The server reads these files when it starts. A change that you save takes effect the next time the server starts; the page says so at its bottom right: **Applies on next server start.**
:::

## Find your way around

Open the page: in the sidebar, move to **Game config** with `↓` and press `Enter`.

![The Game config page: the three sections at the left, the properties of the server at the right, the description of the selected property below them, and the Save and Discard buttons at the bottom.](/screenshots/en/game-config-01-properties.svg)

1. The three sections. `↑` and `↓` choose one; `Enter` or `→` goes into its rows.
2. The rows of the section. In **Properties**, a row is a property and its value.
3. The description of the selected row. For a property it is the comment that the game wrote below it in `server.properties`, in English, with the values that are allowed.
4. **Save** and **Discard**. They can be chosen only while you have changes.

| Key | Does |
| --- | --- |
| `↑` `↓` | Move in the sections or in the rows. |
| `Enter` | In the sections: goes into the rows. On a row: changes it, or opens its actions. |
| `Space` | Switches a property that is on or off. |
| `Tab` | Goes on to the next part: sections, rows, buttons. |
| `Esc` | Goes back one part: buttons, rows, sections, sidebar. |

The bottom line of the screen always names the keys that work where you are. [A tour of the interface](tour.md) explains the keys that every page shares.

## Change a property

Go into the rows of **Properties** with `Enter`. What `Enter` does on a property depends on the kind of value it holds. lip reads the kind from the line `Allowed values:` of the description.

### Switch a property on or off

A property that is `true` or `false` shows a box: `[x]` is `true`, `[ ]` is `false`.

1. Move to the property, here `allow-cheats`.

   ![The property allow-cheats is selected. Its value is an empty box, and its description below the list says that cheats like commands can be used when it is true.](/screenshots/en/game-config-05-boolean.svg)

   1. The value: an empty box is `false`, a checked one is `true`.
   2. What the file says about the property.

2. Press `Space`, or `Enter`. The box changes.

   ![allow-cheats now shows a checked box. A star stands before it and after the section name Properties, the description starts with "Original: false", and the Save and Discard buttons are bright.](/screenshots/en/game-config-06-changed.svg)

Nothing is written yet. The page marks what you changed:

1. A star `*` before the property.
2. A star after the name of the section.
3. **Original:** and the value that the file still holds, in the description.
4. **Save** and **Discard** can now be chosen.

### Choose one of a few values

A property with a fixed set of values, such as `gamemode`, opens a list.

1. Move to the property and press `Enter`.
2. Choose a value with `↑` and `↓`, and press `Enter`. `Esc` closes the list without a change.

![A list titled gamemode with the values survival, creative and adventure; survival is selected.](/screenshots/en/game-config-02-choose.svg)

### Type a number or a text

Any other property opens a field with its value. Type the new value and press `Enter`; `Esc` closes the field without a change.

A number must be a whole number. When the file gives a range or a least value, the field names it, and a value outside it is not taken: the field stays open and says why. A property that the file describes as a positive integer, such as `max-players`, takes neither 0 nor a negative number, and `view-distance` takes nothing below 5.

![The field of server-port, labelled "Value (1–65535)", holds 70000. Below it the message "Enter an integer from 1 to 65535." is shown.](/screenshots/en/game-config-08-number.svg)

1. The range that the file allows.
2. Why the value is not taken. Correct it and press `Enter` again.

A text is one line, as you type it.

![The field of server-name holds "Survival SMP".](/screenshots/en/game-config-09-text.svg)

### Take a change back

On a property that you changed, `Enter` opens a menu instead: its first row changes the value again, and **Reset value** puts back the value that the file holds.

![The menu of the changed property allow-cheats with two rows, Toggle and Reset value.](/screenshots/en/game-config-07-reset.svg)

`Space` still switches an on/off property directly.

## Edit the allowlist

The allowlist names the players who may join. The server uses it only while the property `allow-list` in **Properties** is on.

1. In the sections, move to **Allowlist** and press `Enter`. The first row is **Add player…**; below it are the players, each with its XUID when the file has one. A player who may join a full server is marked **ignores player limit**.

   ![The Allowlist section: the row "Add player…", then the players Steve, Alex, Noor and Kai with their XUIDs. Steve is marked "ignores player limit".](/screenshots/en/game-config-03-allowlist.svg)

2. Press `Enter` on **Add player…**. Type the name, press `Tab`, and type the XUID if you know it.

   ![The Add player dialog with two fields: Name holds Sunny, and "XUID (optional)" holds a number of sixteen digits.](/screenshots/en/game-config-12-add-player.svg)

   1. **Name**: the gamertag of the player. A name that is on the list already is not taken.
   2. **XUID (optional)**: the number of the player's Xbox account, digits only. Leave it empty when you do not know it.

3. Press `Enter`. The player is added at the end of the list and selected.

4. Press `Enter` on a player to open its actions.

   ![The menu of the player Sunny with two rows: "Ignore player limit" and "Remove".](/screenshots/en/game-config-13-player-menu.svg)

   1. **Ignore player limit** lets the player join when the server is full. For a player who has it, the row reads **Apply player limit** and takes it away again.
   2. **Remove** takes the player off the list.

   ![The row of Sunny now ends with "ignores player limit".](/screenshots/en/game-config-14-ignores-limit.svg)

## Edit the permissions

The permissions give single players another permission than the one every new player gets, which is the property `default-player-permission-level` in **Properties**.

1. In the sections, move to **Permissions** and press `Enter`.

   ![The Permissions section: the row "Add player…", then two players shown by XUID, one with the permission operator and one with member.](/screenshots/en/game-config-15-permissions.svg)

   1. A player. This file knows players by XUID only, not by name.
   2. The permission of that player.
   3. What the list is for.

2. Press `Enter` on **Add player…** and type the XUID of the player.

   ![The Add player dialog of the permissions with one field, XUID, which holds a number of sixteen digits.](/screenshots/en/game-config-16-add-permission.svg)

   ::: tip Where to find an XUID
   The allowlist shows the XUID of every player whose XUID the file has. The server also writes it to its output when a player connects; see [Server](server.md).
   :::

3. Press `Enter` and choose the permission. The list starts on `member`, or on the permission that the player has when the XUID is on the list already; then the permission of that player changes and no second row is added.

   ![A list titled with the XUID: operator, member and visitor. member is selected, and the line below says "Can play and build."](/screenshots/en/game-config-17-permission.svg)

   | Permission | The player |
   | --- | --- |
   | `operator` | Can use every command. |
   | `member` | Can play and build. |
   | `visitor` | Can only look around. |

4. Press `Enter` on a player to open its actions: **Change permission…** opens the same list, and **Remove** takes the player off the list.

   ![The menu of a player of the permissions with two rows: "Change permission…" and "Remove".](/screenshots/en/game-config-18-permission-menu.svg)

## Save or discard your changes

The changes of all three sections are saved together.

1. Press `Tab` until the buttons have the focus. From the last row of a list, `↓` goes there too.

   ![The Save button has the focus. The note at the bottom right reads "Applies on next server start."](/screenshots/en/game-config-10-save.svg)

   1. The note of the page: the server reads the files when it starts.

2. Press `Enter` on **Save**. lip writes every file that has changes, and says **Saved.** at the bottom right. The stars are gone.

   ![The properties after saving, without stars. The bottom line ends with "Saved."](/screenshots/en/game-config-11-saved.svg)

What lip writes:

- In `server.properties`, only the values of the properties that you changed. Every other character of the file stays, comments included.
- `allowlist.json` and `permissions.json` are written whole. The players keep their order, and what the page does not show of a player is kept. Comments in these files are not kept.
- `allowlist.json` and `permissions.json` are created when they do not exist. `server.properties` is not; see [Files that cannot be edited](#files-that-cannot-be-edited).

To drop your changes instead, move to **Discard** with `→` and press `Enter`. lip asks first; then it reads the three files again.

![The dialog "Discard changes" asks "Discard the changes?" with the buttons Discard and Cancel.](/screenshots/en/game-config-19-discard.svg)

### Leave the page with unsaved changes

When you go to another page, select another workspace, or quit lip while changes are not saved, lip asks what to do with them.

![The dialog "Unsaved changes" asks "Save the changes?", names allowlist.json and permissions.json, and has the buttons Save, Discard and Cancel.](/screenshots/en/game-config-20-unsaved.svg)

| Button | Does |
| --- | --- |
| **Save** | Saves the files it names (1), then goes on. |
| **Discard** | Drops the changes, then goes on. |
| **Cancel**, or `Esc` | Stays on the page, with the changes as they are. |

### A file that changed on disk

lip reads the files when the page is shown. When a file is no longer what lip read, because the server or you changed it meanwhile, saving does not overwrite it unasked.

![The dialog "Save game config" says "Changed on disk since it was read. Overwrite?", names allowlist.json, and has the buttons Overwrite and Cancel.](/screenshots/en/game-config-21-changed-on-disk.svg)

1. The file that changed. Nothing was written yet, of this file or any other.
2. **Overwrite** writes your version over the one on disk. **Cancel**, which `Enter` presses first, writes nothing; your changes stay on the page.

To see what the file holds now, choose **Cancel**, then **Discard**.

## Files that cannot be edited

A new server has no `server.properties` until it has run once: the game writes the file when it first starts. The page says so and creates nothing. Start the server once on the [Server](server.md) page, stop it, and come back.

![The Properties section shows no rows, only a sentence that says that server.properties does not exist yet and that starting the server once creates it.](/screenshots/en/game-config-22-missing.svg)

1. The server writes the file when it first starts.

A file that lip cannot read, such as an `allowlist.json` with a mistake in it, is shown as it is and never written. Correct it in a text editor.

![The Allowlist section shows a warning, the message of the JSON reader with a line number, and below a rule the text of allowlist.json.](/screenshots/en/game-config-23-unreadable.svg)

1. lip does not write a file that it cannot read. Saving the other sections leaves it alone.
2. Where the file is broken. This message is in English in every language.
3. The file as it is. When it is longer than the area, `Enter` goes into it and `↑` `↓` scroll.

## While the server runs

While the server of the selected workspace runs, the page only shows the three files: you can look through them, but nothing can be changed or saved. This holds whoever started the server, the interface or a `lip run` in another terminal. Stop the server on the [Server](server.md) page to edit.

![The Game config page while the server runs. The top bar says Running, and the note at the bottom right reads "Server runs: stop it to edit."](/screenshots/en/game-config-04-server-running.svg)

`Enter` on a row still opens its actions, so that you see what could be done. They are dimmed, and the menu says why: **Stop the server first.**

![The menu of allow-cheats while the server runs: its only row, Toggle, is dimmed, and below a rule it says "Stop the server first."](/screenshots/en/game-config-26-running-menu.svg)

When the server starts while you have unsaved changes, the changes are kept on the page:

![The page with an unsaved change while the server runs. The note at the bottom right reads "Server runs: stop it to save."](/screenshots/en/game-config-24-running-unsaved.svg)

1. The note changes to **Server runs: stop it to save.** Once the server has stopped, save as usual.
2. Meanwhile **Discard** is the only button that works.

Leaving the page then cannot save either. lip offers to discard the changes or to stay:

![The dialog "Unsaved changes" says "Changes cannot be saved while the server runs.", names server.properties, and has the buttons Discard and Cancel.](/screenshots/en/game-config-25-running-leave.svg)

When the server has stopped, lip reads the three files again, since the server may have written them. It does not do so while you have unsaved changes; saving them then asks before it overwrites [a file that changed on disk](#a-file-that-changed-on-disk).

The configuration files of mods are different: they can be saved while the server runs. See [Mod config](mod_config.md).
