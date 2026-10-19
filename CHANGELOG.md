# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Renamed the npm package from `@futrime/lip` to `@levimc/lip` and the winget package ID from `futrime.lip` to `levimc.lip`.

## [0.34.9] - 2026-10-08

### Changed

- Adjusted remove_files and preserve_files's behavior [#347]

## [0.34.8] - 2026-08-04

### Changed

- Bumped dependencies to latest versions

## [0.34.7] - 2026-04-29

### Fixed

- Fixed compatibility issue with variables of manifest v2

## [0.34.6] - 2026-04-14

### Changed

- Modified default goproxy to https://proxy.golang.org

## [0.34.5] - 2026-04-12

### Changed

- Updated lipr's URL format in GetPackageManifest method

## [0.34.4] - 2026-03-25

### Added

- Installer will install .NET runtime prerequisite
- Npm will bootstrap .NET runtime on install
- Added Chinese Simplified translation for Windows installer

### Changed

- Bumped Scriban to 7.0.3

### Fixed

- Fixed unable to clean read-only cache files
- Fixed compatibility issue in placement with tooth package made with older version of lip(0.32.0 e.g.)

## [0.34.4-beta.2] - 2026-03-25

### Added

- Installer will install .NET runtime prerequisite
- Npm will bootstrap .NET runtime on install

### Changed

- Bumped Scriban to 6.6.0

### Fixed

- Fixed unable to clean read-only cache files
- Fixed compatibility issue in placement with tooth package made with older version of lip(0.32.0 e.g.)

## [0.34.3] - 2026-03-24

### Added

- Add native npm distribution for `@futrime/lip`, bundling `lip` and `lipd` binaries for all supported platforms in one package.
- Add Windows Inno Setup installers for `win-x64` and `win-arm64` releases.

### Changed

- Switch release binaries back to framework-dependent single-file builds with `--no-self-contained` to keep npm package size manageable.
- Publish release assets directly from the matrix build workflow, including portable archives, npm artifacts, Windows installers, and winget updates.

## [0.34.2] - 2026-03-15

### Added

- Added Windows installer(.msi)

## [0.34.1] - 2026-03-10

### Fixed

- Support `install` and `uninstall` lifecycle script aliases in `tooth.json`, including package variants and migrated legacy manifests.

## [0.34.0] - 2026-03-06

### Added

- Get available versions from one available source among Go proxy, lipr, and GitHub

### Changed

- Publish builds with embedded debug info

### Fixed

- Update package explicitness correctly during install, uninstall, and update

## [0.33.4] - 2026-03-04

### Fixed

- Mitigate file corruption after failed downloads by using a temporary directory workflow

## [0.33.3] - 2026-03-03

### Fixed

- Fix script handling in package manifest
- Restrict package script execution to one script per stage
- Adapt to latest lipr changes
- Fix typo

## [0.33.2] - 2026-03-02

### Fixed

- Fix stdio competition between command runner, git runner and lip daemon

## [0.33.1] - 2026-02-27

### Changed

- Adapt to latest lipr format

### Fixed

- Fix variant label support
- Fix downloading via GitHub proxies
- Fix irregular error printing for aggregate exceptions
- Fix `$ref` usage in schema docs

## [0.33.0] - 2026-02-21

### Added

- Support latest lipr index format with package metadata and version info in dict form
- Support getting versions from lipr
- Support latest lipr schema
- Add CLI for lipd
- Add `--version` option to show version information
- Support shorthand option for `--dry-run` across install, uninstall, and update commands
- Add `--json` for `lip versions` command
- Support `--json` output for config and list commands
- Add `lip versions` and support package id in `lip view`
- Publish to winget when releasing

### Changed

- Update variant label format
- Rename schemas
- Rename source provider to source
- Log messages to stderr instead of stdout
- Use Microsoft.Extensions.DependencyInjection for type registration and resolution
- Better exception handling in CLI app
- Merge lip and lipd releases into one
- Update lipr schema to include stars and updated fields
- Documentation improvements

### Fixed

- Avoid duplicate ConsoleUserInteraction instance creation
- Return consistent error code in exception handling
- `lip` client `list()` method should return string

## [0.33.0-beta.2] - 2026-02-13

### Added

- Add lip daemon

## [0.33.0-beta.1] - 2026-02-11

### Changed

- Rewrite the whole project

## [0.32.0] - 2025-09-18

### Added

- Added --overwrite-files and UserInteraction when meet existing file @harry-xi

### Changed

- Skip instead of throwing an exception when a file is duplicated @harry-xi
- Improved exception output @harry-xi

### Fixed

- Use system default proxy [#282] @harry-xi
- Fixed fail to remove git repo from cache @harry-xi

## [0.31.0] - 2025-03-23

### Added

- Set second-pick default GitHub proxy to https://github.levimc.org

## [0.30.2] - 2025-03-21

### Added

- Self-contained installer

## [0.30.1] - 2025-03-19

### Added

- Installer will open browser to download .NET when it is not installed

## [0.30.0] - 2025-03-18

### Added

- Not self-contained builds

### Changed

- Installer now installs not self-contained builds

## [0.29.0] - 2025-03-17

### Added

- Try Git cloning on Go module proxy failure

### Fixed

- Fixed cache manager not cloning repos from all GitHub proxies
- Fixed not applying GitHub proxies when listing remote versions with Git
- Fixed abnormal installation

## [0.28.3] - 2025-03-15

### Changed

- Replaced List with IAsyncEnumerable in IFileSource.GetAllEntries
- Save content in memory for entries
- Suppress no await warning with Task.CompletedTask

### Fixed

- Fixed GitHub clone not via GitHub proxies
- Fixed missing +incompatible in CacheManager
- Fixed directory not delete when uninstall [#218]

## [0.28.2] - 2025-03-12

### Fixed

- Disabled CET to keep old Windows's compability
- Fixed an issue where packages with major version >= 2 were uninstalled when installing packages

## [0.28.1] - 2025-03-10

### Changed

- Return 0 when no command specified

## [0.28.0] - 2025-03-09

### Added

- Windows installer for lip CLI

## [0.27.2] - 2025-03-09

### Added

- Add installer for Windows

## [0.27.1] - 2025-03-08

### Changed

- Use goproxy.io as default go_module_proxies

### Fixed

- Fixed globbed platform when use lip migrate
- Removed useless platform property when manifest v2 doesn't have platforms property
- Fixed issue with +incompatible build metadata

## [0.27.0] - 2025-03-07

### Added

- Install latest versions when omitting versions
- `lip migrate` command to migrate tooth.json to the latest schema version

### Changed

- If `files.place` exists both inside and outside `platforms` in manifest v2, the `files.place` in the `platforms` takes
  precedence.

### Fixed

- Fail to process rooted file paths

## [0.26.2] - 2025-03-07

### Fixed

- Fixed wrong placements type caused by lip manifest v2 when files property is not in platforms property

## [0.26.1] - 2025-03-07

### Fixed

- Do not wait until scripts end

## [0.26.0] - 2025-03-06

### Added

- Show download speed
- Throw error on download failure
- Throw error on command failure

### Changed

- Prefer latest dependency

### Fixed

- Ambiguous error messages when no variant matches

## [0.25.1] - 2025-03-05

### Fixed

- Error message for invalid package specifiers not clear enough
- Scripts not kept in migration
- `lip install` without arguments not installing current directory

## [0.25.0] - 2025-03-05

### Added

- Multiple variant support
- Glob pattern support for files
- Glob pattern support for platform matching
- Multiple asset support
- Alternative asset source support
- Migration support for schema version 1 and 2 to 3
- Dependency resolution support
- Package pruning support
- Git support for asset source

### Changed

- Rewrite everything with C# and .NET 9.0
- Update schema to version 3
- Rewrite lip core library as a C# class library

## [0.24.0] - 2024-10-01

### Added

- `specifiers.txt` support

### Changed

- Allow colons in tags
- Parse arguments with `urfave/cli`

## [0.23.2] - 2024-08-17

### Fixed

- Partially resolved [#157] by adding .tmp extension to incomplete downloads (#158)

## [0.23.1] - 2024-07-28

### Added

- Add colorlog detector (#146)
- Add a line break to the end of the output of `lip --version`

## [0.23.0] - 2024-07-12

### Added

- Add version variable support for asset_url [#134]

### Fixed

- Fix `.lip` path in linux [#129]
- Fix unable to decompress .tar.gz [#140]
- Delete surplus tabs (#139)

## [0.22.0] - 2024-03-23

### Added

- `--no-color` flag to disable color output.
- NSIS installer.

## [0.21.2] - 2024-02-21

### Fixed

- Some ambiguous error messages.
- Not prompting for confirmation to overwrite existing files when installing a tooth.

## [0.21.1] - 2024-02-17

### Fixed

- Untidy error messages.
- Failed to install any tooth without setting a proxy.

## [0.21.0] - 2024-02-17

### Added

- Proxy configuration support.
- Support more kinds of GitHub mirrors
- Support `NO_COLOR` environment variable to disable color output.

### Changed

- Set `https://github.com` as the default GitHub mirror.

### Fixed

- Panic when dependencies and prerequisites have malformed version strings.

## [0.20.1] - 2024-02-04

### Fixed

- Version string.

## [0.20.0] - 2024-02-03

### Added

- Support Go module URL as asset URL.

### Changed

- Files downloaded from GitHub mirror are now cached in different paths.

### Removed

- `source` field in `tooth.json`.

## [0.19.0] - 2024-01-20

### Added

- Tons of debug logging.
- Show available versions without installing.

### Changed

- Optimize error logging.

### Fixed

- Wildcard not parsing correctly.
- Wrong error output when "%" is used in messages.
- Wrong version `@0.0.0` when stringify specifiers without version.

## [0.18.1] - 2024-01-12

### Fixed

- Platform-specific items in tooth.json.
- Go module path escaping.

## [0.18.0] - 2024-01-12

### Added

- Config support.
- GitHub mirror support.

### Changed

- Use `github.com/sirupsen/logrus` for logging.
- Use `github.com/blang/semver/v4` for versioning.
- Refactor most of the code.

### Removed

- Remove `lip autoremove` command.
- Percentage progress bar.

### Fixed

- Some bugs.

## [0.17.0] - 2023-12-30

### Added

- Warning for deprecated tooth.json format versions.
- `info.source` field for indicating the source repo of a tooth.

### Changed

- Check tooth repo validity with Go official module library.

### Fixed

- Failed to uninstall a tooth when some of its files are already removed.

## [0.16.1] - 2023-10-11

### Fixed

- Unable to specify which version to install.

## [0.16.0] - 2023-10-09

### Added

- Prerequisite support.

### Fixed

- Failing to parse files under root directory of a tooth.

## [0.15.2] - 2023-09-05

### Fixed

- Inconsistent tooth.json schema with lipIndex.
- Wrong help messages.

## [0.15.1] - 2023-09-01

### Fixed

- Wrong help message for `--json` flag of `lip show` command.
- Wrong regex for validating versions.

## [0.15.0] - 2023-06-23

### Added

- Tooth file preservation and removal.

### Changed

- Optimize output information format.
- Change tooth.json format to version 2.
- Force to specify output path in `lip tooth pack` command.
- Refactor all code and optimize performance.

### Removed

- `lip exec` command.
- Tooth file possession (use "preserve" and "remove" instead).
- Number-only progress bar.
- Registry support.
- Placed files display in `lip show` command.

## [0.14.2] - 2023-04-24

### Fixed

- Remove misleading message when dependencies are satisfied.
- Packed tooth file not able to be installed.

## [0.14.1] - 2023-04-20

### Fixed

- Broken GOOS and GOARCH specifiers of placement.

## [0.14.0] - 2023-03-24

### Added

- `lip tooth pack` command to pack a tooth into a tooth file.
- Questions when initializing a tooth.

### Fixed

- Not properly parsing placement sometimes.
- Not aborting when a tooth fails to install.

## [0.13.0] - 2023-03-05

### Added

- Aliases for subcommands.

### Changed

- Tool should be registered in `tooth.json` manually now.

## [0.12.0] - 2023-02-27

### Added

- Dependency version validation when installing.
- `lip autoremove` command to remove teeth not required by other teeth.

### Fixed

- Failing to run tools with arguments.
- Dependencies still being installed when the dependent is not going to be installed.

## [0.11.45141] - 2023-02-19

### Fixed

- Wrongly showing debug information when redirecting to local lip.

## [0.11.4514] - 2023-02-17

### Added

- Showing information about installed teeth in `lip list` command.

## [0.11.0] - 2023-02-17

### Added

- `--available` flag for `lip show` command.
- `--numeric-progress` flag for `lip install` command.
- `--no-dependencies` flag for `lip install` command.
- `confirmation` field in `tooth.json` to show messages and ask for confirmation before installing.
- Check for invalid additional arguments.
- Structured information output.
- Support for multiple GOPROXYs.
- `--keep-possession` flag for `lip uninstall` command.
- Automatic deletion of empty directories when uninstalling a tooth.
- Support for file possession.

### Fixed

- Remove wrongly displayed debug information.
- Failing to re-download when a broken tooth file exists in cache.

## [0.10.0] - 2023-02-12

### Added

- Verbose and quiet mode.
- JSON output support for `lip list` and `lip show`.

## [0.9.0] - 2023-02-11

### Added

- HTTP Code reporting when failing to make a request.
- `lip list --upgradable` command.
- Topological sorting for dependencies.
- Progress bar for downloading tooth files.

### Fixed

- No notice when a tooth file is cached.
- Tooth paths in `dependencies` field of `tooth.json` not converting to lowercase.
- Mistakes in help message of `lip cache purge`.

## [0.8.3] - 2023-02-09

### Fixed

- Mistakes in path prefix extraction when there is only one file in the tooth.

## [0.8.2] - 2023-02-09

### Fixed

- Failing to input anything to post-install and pre-uninstall commands.
- Wrong installation order of dependencies.
- Registry not working in `lip show`.
- Unstable versions can be wrongly installed when no specific version is specified.

## [0.8.1] - 2023-02-07

### Fixed

- Failing to get information from registry with other index than index.json.

## [0.8.0] - 2023-02-06

### Added

- Registry support. The default registry is <https://registry.litebds.com>.

### Fixed

- Failing to uninstall teeth with uppercase letters in provided tooth path.

## [0.7.1] - 2023-02-05

### Fixed

- Failing to hot update or remove lip in local directory.

## [0.7.0] - 2023-02-01

### Added

- Support for installing anything to any path.
- Prompt for confirmation when installing to a path that is not in working directory.

## [0.6.0] - 2023-01-31

### Added

- Support for on-demand installation depending on OS and platform.
- Removal for downloaded tooth files that do not pass the validation.

## [0.5.1] - 2023-01-30

### Fixed

- Failing to install any tool.

## [0.5.0] - 2023-01-30

### Added

- Available version list in `lip show` command.
- Redirection to local lip executable when running `lip`.
- Support for pre-uninstall scripts.
- Support for hot update of lip.
- Support for executing tools in `.lip/tools` directory.

## [0.4.0] - 2023-01-26

### Added

- Post-install script support.
- Tooth path validation.
- Flexible tooth.json parsing.

## [0.3.4] - 2023-01-25

### Changed

- Bumped github.com/fatih/color from 1.14.0 to 1.14.1.

### Fixed

- Misleading error hints.
- Failing to fetch tooth with major version v0 or v1.
- Failing to match dependencies.
- Failing to fetch tooth when uppercase letters exist in tooth path.

## [0.3.3] - 2023-01-24

### Fixed

- Default to earliest version when no version is specified in tooth.json.
- Panic when tooth.json is invalid.

## [0.3.2] - 2023-01-23

### Added

- "Add to PATH" option in setup utility.
- Mac OS, Linux and OpenBSD support.
- Arm64 support.

## [0.3.1] - 2023-01-21

### Added

- Setup utility to install lip.

## [0.3.0] - 2023-01-20

### Added

- Possession keeping support when force-reinstalling or upgrading.
- `--force-reinstall` flag and `--upgrade` flag support.

## [0.2.1] - 2023-01-18

### Fixed

- Failing to fetch tooth whose version has suffix `+incompatible`.
- Failing to parse wildcards.

## [0.2.0] - 2023-01-18

### Added

- Possession field in tooth.json to specify directory to remove when uninstalling a tooth.

### Changed

- Change extension name of tooth files to .tth

### Fixed

- Fix failing to fetch tooth when the repository does not contain go.mod file.
- Fix failing to parse tooth file when the tooth is downloaded via GOPROXY.
- Fix failing to parse tooth when tooth.json is the only file in the tooth.

## [0.1.0] - 2023-01-17

### Added

- Basic functions: cache, install, list, show, tooth init, and uninstall.

[#129]: https://github.com/futrime/lip/issues/129
[#134]: https://github.com/futrime/lip/issues/134
[#140]: https://github.com/futrime/lip/issues/140
[#157]: https://github.com/futrime/lip/issues/157
[#218]: https://github.com/futrime/lip/issues/218
[#282]: https://github.com/futrime/lip/issues/282
[#347]: https://github.com/futrime/lip/issues/347

[Unreleased]: https://github.com/LiteLDev/lip/compare/v0.34.9...HEAD
[0.34.9]: https://github.com/futrime/lip/compare/v0.34.8...v0.34.9
[0.34.8]: https://github.com/futrime/lip/compare/v0.34.7...v0.34.8
[0.34.7]: https://github.com/futrime/lip/compare/v0.34.6...v0.34.7
[0.34.6]: https://github.com/futrime/lip/compare/v0.34.5...v0.34.6
[0.34.5]: https://github.com/futrime/lip/compare/v0.34.4...v0.34.5
[0.34.4]: https://github.com/futrime/lip/compare/v0.34.4-beta.2...v0.34.4
[0.34.4-beta.2]: https://github.com/futrime/lip/compare/v0.34.3...v0.34.4-beta.2
[0.34.3]: https://github.com/futrime/lip/compare/v0.34.2...v0.34.3
[0.34.2]: https://github.com/futrime/lip/compare/v0.34.1...v0.34.2
[0.34.1]: https://github.com/futrime/lip/compare/v0.34.0...v0.34.1
[0.34.0]: https://github.com/futrime/lip/compare/v0.33.4...v0.34.0
[0.33.4]: https://github.com/futrime/lip/compare/v0.33.3...v0.33.4
[0.33.3]: https://github.com/futrime/lip/compare/v0.33.2...v0.33.3
[0.33.2]: https://github.com/futrime/lip/compare/v0.33.1...v0.33.2
[0.33.1]: https://github.com/futrime/lip/compare/v0.33.0...v0.33.1
[0.33.0]: https://github.com/futrime/lip/compare/v0.33.0-beta.2...v0.33.0
[0.33.0-beta.2]: https://github.com/futrime/lip/compare/v0.33.0-beta.1...v0.33.0-beta.2
[0.33.0-beta.1]: https://github.com/futrime/lip/compare/v0.32.0...v0.33.0-beta.1
[0.32.0]: https://github.com/futrime/lip/compare/v0.31.0...v0.32.0
[0.31.0]: https://github.com/futrime/lip/compare/v0.30.2...v0.31.0
[0.30.2]: https://github.com/futrime/lip/compare/v0.30.1...v0.30.2
[0.30.1]: https://github.com/futrime/lip/compare/v0.30.0...v0.30.1
[0.30.0]: https://github.com/futrime/lip/compare/v0.29.0...v0.30.0
[0.29.0]: https://github.com/futrime/lip/compare/v0.28.3...v0.29.0
[0.28.3]: https://github.com/futrime/lip/compare/v0.28.2...v0.28.3
[0.28.2]: https://github.com/futrime/lip/compare/v0.28.1...v0.28.2
[0.28.1]: https://github.com/futrime/lip/compare/v0.28.0...v0.28.1
[0.28.0]: https://github.com/futrime/lip/compare/v0.27.2...v0.28.0
[0.27.2]: https://github.com/futrime/lip/compare/v0.27.1...v0.27.2
[0.27.1]: https://github.com/futrime/lip/compare/v0.27.0...v0.27.1
[0.27.0]: https://github.com/futrime/lip/compare/v0.26.2...v0.27.0
[0.26.2]: https://github.com/futrime/lip/compare/v0.26.1...v0.26.2
[0.26.1]: https://github.com/futrime/lip/compare/v0.26.0...v0.26.1
[0.26.0]: https://github.com/futrime/lip/compare/v0.25.1...v0.26.0
[0.25.1]: https://github.com/futrime/lip/compare/v0.25.0...v0.25.1
[0.25.0]: https://github.com/futrime/lip/compare/v0.24.0...v0.25.0
[0.24.0]: https://github.com/futrime/lip/compare/v0.23.2...v0.24.0
[0.23.2]: https://github.com/futrime/lip/compare/v0.23.1...v0.23.2
[0.23.1]: https://github.com/futrime/lip/compare/v0.23.0...v0.23.1
[0.23.0]: https://github.com/futrime/lip/compare/v0.22.0...v0.23.0
[0.22.0]: https://github.com/futrime/lip/compare/v0.21.2...v0.22.0
[0.21.2]: https://github.com/futrime/lip/compare/v0.21.1...v0.21.2
[0.21.1]: https://github.com/futrime/lip/compare/v0.21.0...v0.21.1
[0.21.0]: https://github.com/futrime/lip/compare/v0.20.1...v0.21.0
[0.20.1]: https://github.com/futrime/lip/compare/v0.20.0...v0.20.1
[0.20.0]: https://github.com/futrime/lip/compare/v0.19.0...v0.20.0
[0.19.0]: https://github.com/futrime/lip/compare/v0.18.1...v0.19.0
[0.18.1]: https://github.com/futrime/lip/compare/v0.18.0...v0.18.1
[0.18.0]: https://github.com/futrime/lip/compare/v0.17.0...v0.18.0
[0.17.0]: https://github.com/futrime/lip/compare/v0.16.1...v0.17.0
[0.16.1]: https://github.com/futrime/lip/compare/v0.16.0...v0.16.1
[0.16.0]: https://github.com/futrime/lip/compare/v0.15.2...v0.16.0
[0.15.2]: https://github.com/futrime/lip/compare/v0.15.1...v0.15.2
[0.15.1]: https://github.com/futrime/lip/compare/v0.15.0...v0.15.1
[0.15.0]: https://github.com/futrime/lip/compare/v0.14.2...v0.15.0
[0.14.2]: https://github.com/futrime/lip/compare/v0.14.1...v0.14.2
[0.14.1]: https://github.com/futrime/lip/compare/v0.14.0...v0.14.1
[0.14.0]: https://github.com/futrime/lip/compare/v0.13.0...v0.14.0
[0.13.0]: https://github.com/futrime/lip/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/futrime/lip/compare/v0.11.45141...v0.12.0
[0.11.45141]: https://github.com/futrime/lip/compare/v0.11.4514...v0.11.45141
[0.11.4514]: https://github.com/futrime/lip/compare/v0.11.0...v0.11.4514
[0.11.0]: https://github.com/futrime/lip/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/futrime/lip/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/futrime/lip/compare/v0.8.3...v0.9.0
[0.8.3]: https://github.com/futrime/lip/compare/v0.8.2...v0.8.3
[0.8.2]: https://github.com/futrime/lip/compare/v0.8.1...v0.8.2
[0.8.1]: https://github.com/futrime/lip/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/futrime/lip/compare/v0.7.1...v0.8.0
[0.7.1]: https://github.com/futrime/lip/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/futrime/lip/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/futrime/lip/compare/v0.5.1...v0.6.0
[0.5.1]: https://github.com/futrime/lip/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/futrime/lip/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/futrime/lip/compare/v0.3.4...v0.4.0
[0.3.4]: https://github.com/futrime/lip/compare/v0.3.3...v0.3.4
[0.3.3]: https://github.com/futrime/lip/compare/v0.3.2...v0.3.3
[0.3.2]: https://github.com/futrime/lip/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/futrime/lip/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/futrime/lip/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/futrime/lip/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/futrime/lip/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/futrime/lip/releases/tag/v0.1.0
