# Tools

After this page you can use the four tools of the interface for people who make packages: create a `lip.toml`, migrate an old `tooth.json`, pack a ZIP, and look at the package file of any package.

If you only run a server, you do not need this page. To learn what a package is and how to publish one, read [Publish your mod with lip](https://levimc.org/tutorials/develop/publish-a-package/).

## The page

The tools work in the selected workspace. To work on a mod of yours, start `lip` in the folder of the mod, or add that folder on the [Workspaces](workspaces.md) page.

Open the page: in the sidebar, move to **Tools** and press `Enter`.

![The Tools page in the folder D:\projects\WelcomeMod: four actions, of which "Migrate tooth.json" is dimmed, one sentence about the selected one, and below it the workspace and whether it has a lip.toml and a tooth.json, both "None".](/screenshots/en/tools-05-status.svg)

1. What the selected action does. Move with `↑` and `↓`; `Enter` carries the action out.
2. The selected workspace: the folder that the actions work in.
3. Whether that folder has a `lip.toml` and a `tooth.json`: **Found** or **None**. lip looks again every two seconds, so the lines follow what you do outside lip.
4. A dimmed action: one that cannot work in this folder as it is. See [Dimmed actions](#dimmed-actions).

| Action | Does | Command |
| --- | --- | --- |
| **Create lip.toml** | Writes the package file of a new package. | [`lip pkg init`](../reference/commands/pkg_init.md) |
| **Migrate tooth.json** | Converts the package file of lip 0.x to a `lip.toml`. | [`lip pkg migrate`](../reference/commands/pkg_migrate.md) |
| **Pack ZIP…** | Packs a folder into a ZIP archive. | [`lip pkg pack`](../reference/commands/pkg_pack.md) |
| **View package…** | Shows the package file and the versions of a package. | [`lip pkg view`](../reference/commands/pkg_view.md) |

An action whose name ends in `…` asks before it does anything.

## Dimmed actions

The two files of the status lines decide which actions can work. An action that they rule out is dimmed. When you select it, a line below its sentence says why, and the bottom line of the screen no longer offers `Enter`.

![The action "Migrate tooth.json" is selected and dimmed. Below its sentence the page says "There is no tooth.json here."](/screenshots/en/tools-06-migrate-disabled.svg)

1. The reason.

| Action | Dimmed when | The page says |
| --- | --- | --- |
| **Create lip.toml** | The folder has a `lip.toml`. lip does not replace one. | **lip.toml already exists here.** |
| **Migrate tooth.json** | The folder has a `lip.toml`. | **lip.toml already exists here.** |
| **Migrate tooth.json** | The folder has no `tooth.json`. | **There is no tooth.json here.** |

**Pack ZIP…** and **View package…** are never dimmed. Whether a server runs makes no difference to this page: none of the four actions changes a server.

The action comes back as soon as the files allow it. To start over with a new `lip.toml`, delete the old one in a file manager; within two seconds the page shows `lip.toml` **None**, and **Create lip.toml** can be chosen again.

## Create lip.toml

1. Move to **Create lip.toml** and press `Enter`. lip asks for the package id.

   ![A dialog titled "lip asks" above the progress dialog of "Create lip.toml". It asks for "the package id of the package (a Go module path, e.g. github.com/owner/repo)", and the field holds github.com/steve/WelcomeMod.](/screenshots/en/tools-07-init-question.svg)

2. Type the id and press `Enter`. The id is the address of the repository of the package, such as `github.com/owner/repo`. An answer that is not such an address is asked for again. `Esc` cancels, and nothing is written. The question itself comes from the command, and is in English.

3. lip writes `lip.toml` in the workspace and says so. Press `Enter`.

   ![The dialog "Create lip.toml" says "Created lip.toml." and has the button OK.](/screenshots/en/tools-08-init-result.svg)

The file has the id you gave, the version `0.1.0`, and one entry for a server on Windows x64:

```toml
format = 4
id = "github.com/steve/WelcomeMod"
version = "0.1.0"

[[variants.""]]
side = "server"
platform = "win-x64"
```

Edit it in a text editor from here on; [lip.toml](../concepts/lip_toml.md) describes every field.

Now that the file exists, the action is dimmed:

![The action "Create lip.toml" is selected and dimmed. Below its sentence the page says "lip.toml already exists here.", and the status line of lip.toml reads Found.](/screenshots/en/tools-09-init-disabled.svg)

1. lip does not replace a `lip.toml`.

## Migrate tooth.json

Packages of lip 0.x describe themselves in a `tooth.json`. This action writes the `lip.toml` that says the same, and deletes the `tooth.json`.

1. Move to **Migrate tooth.json**. It can be chosen when the status lines read `lip.toml` **None** and `tooth.json` **Found**.

   ![The action "Migrate tooth.json" is selected. The status lines read lip.toml None and tooth.json Found.](/screenshots/en/tools-10-migrate.svg)

   1. The status lines that make the action possible.

2. Press `Enter`. When the `tooth.json` lacks something that a `lip.toml` needs, lip asks for it, once for each such value. Here the old file did not say which platform it is for:

   ![A dialog titled "lip asks" asks for "the platforms (RIDs, comma-separated, e.g. win-x64) the variants without a platform apply to", and the field holds win-x64.](/screenshots/en/tools-11-migrate-question.svg)

   Type the answer and press `Enter`. An answer that is not valid is asked for again.

3. lip shows the result. Press `Enter`.

   ![The dialog "Migrate tooth.json" shows a line that starts with a warning sign about preserve_files, and ends with "Migrated tooth.json to lip.toml." and the button OK.](/screenshots/en/tools-12-migrate-result.svg)

   1. A note, marked `⚠`: something that the new file says in another way than the old one, or that it has no place for. Read each note and check that part of `lip.toml`. The notes are in English.
   2. The result: `lip.toml` is written, and `tooth.json` is deleted.

4. The status lines now read the other way round, and the action is dimmed: there is nothing left to migrate.

   ![The status lines read lip.toml Found and tooth.json None. The page says "lip.toml already exists here."](/screenshots/en/tools-13-migrated.svg)

[Migrating packages](../authors/migrating_packages.md) explains what changes between the two formats.

## Pack ZIP…

This action packs a folder into the ZIP archive that you attach to a release of your package. It asks two things: the folder, and the file to write.

1. Move to **Pack ZIP…** and press `Enter`. The folder browser opens in the workspace, titled **Folder to pack**.

   ![The folder browser "Folder to pack" shows D:\projects\WelcomeMod with the folder WelcomeMod inside, and the buttons "Select this folder", "New folder…" and "Cancel".](/screenshots/en/tools-14-pack-folder.svg)

   1. The folder that will be packed. To pack the workspace itself, leave it as it is; otherwise open another folder.
   2. Press `Tab` to reach the buttons, and `Enter` on **Select this folder**.

2. A second browser opens, titled **Save ZIP**. It offers the name `package.zip` in the workspace.

   ![The browser "Save ZIP" shows the same folder with a file package.zip, a field "File name" that holds package.zip, the note "Files: .zip", and the buttons Save, "New folder…" and Cancel.](/screenshots/en/tools-15-pack-name.svg)

   1. The folder that the ZIP is written to. Open another folder to save it elsewhere.
   2. The name of the ZIP. Press `Tab` to go there and type another name; `.zip` is added when you leave it out. `Enter` in this field saves.

   `Esc` goes back to the first step.

3. When a file of that name exists, lip asks before it replaces it. `Enter` presses **Cancel** first, which leads back to the name; press `←` and then `Enter` to replace.

   ![The dialog "Pack ZIP" says "package.zip already exists. Replace it?" with the buttons Replace and Cancel.](/screenshots/en/tools-16-pack-replace.svg)

4. lip writes the archive and names it. Press `Enter`.

   ![The dialog "Pack ZIP" says that the archive is packed, gives its path D:\projects\WelcomeMod\package.zip, and has the button OK.](/screenshots/en/tools-17-pack-result.svg)

The archive holds every file of the folder at its path in the folder, except the archive itself, and the folders `.git` and `.lip` at the top of the folder.

[The folder and file browser](tour.md#the-folder-and-file-browser) describes the browser in full.

## View package…

This action shows what lip knows of a package before you install it: its package file and the versions that exist. It changes nothing in the workspace.

Move to **View package…** and press `Enter`. lip asks where the package is:

![The dialog "View package" with three rows: "Installed mod…", "Type a package…" and "Browse…". The line below says what the selected one shows.](/screenshots/en/tools-02-view-choice.svg)

`Esc` in any of the three leads back to this choice.

### An installed mod

1. Press `Enter` on **Installed mod…**. lip lists what is installed in the workspace: the mods that you installed, then what they depend on, then the game and the loader.

   ![The dialog "Installed mods" lists seven packages with their versions; the line below it gives the name of the selected one in full.](/screenshots/en/tools-18-view-installed.svg)

   1. The installed version.
   2. A dimmed row: a package that is installed only because another one needs it.

   A long name is cut in its middle; the line below the list has it in full.

2. Press `Enter` on a package. lip reads its package file, which may need the network, and shows the result. `Esc` stops waiting.

   ![A viewer titled github.com/LiteLDev/LegacyScriptEngine#lua@0.13.1 shows JSON that starts with "manifest".](/screenshots/en/tools-19-view-result.svg)

   1. `manifest`: the package file of the installed version, as JSON. Further down, `versions` lists every version that the repository has.

   Scroll with `↑`, `↓`, `PgUp`, `PgDn`, `Home` and `End`; `Esc` closes the viewer.

In a workspace without packages there is nothing to list:

![The dialog "Installed mods" says "Nothing is installed here." and has the button Close.](/screenshots/en/tools-22-view-nothing.svg)

### A package that you type

Press `Enter` on **Type a package…** and type the package as you would on the command line: a repository, with `@` and a version if you want a certain one, or the address of a ZIP file, or a path.

![The dialog "View package" with the field Package, which holds github.com/LiteLDev/LeviOptimize@1.8.1.](/screenshots/en/tools-20-view-typed.svg)

Press `Enter`. The result is shown as for an installed mod.

### A file or a folder on your disk

Press `Enter` on **Browse…**. The browser lists folders and `.zip` files.

![The browser "Choose package" shows D:\servers\survival with the folder mods and the file LeviAntiCheat-windows-x64.zip, the note "Files: .zip", and the buttons "Select this folder" and Cancel.](/screenshots/en/tools-21-view-browse.svg)

1. A ZIP file: `Enter` shows the package in it.
2. **Select this folder** shows the folder in the bar as a package: one that has a `lip.toml`, such as the folder of your own mod.
3. The kind of files that the list shows.

## When an action fails

The page dims an action only for what the two status lines tell. An action can still fail for another reason: a `tooth.json` that is broken, a package that cannot be found, a folder that was deleted a moment ago. lip then shows a dialog titled with the name of the action and **Failed**, and changes nothing.

![The dialog "Migrate tooth.json · Failed" starts with "error: Cannot read tooth.json", goes on with three lines that start with "caused by:", and names a report file.](/screenshots/en/tools-23-failed.svg)

1. **error:** what went wrong, and often what to do about it. Each line **caused by:** goes one step deeper into the reason; the last one is usually the most exact. Here the `tooth.json` has no `version`. This text comes from the command and is in English.
2. **Full report:** a file with the details, below `.lip/logs` of the workspace. Attach it when you ask for help; see [Troubleshooting](../guides/troubleshooting.md).

Press `Enter` or `Esc` to close the dialog.
