# Server

The Server page is the console of your server. After this page you can start and stop the server, read what it writes, send it commands, have it started again after a crash, and you know what happens to it when you quit lip.

If the keys or the parts of the screen are new to you, read [A tour of the interface](./tour.md) first.

## The parts of the page

![The Server page while the server runs. Numbers mark the state line, the output and the input line.](/screenshots/en/server-02-running.svg)

1. **The state line** says whether the server runs. Below it are the **Start** or **Stop** button and the option **Auto restart**.
2. **The output** is what the server writes.
3. **The input line** is where you type commands for the server. It is there only while the server runs.

`Tab` goes from one part to the next: the button and the option, the output, the input line.

## Every state of the server

The state line always shows one of the following.

### No server here yet

A folder without the game has nothing to run. The page offers one button.

![The Server page of an empty folder. It says "No server here yet." and has a button "New server…".](/screenshots/en/server-03-no-server.svg)

Press `Enter` to create a server. This starts the new server wizard of the [Workspaces](./workspaces.md) page.

### Stopped

`○ Stopped` means that the server does not run and has not run since lip started. The output is empty.

![The Server page of a stopped server. The Start button is highlighted, and the output says "No output yet."](/screenshots/en/server-01-stopped.svg)

Press `Enter` on **Start** to start it.

### Running

When you press Start, the state is `● Starting` until the program of the server runs. That is usually a moment. It takes longer when lip first has to prepare the system for the server, such as the Visual C++ runtime on Windows or Wine on Linux and macOS. When that fails, lip shows the failure, and the state is `○ Stopped` again.

Then the state is `● Running`. The server counts as running from the moment its program runs, while it still loads its world and its mods.

![The Server page right after Start. The state says "Running · 00:00:01", the button is now Stop, the output has one gray line, and the input line has appeared.](/screenshots/en/server-05-just-started.svg)

1. `● Running`, followed by how long the server has been running, as hours, minutes and seconds.
2. The button is now **Stop**.
3. Gray lines between dashes are written by lip itself, not by the server.
4. The input line appears, and the focus moves into it.

The top line of lip shows `● Running` too, and the Server entry of the sidebar gets a `●`. You can go to other pages; the server keeps running.

The server is ready for players when it writes `Server started.` in its output.

::: info
If the server cannot be started at all, for example because a file of the game is missing, lip shows a failure dialog and the state stays "Stopped". See [When something fails](./tour.md#when-something-fails).
:::

### Stopping

After you ask the server to stop, the state is `● Stopping` until the server has ended. A server usually needs a few seconds to save its world.

![The Server page after Ctrl+C. The state says "Stopping", and the output has the gray line "asking the server to stop".](/screenshots/en/server-08-stopping.svg)

1. lip has asked the server to stop, and waits for it.
2. lip sent the server the command `stop`.

The Stop button is gray meanwhile: asking twice does nothing more.

### Exited with a code

When the server has ended, the state is `○ Exited with code` and a number. The code `0` means that the server ended normally. Any other code means that something went wrong.

![The Server page of a server that has ended. The state says "Exited with code 0", the button is Start again, and the output is still there.](/screenshots/en/server-09-exited.svg)

1. The server is not running.
2. **Start** starts it again.
3. The output stays, so you can read why the server ended.

### Restarting

When the server ends with a code other than `0`, you did not ask it to stop, and **Auto restart** is on, lip starts it again after 5 seconds. Meanwhile the state is `● Restarting`, followed by the code that the server ended with.

![The Server page after a crash. The state says "Restarting · Exited with code -1073741819", Auto restart is checked, and a yellow line of lip says that it restarts the server in 5 seconds.](/screenshots/en/server-11-restarting.svg)

1. lip waits, then starts the server again.
2. How the server ended.
3. Auto restart is on.

Press `Ctrl+C` or **Stop** while lip waits, and the server is not started again.

### Running outside this interface

A server can also be started with the plain command `lip run` in another terminal. lip then knows that the server runs, and no more.

![The Server page says "Running outside this interface". Below the button, a line says "Another `lip run` started it. Stop it there."](/screenshots/en/server-10-elsewhere.svg)

1. The server runs, but this interface did not start it.
2. **Start** does nothing here, and there is no Stop.
3. Stop the server where it was started.

There is no output and no input line, because they belong to the terminal that started the server. Everything else that depends on a running server still holds: mods cannot be changed until it has stopped.

## Starting and stopping

To start the server, press `Enter` on **Start**. Step 3 of the [Home](./home.md) page does the same.

To stop the server, either:

- press `Ctrl+C` anywhere on the Server page, or
- press `Tab` until **Stop** is highlighted, then `Enter`.

Both send the server the command `stop`, so that it saves the world and ends by itself. lip never kills the server.

::: warning
On every other page, `Ctrl+C` asks whether to quit lip. Only on the Server page, and only while the server runs, does it stop the server.
:::

## Reading the output

While the focus is in the input line, the output follows the server: new lines appear at the bottom.

To read back, press `Esc` in the input line. The focus moves to the output, and its title becomes bright.

![The output has the focus and is scrolled back. Marks at its right edge show that there is more above and below.](/screenshots/en/server-06-output-scrolled.svg)

1. The output has the focus.
2. `▲` and `▼` mean that there are more lines above and below.
3. The keys that scroll.

| Key | Does |
| --- | --- |
| `↑` `↓` | Scroll one line. |
| `PgUp` `PgDn` | Scroll one screenful. |
| `Home` | Goes to the first line. |
| `End` | Goes back to the last line. |

While you read back, the lines stay where they are, even when the server writes more.

To go back to the input line, press `End` and then `↓`, or press `Tab`.

## Sending commands to the server

Type a command of the server in the input line and press `Enter`. The gray `list` in the empty line is an example, not something that is sent.

![The input line holds the text "say Welcome to Survival".](/screenshots/en/server-02-running.svg)

The answer of the server appears in the output.

lip remembers the lines you sent. Press `↑` in the input line to bring back the one before, and `↓` to go forward again. What you were typing is kept and comes back when you press `↓` past the last line.

![The input line shows "list", brought back with the up arrow. The bottom line names the keys "↑↓ History".](/screenshots/en/server-07-history.svg)

These are commands of the Minecraft server, such as `list` or `say`. They are not commands of lip.

### Leaving the input line

While the input line has the focus, the letters you type go to the server, so `Esc` is the way out:

1. `Esc` goes from the input line to the output.
2. `Esc` again goes to the sidebar.

`Tab` also leaves the input line, for the sidebar.

## Auto restart

**Auto restart** says whether lip starts the server again after a crash. Press `→` to move from the button to the option. A sentence at its right says what it does. Press `Space` to switch it.

![The option "Auto restart" is checked and highlighted. At its right is the sentence "Restarts after a crash."](/screenshots/en/server-04-auto-restart.svg)

With the option on:

- A server that ends with a code other than `0` is started again after 5 seconds.
- A server that you stopped, or that ended with the code `0`, is not started again.
- After 5 restarts within 10 minutes, lip gives up and says so in the output. Read the output above that line to find out why the server keeps failing.

The option belongs to the workspace, so each server has its own. The [Settings](./settings.md) page shows the same option.

## Quitting lip while the server runs

When you quit lip while a server runs, the question says what happens to it.

![The question "Quit lip?" with a second line: "Running servers keep running."](/screenshots/en/server-12-quit-keeps-running.svg)

Normally the server **keeps running**. lip runs servers in a background program of its own, the daemon, which stays when the interface closes. You can close the terminal. To come back, open a terminal in the same folder and run `lip`: the Server page shows the server, with its recent output, and you can type commands and stop it as before.

In one rare case, when lip cannot use its daemon and runs everything itself, the question says something else:

![The question "Quit lip?" with a second line: "Running servers stop first."](/screenshots/en/server-13-quit-stops.svg)

If you choose **Quit** then, lip asks each running server to stop, shows the Server page while they end, and quits once they have all ended.

## Managed servers

A server that is started on this page is a managed server: the daemon of lip runs it, and no terminal owns it. That is why it keeps running when you quit, why several terminals can show the same server at once, and why each workspace can have its own server running at the same time. The command `lip run --managed` starts and shows the same servers. [Running a server from the commands](../guides/server_from_commands.md) explains them, and how to stop one without the interface.
