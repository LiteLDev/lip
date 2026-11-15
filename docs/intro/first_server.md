# Your first server

After this page you have a Minecraft Bedrock server with LeviLamina and one mod, running on your computer. You get there from an empty folder, in the interface of `lip`, without typing a single command but `lip` itself.

It takes about ten minutes, most of it waiting for downloads.

## What you need

- `lip`, installed: see [Installation](installation.md). `lip -v` must print a version.
- A terminal window. The pictures below are 100 columns wide and 28 rows high; anything from 80 columns works well.
- An internet connection. `lip` downloads the game from Minecraft, and LeviLamina and the mods from GitHub.

::: tip How to read the pictures
Every picture shows what `lip` really displays at that step. The orange marks are added for this page: boxes and numbered circles point at what the text talks about, and the keys under a picture are the ones to press next.
:::

## Start lip

1. Make a folder to keep your servers in, go into it, and start `lip`. The folder can be anywhere; this page uses `D:\servers`.

   ::: code-group

   ```powershell [Windows]
   mkdir D:\servers
   cd D:\servers
   lip
   ```

   ```shell [Linux and macOS]
   mkdir -p ~/servers
   cd ~/servers
   lip
   ```

   :::

   `lip` fills the terminal with its interface and shows the **Home** page.

   ![The Home page of lip in the empty folder D:\servers. The sidebar at the left lists nine pages; the page says that no game is installed and lists three next steps, the first of which, "Create a server", is selected.](/screenshots/en/first-server-01-home.svg)

   - **(1)** The sidebar lists the nine pages of `lip`. The one that is shown is marked.
   - **(2)** The overview says which folder `lip` works in and what it holds. There is nothing yet: no game, no loader, no mods.
   - **(3)** **Next steps** are the three things this page does with you: **Create a server**, **Install mods**, **Start the server**.
   - **(4)** The bottom line always lists the keys that work right now.

   You only need a few keys: `↑` and `↓` move, `Enter` opens or confirms, and `Esc` goes back. There are no letter shortcuts to remember.

::: details If lip says "GitHub unreachable"
When `lip` starts, it checks whether it can reach GitHub, because LeviLamina and the mods come from there. If it cannot, it asks you what to do:

![The dialog "GitHub unreachable" above the Home page. It says why GitHub was not reached, asks "How should lip reach GitHub?", and offers three answers: Use a proxy, Connect directly, Later.](/screenshots/en/first-server-25-github.svg)

- **(1)** **Use a proxy**: choose this if you have the address of a GitHub proxy. `lip` asks for it, tests it, and uses it from then on.
- **(2)** **Connect directly**: choose this if your network reaches GitHub after all, for example because the connection was down for a moment. `lip` does not ask again.
- **(3)** **Later**: `lip` changes nothing and asks again the next time it starts. `Esc` does the same.

Without a way to GitHub, `lip` cannot download LeviLamina or the mods. You can change your answer at any time on the **Settings** page; see [Settings](../interface/settings.md).
:::

## Create the server

2. On the **Home** page, **Create a server** is selected. Press `Enter`.

   `lip` asks where the server goes. **This folder** puts it right into `D:\servers`. Each server needs a folder of its own, so choose **Another folder…** instead: press `↓`, then `Enter`.

   ![The dialog "New server" with two choices, "This folder" and "Another folder…". "Another folder…" is marked.](/screenshots/en/first-server-02-where.svg)

   ::: tip
   If you started `lip` inside an empty folder that is meant for this one server, choose **This folder** and go on at step 5.
   :::

3. The folder browser opens. It shows **(1)** the folder that is open, `D:\servers`, and **(2)** what is in it: nothing but `..`, which leads one folder up.

   Make a folder for the server here: press `Tab` to go to the buttons, `→` to go to **New folder…** **(3)**, and `Enter`.

   ![The folder browser, open at D:\servers. The list holds only "..". Below it are the buttons "Select this folder", "New folder…" and "Cancel".](/screenshots/en/first-server-03-folder.svg)

4. Type a name for the folder, such as `survival`, and press `Enter`. `lip` creates `D:\servers\survival` and takes it as the folder of the server.

   ![The small dialog "New folder" above the folder browser, with "survival" typed into its field "Name".](/screenshots/en/first-server-04-folder-name.svg)

5. Choose the **Game version**. `lip` lists the versions of the Bedrock Dedicated Server, the newest first.

   - **(1)** **LeviLamina ✓** means that LeviLamina supports this version, so mods work on it.
   - **(2)** **No LeviLamina** means that only a plain server without mods is possible with this version.

   Select the newest version that says **LeviLamina ✓**, which is usually the first row, and press `Enter`.

   ![The dialog "Game version" lists eight versions. Most are marked "LeviLamina ✓"; three say "No LeviLamina". The first, 1.26.60, is selected and marked "newest".](/screenshots/en/first-server-05-game-version.svg)

   ::: info On Linux
   On Linux on x64, `lip` next asks for the **Build**. Choose **windows**: it is the one that supports LeviLamina and its mods, and `lip` runs it through Wine. See [Linux and macOS](../guides/linux_macos.md).
   :::

6. Choose the **Loader**. The loader is what lets the server run mods. Keep the newest LeviLamina and press `Enter`.

   ![The dialog "Loader" lists four versions of LeviLamina, the newest selected, and "No loader" as the last choice.](/screenshots/en/first-server-06-loader.svg)

7. `lip` shows its plan before it changes anything. Read it:

   - **(1)** The lines that start with `Dry run:` say what `lip` would install: the game (`github.com/LiteLDev/bds`) and LeviLamina. They are in English in every language, because they are what the command `lip setup` says.
   - **(2)** **Apply** carries the plan out. **Cancel** and `Esc` leave everything as it is.

   Press `Enter` to apply.

   ![The dialog "Set up server" shows three lines that start with "Dry run:": lip would install the game 1.26.60 and LeviLamina 26.60.3, and would ask you to accept the Minecraft EULA. Below them are the buttons "Apply" and "Cancel".](/screenshots/en/first-server-07-plan.svg)

   Every change that `lip` makes to a server goes this way: first the plan, then your **Apply**.

8. The Minecraft server is under the Minecraft End User License Agreement and the Microsoft Privacy Policy. Read them at the two addresses shown. To accept them and go on, press `Enter` on **Accept**.

   ![The dialog "Minecraft EULA" names the Minecraft End User License Agreement and the Microsoft Privacy Policy with their addresses. The button "Accept" is marked, next to "Decline".](/screenshots/en/first-server-08-eula.svg)

   `lip` asks this each time it installs a game. If you decline, nothing is installed.

9. `lip` downloads and installs. Wait until both lines are done.

   ![The dialog "Set up server" while lip works: the download of the game is at 100%, the download of LeviLamina at 62%.](/screenshots/en/first-server-09-progress.svg)

   The first time, `lip` may also install what the server needs of your system:

   - On Windows, the Microsoft Visual C++ Redistributable. Windows asks you to allow its installer to make changes.
   - On Linux and macOS, Wine. The download is large, and `lip` keeps it for all your servers.

   `Esc` cancels. `lip` then undoes what it had changed so far.

10. When you see **✓ Done.**, the server is installed. Press `Enter` to close the dialog.

    ![The dialog "Set up server" after the work: two lines say that the game and LeviLamina were installed, followed by "✓ Done." and the button "OK".](/screenshots/en/first-server-10-result.svg)

11. You are now on the **Workspaces** page. A workspace is a folder that holds a server.

    - **(1)** The new server is in the list. The **●** marks the workspace that `lip` works in now.
    - **(2)** The top line always names that workspace, on every page.

    Go back to the **Home** page: press `Esc` to move to the sidebar, `↑` six times to reach **Home**, and `Enter`.

    ![The Workspaces page lists D:\servers and D:\servers\survival. The second is selected and marked with a dot; the top line of the window names it too.](/screenshots/en/first-server-11-workspace.svg)

## Install a mod

12. The **Home** page has changed: **(1)** the overview names the game and the loader, and **(2)** the first step is ticked and **Install mods** is selected. Press `Enter`.

    ![The Home page of D:\servers\survival: the game is 1.26.60 and the loader LeviLamina 26.60.3. Step 1 has a tick, and step 2, "Install mods", is selected.](/screenshots/en/first-server-12-home-server.svg)

13. This is the **Get mods** page. It shows the mods of [levimc.org](https://levimc.org/mods/).

    - **(1)** At the top you search, sort and filter.
    - **(2)** The list shows each mod with its author, its downloads, its stars and the date of its last update.
    - **(3)** Below the list are the details of the mod that is selected.

    This page installs LeviOptimize, a mod that makes the server run faster. Search for it: press `↑` three times to go from the list up to **Search**.

    ![The Get mods page: the fields Search, Sort and Filters, a list of eight mods with downloads, stars and dates, and below it the details of the selected mod, LegacyScriptEngine.](/screenshots/en/first-server-13-get-mods.svg)

14. Type `optim`. The list shrinks as you type, until only LeviOptimize is left. Press `Enter` to go to the results.

    ![The Get mods page with "optim" typed into Search. The list holds one mod, LeviOptimize, and the details below are those of LeviOptimize.](/screenshots/en/first-server-14-search.svg)

15. LeviOptimize is selected. Press `Enter` to open what you can do with it, then `Enter` again on **Install…**.

    ![The menu of LeviOptimize with three rows: "Install…", "README" and "Open in browser". "Install…" is marked.](/screenshots/en/first-server-15-menu.svg)

    **README** shows the description that the author wrote, which is worth reading before you install a mod you do not know.

16. Choose the version.

    - **(1)** `lip` lists only the versions that fit your game and your LeviLamina, the newest first.
    - **(2)** The last line shows what `lip` is going to install.

    Keep the newest version and press `Enter`.

    ![The version dialog of LeviOptimize lists the versions 1.8.1 and 1.8.0 with their dates, and "Custom…". The line at the bottom reads github.com/LiteLDev/LeviOptimize@1.8.1.](/screenshots/en/first-server-16-versions.svg)

17. Again `lip` shows its plan first: it would install LeviOptimize 1.8.1, and create the folder `mods/` that mods live in. If a mod needs other mods, they are listed here too. Press `Enter` on **Apply**.

    ![The plan of the installation: "Dry run: would install LeviOptimize 1.8.1 (5 files)" and "Dry run: would create the directory mods/", with the buttons "Apply" and "Cancel". "Apply" is marked.](/screenshots/en/first-server-17-mod-plan.svg)

18. When you see **✓ Done.**, the mod is installed. Press `Enter`.

    ![The result of the installation: lip installed the package github.com/LiteLDev/LeviOptimize in version 1.8.1, followed by "✓ Done." and the button "OK".](/screenshots/en/first-server-18-mod-result.svg)

19. In the list, LeviOptimize now has a **✓**: it is installed.

    Go back to the **Home** page: press `Esc` to move to the sidebar, `↑` three times, and `Enter`.

    ![The Get mods page after the installation: the row of LeviOptimize starts with a tick.](/screenshots/en/first-server-19-mod-installed.svg)

## Start the server

20. Two steps are ticked **(1)**, and **Start the server** is selected **(2)**. Press `Enter`.

    ![The Home page: one mod is installed, steps 1 and 2 have ticks, and step 3, "Start the server", is selected.](/screenshots/en/first-server-20-home-mods.svg)

21. `lip` starts the server and shows the **Server** page.

    - **(1)** The first line says that the server runs, and for how long. The top line and the sidebar show a green dot too.
    - **(2)** **Output** is what the server writes. Wait for the line `Server started.`: from then on, players can join. The lines of `[LeviLamina]` show the mod being loaded.
    - **(3)** The line at the bottom is where you type commands for the server.

    ![The Server page while the server runs. The output shows the server starting, LeviLamina loading LeviOptimize, the ports 19132 and 19133, and "Server started.". Below it is the input line.](/screenshots/en/first-server-21-running.svg)

    To play, add a server in Minecraft with the address of this computer and the port `19132`, the port that the output names.

22. Give the server a command. Type `list`, a command of the game that names the players who are online, and press `Enter`.

    ![The Server page with "list" typed into the input line, which is marked.](/screenshots/en/first-server-22-command.svg)

23. The output shows **(1)** the command you sent and **(2)** what the server answered. Nobody has joined yet.

    The commands of the game and of your mods work here as in the console of the server, without the leading `/`.

    ![The output of the server ends with "> list" and the answer "There are 0/10 players online:".](/screenshots/en/first-server-23-answer.svg)

24. Stop the server: press `Ctrl+C`. `lip` sends the server its command `stop`, and waits. When the server has ended, the first line changes to **Exited with code 0**, which means that it ended normally.

    ![The Server page after the server stopped. The first line reads "Exited with code 0", the output ends with "Quit correctly", and the button "Start" is marked.](/screenshots/en/first-server-24-stopped.svg)

    The marked button, **Start**, starts the server again.

To leave `lip`, press `Esc` until you are in the sidebar, press `Esc` once more, and confirm. If a server still runs, the question says whether it keeps running. Start `lip` again in any folder: your server is on the **Workspaces** page.

## What you have now

- A folder, `D:\servers\survival`, that holds the game, LeviLamina and one mod. It is an ordinary folder: you can look into it, back it up and move it.
- A server that you start and stop on the **Server** page.

::: warning Stop the server before you change it
While the server runs, `lip` does not install, update or remove anything in its folder, and does not save `server.properties`, the allowlist or the permissions. Stop the server first, then make the change.
:::

## What to do next

- Change the name of the server, the game mode or the number of players: [Game config](../interface/game_config.md).
- Find more mods: [Get mods](../interface/get_mods.md). Update or remove the ones you have: [Mods](../interface/mods.md).
- Change the settings of a mod: [Mod config](../interface/mod_config.md).
- Let the server restart by itself after a crash, and see what else the Server page does: [Server](../interface/server.md).
- Add a second server, or change the version of the game: [Workspaces](../interface/workspaces.md).
- Learn the interface properly: [A tour of the interface](../interface/tour.md).
- Do all of this with commands instead: [A server from the commands](../guides/server_from_commands.md).
- Something went wrong? See [Troubleshooting](../guides/troubleshooting.md).
