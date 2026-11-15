# Workspaces

After this page you can keep several servers side by side: create a new one, switch between them, add a server that you already have, and change the game or the LeviLamina version of one.

## What a workspace is

A **workspace** is a folder that holds one server: the game, LeviLamina, the mods, the worlds, and lip's own records in `.lip/`. Every page of the interface works on one workspace at a time, the **selected workspace**. The top bar names it.

lip keeps a list of the workspaces that you used. When you start `lip`, the folder you started it in is added to the list, if it is not there yet, and selected. The list is kept between runs.

## The list

Open the page: in the sidebar, move to **Workspaces** and press `Enter`.

![The Workspaces page: the rows "New server…" and "Add existing folder…", then three folders. The first has a dot, and the last is marked "missing".](/screenshots/en/workspaces-01-list.svg)

1. Two actions: **New server…** creates a server, and **Add existing folder…** adds a folder that has one.
2. The selected workspace, marked with `●`.
3. A workspace whose folder no longer exists, marked **missing**.

Move down into the list with `↓`, or press `Tab`. The area below the list shows the whole path of the row that has the focus, and one sentence about it.

![The list with the focus on the first folder. The second folder is marked "server running" and the third "missing". Below the list are the path of the first folder and "The selected workspace."](/screenshots/en/workspaces-03-marks.svg)

1. `●`: the selected workspace.
2. **server running**: the server of this workspace runs, whoever started it.
3. **missing**: the folder was deleted, moved or renamed. lip can only take it off the list.
4. The whole path, which the list cuts at its start when it is too long, and what the row is.

`Enter` on a workspace opens its actions. Which ones there are depends on the workspace:

| Action | Offered for | Does |
| --- | --- | --- |
| **Switch** | Every workspace but the selected one and missing ones. | [Selects the workspace](#switch-to-another-workspace). |
| **Change game or loader version…** | Every workspace but missing ones. | [Sets the server up again](#change-the-game-or-the-loader-version) with other versions. |
| **Remove from list** | Every workspace but the selected one. | [Takes it off the list](#remove-a-workspace-from-the-list); no file changes. |

## Switch to another workspace

1. Move to the workspace and press `Enter`.

   ![The menu of the folder creative with three rows: "Switch", "Change game or loader version…" and "Remove from list".](/screenshots/en/workspaces-02-menu.svg)

2. Press `Enter` on **Switch**.

   ![After switching: the top bar and a message at the bottom right name D:\servers\creative, and the dot stands before that folder in the list.](/screenshots/en/workspaces-09-switched.svg)

   1. The top bar names the new workspace. Every page now shows it: its server, its mods, its configuration.
   2. The `●` moved to it.

Switching stops nothing. A server that runs in the workspace you left keeps running, and the list marks it **server running**.

When the [Game config](game_config.md) or the [Mod config](mod_config.md) page has unsaved changes, lip asks what to do with them before it switches.

## Add an existing folder

Use this for a server that lip does not list yet: one that you set up with the commands, or copied from another computer.

1. Move to **Add existing folder…** and press `Enter`.

   ![The row "Add existing folder…" is selected. The line below the list says "Adds a folder that already holds a server."](/screenshots/en/workspaces-10-add-row.svg)

2. The folder browser opens in the selected workspace. Go to the folder of the server: `←` goes up one level, `↑` and `↓` move, and `Enter` or `→` opens a folder.

   ![The folder browser, titled "Add folder", shows D:\servers with the folders creative, lobby, skyblock and survival; lobby is selected.](/screenshots/en/workspaces-11-browse.svg)

   1. The folder that the list shows. You can also type or paste a path here.
   2. A folder inside it.

3. When the browser shows the folder of the server, press `Tab` to reach the buttons, and `Enter` on **Select this folder**.

   ![The browser shows D:\servers\lobby with its folders behavior_packs, mods, resource_packs and worlds. The button "Select this folder" is marked.](/screenshots/en/workspaces-12-select-folder.svg)

4. lip adds the folder to the end of the list and selects it.

   ![The list now ends with D:\servers\lobby, which has the dot. The top bar names it too.](/screenshots/en/workspaces-13-added.svg)

lip does not look into the folder: any folder that exists can be added. A folder that is on the list already is not added twice; it is selected. [The folder and file browser](tour.md#the-folder-and-file-browser) describes the browser in full.

## Remove a workspace from the list

Removing takes the workspace off the list and nothing more. **No file is deleted**: the server stays on your disk, and you can add its folder again later.

1. Move to the workspace and press `Enter`. For a missing folder, **Remove from list** is the only action.

   ![The menu of the missing folder skyblock with one row, "Remove from list".](/screenshots/en/workspaces-06-menu-missing.svg)

2. Press `Enter` on **Remove from list**. lip asks first.

   ![The dialog of skyblock asks "Remove it from the list? No file is deleted." with the buttons "Remove from list" and "Cancel".](/screenshots/en/workspaces-07-remove.svg)

3. Press `Enter` on **Remove from list** again.

   ![The list without skyblock. The bottom line ends with "Removed from the list."](/screenshots/en/workspaces-08-removed.svg)

The selected workspace cannot be removed: its menu has no such row. Switch to another workspace first.

![The menu of the selected workspace survival with one row, "Change game or loader version…".](/screenshots/en/workspaces-04-menu-selected.svg)

## Create a new server

**New server…** is a wizard: it asks a few questions, one dialog after the other, and then installs the server. `Esc` goes back one step; in the first step it closes the wizard, and nothing has changed.

The same wizard opens from the first step of the [Home](home.md) page. [Your first server](../intro/first_server.md) walks through it once from start to end; this section describes every choice.

### Step 1: where the server goes

Press `Enter` on **New server…**.

When the selected workspace has no server yet, lip first offers that folder:

![The dialog "New server" with two rows, "This folder" and "Another folder…". "This folder" is selected, and the line below shows D:\servers\survival.](/screenshots/en/new-server-11-this-folder.svg)

1. **This folder** installs the server in the selected workspace.
2. Its path.

**Another folder…** opens the folder browser. When the selected workspace already has a server, the browser opens at once.

![The dialog "New server" with the row "Another folder…" marked.](/screenshots/en/new-server-01-where.svg)

In the browser, go to the place where the server should be.

![The folder browser, titled "New server", shows D:\servers with the folders backups and creative, and the buttons "Select this folder", "New folder…" and "Cancel".](/screenshots/en/new-server-02-folder.svg)

1. The folder shown. **Select this folder** installs the server right here.
2. **New folder…** asks for a name, and installs the server in a folder of that name inside the one shown.

![The dialog "New folder" with the field Name, which holds "survival".](/screenshots/en/new-server-03-folder-name.svg)

You can also type a path into the bar at the top, one that does not exist yet included. lip creates the folder only when you have made all choices; until then nothing is created.

lip refuses a folder whose server is running (**Stop the server first.**) and a path that names a file (**That is a file**).

### Step 2: the game version

lip reads the versions of the game that can be downloaded, which takes a moment, and lists them: the releases, newest first, then the previews, marked **preview**.

![The dialog "Game version" lists 1.26.60 down to 1.21.130. Most rows show "LeviLamina ✓"; the first is marked "newest".](/screenshots/en/new-server-04-game-version.svg)

The column after the version says whether mods can work on it:

![The same list with the row 1.26.30 selected, which shows "No LeviLamina". The line below says "Mods need LeviLamina, which this version lacks."](/screenshots/en/new-server-12-no-levilamina.svg)

1. **LeviLamina ✓**: a LeviLamina supports this version. Choose such a version for a server with mods.
2. **No LeviLamina**: no LeviLamina was made for this version. Such a version can only be a plain server.
3. The same in words, for the selected row.

::: info Why the column matters
Mods do not run on the game itself. They need **LeviLamina**, the loader, and each LeviLamina is made for one game version. The newest game is not always supported yet.
:::

lip reads this column from the mod catalogue of levimc.org. When the catalogue does not answer within a few seconds, the list is shown without the column; the loader step still tells you what fits.

Choose a version with `↑` and `↓`, and press `Enter`.

### Step 3: the build (Linux on x64 only)

The game comes in two **builds**: the Windows server and the Linux server. LeviLamina and its mods need the Windows server.

lip asks for the build only where both can run: on Linux on an x64 processor, and for a version that has both. Everywhere else the build is `windows`, and you do not see this step. On Linux, a version that has only one of the two says so in the list of versions:

![The list of game versions on Linux. The row 1.26.30 shows "windows only" after "No LeviLamina".](/screenshots/en/new-server-16-linux-games.svg)

1. A version that has no Linux server: for it, lip does not ask for the build.

![The dialog "Build" with the rows windows and linux. linux is selected, and the line below says "No loader; for Linux on x64 only."](/screenshots/en/new-server-17-build.svg)

1. **windows**: the Windows server, which lip runs through Wine. It is the one that supports LeviLamina and its mods.
2. **linux**: the Linux server. It runs without Wine, has no loader, and cannot run mods. The wizard skips the loader step.

[Linux and macOS](../guides/linux_macos.md) says more about Wine and the Linux server.

### Step 4: the loader

lip looks for the LeviLamina versions that fit the game version you chose, which may take a moment, and lists them, newest first, followed by **No loader**.

![The dialog "Loader" lists LeviLamina 26.60.3, marked "newest", down to 26.60.0, then "No loader". The line below says "Lets the server run mods."](/screenshots/en/new-server-05-loader.svg)

For a server with mods, press `Enter` on the first row, the newest LeviLamina.

**No loader** installs the game alone: a plain server that cannot run mods.

![The same dialog with "No loader" selected. The line below says "A plain server, without mods."](/screenshots/en/new-server-14-no-loader.svg)

When no LeviLamina fits the game version, the step says so. It is no dead end:

![The dialog "Loader" starts with "No LeviLamina version supports this game version." and has two rows, "No loader" and "Back".](/screenshots/en/new-server-13-no-loader-fits.svg)

1. **No loader** goes on with a plain server.
2. **Back** returns to the game versions, where you can choose one with **LeviLamina ✓**. `Esc` does the same.

### Step 5: the plan

Now lip creates the folder, when it does not exist, adds it to the list and selects it. Then it shows what it would install, without installing anything yet.

![The dialog "Set up server" lists three lines that start with "Dry run:" and has the buttons Apply and Cancel.](/screenshots/en/new-server-06-plan.svg)

The lines are what the command says, in English: here the game, LeviLamina, and that lip will ask you to accept the license.

- **Apply** installs.
- **Cancel** installs nothing. The folder stays, empty, and stays the selected workspace; run the wizard again in it, or [remove it from the list](#remove-a-workspace-from-the-list).

### Step 6: the license

The game is Microsoft's. Before lip downloads it, you must accept the Minecraft End User License Agreement and the Microsoft Privacy Policy. Read them at the two addresses shown.

![The dialog "Minecraft EULA" names the two documents, gives their addresses, and has the buttons Accept and Decline.](/screenshots/en/new-server-07-eula.svg)

**Accept** goes on. **Decline** closes the dialog, and nothing is installed:

![The same dialog with the button Decline marked.](/screenshots/en/new-server-15-eula-decline.svg)

### Step 7: download and result

lip downloads the game and LeviLamina and installs them. `Esc` cancels; what was done so far is undone.

![The dialog "Set up server" shows two downloads, one at 100% and one at 62%.](/screenshots/en/new-server-08-progress.svg)

When it is done, the dialog lists what was installed and ends with **✓ Done.** Press `Enter`.

![The dialog "Set up server" lists the two packages that were installed and ends with "Done." and the button OK.](/screenshots/en/new-server-09-result.svg)

The new server is the selected workspace, and has the focus in the list.

![The Workspaces list with the new folder D:\servers\survival, which has the dot.](/screenshots/en/new-server-10-workspace.svg)

Go to the [Server](server.md) page to start it, or to [Get mods](get_mods.md) first.

::: details The same with a command
The wizard carries out [`lip setup`](../reference/commands/setup.md) in the new folder with your choices, for example:

```shell
lip setup --game 1.26.60 --loader levilamina@26.60.3
```

With **No loader** it is `--no-loader`, and with the Linux server `--build linux`.
:::

## Change the game or the loader version

To move a server to another game version or another LeviLamina, set it up again. The wizard is the same, without the folder, and each step starts on what the server has now.

1. Move to the workspace, press `Enter`, and choose **Change game or loader version…**.

2. Choose the game version. The one that the server has is marked **installed**, and selected.

   ![The dialog "Game version" with 1.26.60 selected, which is marked "newest" and "installed".](/screenshots/en/workspaces-14-change-game.svg)

3. Choose the loader. The dialog starts on the LeviLamina that the server has, or on **No loader** when it has none.

   ![The dialog "Loader" with LeviLamina 26.60.2 selected, one row below the newest.](/screenshots/en/workspaces-15-change-loader.svg)

4. Read the plan, and choose **Apply**.

   ![The dialog "Set up server" says that lip would uninstall LeviLamina 26.60.3 and install LeviLamina 26.60.2, with the buttons Apply and Cancel.](/screenshots/en/workspaces-16-change-plan.svg)

   1. What lip would remove and install.
   2. Nothing changes before you choose **Apply**.

When the workspace is not the selected one, lip selects it once you have made the choices, before it shows the plan.

::: warning Mods that do not fit the new version
A mod is made for certain LeviLamina versions. When an installed mod has no version for the game or the LeviLamina you chose, the plan names it and says that the setup would uninstall it. Choosing **Apply** then uninstalls those mods. Choose **Cancel** to keep everything as it is.
:::

When you choose the versions that the server has already, there is nothing to do, and the plan has only **Close**:

![The dialog "Set up server" says "Nothing would change." and has one button, Close.](/screenshots/en/plan-02-no-changes.svg)

A change of the game keeps `server.properties`, `allowlist.json`, `permissions.json` and your worlds. It deletes the other files of the old game, and with them the packs that you copied into `behavior_packs/` or `resource_packs/`; [What survives a game change](../reference/files.md#what-survives-a-game-change) has the whole list. Make a copy of the folder first: an older game may not open a world that a newer game saved.

### A server with the Linux build on another system

A workspace that was set up with the Linux server runs only on Linux on x64. On any other system, the wizard cannot offer that build. It says so before you choose anything, and goes on with the Windows server:

![The dialog "Game version" starts with two lines: "This workspace has the linux build, which does not run on this system. The setup replaces it with the windows build."](/screenshots/en/workspaces-17-linux-build.svg)

1. The notice, shown before you choose anything.

The plan then names the build in its title, **Switch to windows**, and lists the Linux server among what is uninstalled:

![The dialog "Switch to windows" lists that lip would uninstall the Linux game and install the Windows game and LeviLamina.](/screenshots/en/workspaces-18-switch-to-windows.svg)

## While a server runs

A running server does not keep you from using the list:

- The list marks the workspace **server running**, and the details say **Its server is running.**
- **Switch** and **Remove from list** work as usual. Neither stops the server.
- **Change game or loader version…** is dimmed, and the menu says **Stop the server first.** lip does not change the game under a running server.

![The menu of the folder creative, whose server runs. The row "Change game or loader version…" is dimmed, and below a rule the menu says "Stop the server first."](/screenshots/en/workspaces-05-menu-running.svg)

A new server cannot go into a folder whose server runs either. To stop a server, switch to its workspace and use the [Server](server.md) page. [The interface reference](../reference/interface.md#while-the-server-runs) lists what every page does meanwhile.

## Several interfaces at once

You can open `lip` in several terminals. They share one list, the file `workspaces.json` (see [Files](../reference/files.md)):

- Each interface has its own selected workspace, so two terminals can show two servers.
- A workspace that one interface adds or removes shows up in the others within a few seconds, while their Workspaces page is open.
- Changes that several interfaces make at the same time are all kept.
