# Contributing

Open pull requests against `main`. Title them in [Conventional Commits](https://www.conventionalcommits.org/) (`feat: ...`, `fix(solver): ...`, `feat!: ...` for breaking changes): the squash commit and the release notes take the title.

## Releases

Push a tag from the commit to release:

```sh
git tag v1.2.3
git push origin v1.2.3
```

The release workflow builds the tag and publishes it to GitHub, npm and winget. A tag like `v1.2.3-beta.1` is a prerelease.

When a release needs a fix but `main` has moved on with changes not ready to ship, branch from the release's tag, land the fix there and tag it:

```sh
git switch -c release/1.2.3 v1.2.3
# commit or cherry-pick the fix
git tag v1.2.4
git push origin release/1.2.3 v1.2.4
```
