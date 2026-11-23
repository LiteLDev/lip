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

`.github/workflows/ci.yml` runs the same, on every system `lip` is released for, and also runs a real server
with the `lip` it built. Its job `ci-ok` succeeds only when every other job did, and is the check that pull requests
and the merge queue require.

Pull requests land through the merge queue, whose merge method is squash, so each one becomes a single commit on
`main`. `gh pr merge` adds a pull request to the queue instead of merging it at once; the queue, not a flag, picks
the merge method. The queue runs `ci.yml` on the exact commit that will land, so a push to `main` does not run it
again; a push to a `release/**` branch still does.

## The documentation

The documentation at [levimc.org/lip](https://levimc.org/lip/) is written in
[LiteLDev/liteldev.github.io](https://github.com/LiteLDev/liteldev.github.io) (`docs/lip/`), with the documentation
of the other LeviMC projects; that repository says how to build it and how to write a page. The pictures of the
interface are still made here, by the tests, and committed there (`docs/lip/public/screenshots/`): see
[tests/Lip.Cli.Tests/Tui/Screenshots/README.md](tests/Lip.Cli.Tests/Tui/Screenshots/README.md).

The tutorials that use `lip` (your first server, a server from the command line, publishing a package) are at
[levimc.org/tutorials](https://levimc.org/tutorials/), written in
[LiteLDev/liteldev.github.io](https://github.com/LiteLDev/liteldev.github.io).
