---
layout: home

hero:
  name: "lip"
  text: "Run a LeviLamina server and manage its mods"
  tagline: Set up a Minecraft Bedrock server, add mods, and run it, from one screen in your terminal.
  image:
    src: /logo.webp
    alt: lip
  actions:
    - theme: brand
      text: Your first server
      link: /intro/first_server
    - theme: alt
      text: Install lip
      link: /intro/installation
    - theme: alt
      text: Reference
      link: /reference/cli

features:
  - title: A server in three steps
    details: Pick a folder, a game version and a loader. lip downloads the Bedrock Dedicated Server and LeviLamina, and starts the server for you.
    link: /intro/first_server
  - title: Mods from levimc.org
    details: Browse the mods, see which versions fit your server, and install, update or remove them together with what they depend on.
    link: /interface/get_mods
  - title: One interface, no commands to learn
    details: Type lip and use the arrow keys and Enter. Every change is shown as a plan before lip applies it.
    link: /interface/tour
  - title: Commands when you want them
    details: Everything the interface does is also a command, for scripts and for servers without a screen. On Windows, Linux and macOS.
    link: /guides/server_from_commands
---

## What it looks like

Type `lip` in a terminal, and this is where you work. The picture shows the Server page while the server runs: what the server writes, and a line to type its commands in.

![The Server page of lip while the server runs: the output of the server says "Server started.", and the input line below it waits for a command.](/screenshots/en/first-server-21-running.svg)

## Where to start

- New to `lip`? Read [What is lip?](intro/what_is_lip.md), then [install it](intro/installation.md).
- Want a server now? Follow [Your first server](intro/first_server.md): it takes you from an empty folder to a running server with one mod, one picture per step.
- Prefer commands? Go to [A server from the commands](guides/server_from_commands.md).
