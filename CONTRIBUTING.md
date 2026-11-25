# Contributing to lip

Issues and pull requests are welcome. [Open an issue](https://github.com/LiteLDev/lip/issues/new/choose) for a bug
or an idea before a large change, so that we can agree on it first. `lip` follows the
[Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/) Code of Conduct.

## The protocol

`protocol/` describes what `lip` does as seen from outside: its commands, its files, what it reads and writes on the
disk and on the network. It is the single source of truth. A change of behavior changes `protocol/` first, and the
code and the documentation follow it; where they disagree with it, they are wrong.

## Building and testing

`lip` is a .NET 10 program (`src/`), with its tests in `tests/`. From the root of the repository:

```
dotnet build                                    # build
dotnet test                                     # every test
dotnet format --verify-no-changes               # the code style, as CI checks it
dotnet build -warnaserror -p:EnforceCodeStyleInBuild=true
dotnet publish src/Lip.Cli -o bin -r win-x64 --self-contained -p:DebugType=embedded   # a release build
```

`.github/workflows/ci.yml` runs the same, on every system `lip` is released for, and also tests the `lip` it
built end to end: it installs it as a user does (with `install.sh` on Linux and macOS, in new `ubuntu:latest` and
`debian:stable-slim` containers on Linux, and with the Windows Installer package on Windows), as a user without
administrator rights where it can, and runs a real server with it. It runs only the jobs a change needs: a change
to `ci.yml` runs them all; a change to `src` or what every project builds with runs all but the installation of
the latest release with `install.sh`; a change to `tests` runs only the lint and the tests; a change to
`install.sh` runs the lint (with ShellCheck), the end-to-end test and an installation of the latest release with
it on Linux and macOS; a change to `packaging` or `.github/scripts` runs the end-to-end test (and, for
`.github/scripts`, the lint); and a change to nothing of these, like one to the docs, runs none. Its job `ci-ok` succeeds
only when every job the change needs ran and succeeded, and is the check that pull requests and the merge queue require.

Pull requests land through the merge queue, whose merge method is squash, so each one becomes a single commit on
`main`. `gh pr merge` adds a pull request to the queue instead of merging it at once; the queue, not a flag, picks
the merge method. The queue runs `ci.yml` on the exact commit that will land, so a push to `main` does not run it
again; a push to a `release/**` branch still does.

## The documentation

The documentation at [levimc.org/lip](https://levimc.org/lip/) is written in
[LiteLDev/liteldev.github.io](https://github.com/LiteLDev/liteldev.github.io) (`content/lip/`), with the documentation
of the other LeviMC projects; that repository says how to build it and how to write a page. The pictures of the
interface are still made here, by the tests, and committed there (`content/lip/public/screenshots/`): see
[tests/Lip.Cli.Tests/Tui/Screenshots/README.md](tests/Lip.Cli.Tests/Tui/Screenshots/README.md).

`install.sh`, the script that installs `lip` on Linux and macOS ([install.sh(1)](protocol/install.sh.md)), is
written here and served from the website as `https://levimc.org/lip/install.sh`. When you change it, copy it to
`content/lip/public/install.sh` there too, so that the website serves the same script.

The tutorials that use `lip` (your first server, a server from the command line, publishing a package) are at
[levimc.org/tutorials](https://levimc.org/tutorials/), written in
[LiteLDev/liteldev.github.io](https://github.com/LiteLDev/liteldev.github.io).
