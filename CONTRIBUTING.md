# Contributing

## Branches

| Branch | What it holds | How changes land |
| --- | --- | --- |
| `main` | The next feature release. Every commit on it can be released. | Squash-merged pull requests, through the merge queue |
| `release/X.Y` | The X.Y line: bugfix releases X.Y.1, X.Y.2, … | Backports of fixes merged into `main`, or pull requests for fixes that only apply to this line |
| `next/<topic>` | Unfinished work that users would see, collected until it is complete | Pull requests of any size, in any state that builds |
| anything else | One pull request | — |

`main` must stay releasable, so a change that users can see (anything `specs/` describes: the CLI, lipd, file formats, filesystem and network behavior) merges into `main` only when it is complete. Split large work into as many pull requests as you like, but aim them at `next/<topic>`; when the topic is done, merge `next/<topic>` into `main` with one pull request whose title describes the whole change (for example `feat!: replace the manifest format with format bf45b1`), then delete the branch. Changes users cannot see (refactoring, tests, CI, docs) go straight to `main`.

## Pull requests

- The title follows [Conventional Commits](https://www.conventionalcommits.org/) (`feat: ...`, `fix(solver): ...`, `feat!: ...` for breaking changes). The squash commit and the release notes take the title, and it tells whether the next version is a feature or a bugfix release.
- `main` takes pull requests through the merge queue: the build and tests run again on top of everything queued before, so pull requests that pass alone cannot break `main` together.
- A fix that the current release line also needs gets the `backport release/X.Y` label (before or after merging). Once it is merged, a bot cherry-picks it onto `release/X.Y` and opens a pull request there; conflicts are left as a comment for you to backport by hand.

## Releases

Run the [create-release workflow](https://github.com/LiteLDev/lip/actions/workflows/create-release.yml) from `main` with a version and a branch:

| Release | Version | Branch | Result |
| --- | --- | --- | --- |
| Feature | `X.Y.0` | `main` | Releases `main` and starts `release/X.Y` at the release commit |
| Feature while `main` is not ready | `X.Y.0` | the newest `release/A.B` | Starts `release/X.Y` from `release/A.B` (with the features backported to it) and releases it |
| Bugfix | `X.Y.Z` | `release/X.Y` | Releases the line |
| Prerelease | `X.Y.0-beta.N` | `main` | Releases `main` as a prerelease |

The workflow refuses a version that already exists, a bugfix from another branch than its line, and a feature release that is not newer than every release. It tags the branch tip, drafts a release whose notes list the pull requests since the previous release on that branch, then builds and publishes the tag with that tag's own `release.yml`: the newest stable version becomes Latest on GitHub, `latest` on npm and the winget version; a bugfix of an older line goes to npm under `release-X.Y`; a prerelease goes to npm under its identifier (`beta`).
