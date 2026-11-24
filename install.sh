#!/bin/sh
# Installs lip on Linux and macOS, without administrator rights:
#
#   curl -fsSL https://levimc.org/lip/install.sh | sh
#
# protocol/install.sh.md says what it does. In short: it downloads the release archive of lip for this
# system from GitHub, checks it against the release's SHA256SUMS, puts lip in ~/.local/bin, and offers
# to add that directory to PATH. These environment variables change what it does:
#
#   LIP_VERSION           the version to install, like 0.42.0 or v0.42.0; the latest release by default
#   LIP_INSTALL_DIR       the directory to install lip to; ~/.local/bin by default
#   LIP_GITHUB_PROXY      a URL that requests to GitHub go through, as lip's github_proxy
#   LIP_NO_MODIFY_PATH=1  never change a shell startup file
#
# The whole script is a function called on its last line, so that a download cut short runs nothing.

set -eu

say() {
	printf '%s\n' "$*"
}

die() {
	printf 'error: %s\n' "$*" >&2
	exit 1
}

# The proxy prefix that lip's github_proxy defines: the URI, without a trailing /github.com or
# /https://github.com, followed by a slash unless it ends with one. Empty when no proxy is set.
proxy_prefix() {
	p=${LIP_GITHUB_PROXY:-}
	[ -n "$p" ] || return 0
	q=${p%/}
	case $q in
	*/https://github.com) p=${q%/https://github.com} ;;
	*/github.com) p=${q%/github.com} ;;
	esac
	case $p in
	*/) printf '%s' "$p" ;;
	*) printf '%s/' "$p" ;;
	esac
}

# download URL FILE: an HTTPS GET of URL to FILE, following redirects, through the proxy when one is
# set, and directly when that fails.
download() {
	if [ -n "$proxy" ] && curl -fsSL --retry 2 -o "$2" "$proxy$1"; then
		return 0
	fi
	curl -fsSL --proto '=https' --tlsv1.2 --retry 2 -o "$2" "$1"
}

# The version of the latest release: the last segment of the URL that /releases/latest redirects to,
# like https://github.com/LiteLDev/lip/releases/tag/v0.42.0.
latest_version() {
	u=https://github.com/LiteLDev/lip/releases/latest
	e=
	if [ -n "$proxy" ]; then
		e=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$proxy$u" 2>/dev/null) || e=
	fi
	case $e in
	*/LiteLDev/lip/releases/tag/v*) ;;
	*) e=$(curl -fsSLI --proto '=https' --tlsv1.2 -o /dev/null -w '%{url_effective}' "$u") ||
		die "could not find the latest release of lip at $u" ;;
	esac
	case $e in
	*/LiteLDev/lip/releases/tag/v*) printf '%s' "${e##*/releases/tag/v}" ;;
	*) die "could not find the latest release of lip at $u" ;;
	esac
}

sha256() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1" | cut -d ' ' -f 1
	elif command -v shasum >/dev/null 2>&1; then
		shasum -a 256 "$1" | cut -d ' ' -f 1
	else
		die "found neither sha256sum nor shasum to check the download with"
	fi
}

# The platform of the release archive for this system.
detect_platform() {
	os=$(uname -s)
	arch=$(uname -m)
	case $os in
	Linux)
		case $arch in
		x86_64 | amd64) platform=linux-x64 ;;
		aarch64 | arm64) die "lip is not released for Linux on arm64, where a LeviLamina server does not run" ;;
		*) die "lip is not released for Linux on $arch" ;;
		esac
		;;
	Darwin)
		case $arch in
		arm64) platform=osx-arm64 ;;
		x86_64)
			# A shell under Rosetta 2 on Apple silicon reports x86_64.
			if [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || true)" = 1 ]; then
				platform=osx-arm64
			else
				platform=osx-x64
			fi
			;;
		*) die "lip is not released for macOS on $arch" ;;
		esac
		;;
	MINGW* | MSYS* | CYGWIN* | Windows_NT)
		die "this script is for Linux and macOS. On Windows, run the .msi installer from https://github.com/LiteLDev/lip/releases/latest"
		;;
	*) die "lip is not released for $os" ;;
	esac
}

# Whether a line of FILE, not a comment, names the install directory: as its absolute path, or as
# $HOME/..., ${HOME}/... or ~/... when it is in $HOME, not followed by a character of a file name.
names_dir() {
	[ -f "$1" ] || return 1
	forms=$dir
	case $dir in
	"$HOME"/*)
		r=${dir#"$HOME"/}
		forms="$forms
\$HOME/$r
\${HOME}/$r
~/$r"
		;;
	esac
	LIP_FORMS=$forms awk '
		BEGIN { n = split(ENVIRON["LIP_FORMS"], f, "\n") }
		/^[ \t]*#/ { next }
		{
			for (i = 1; i <= n; i++) {
				s = $0
				while ((p = index(s, f[i])) > 0) {
					c = substr(s, p + length(f[i]), 1)
					if (c !~ /[A-Za-z0-9._-]/) { found = 1; exit }
					s = substr(s, p + 1)
				}
			}
		}
		END { exit !found }
	' "$1"
}

# Whether a shell startup file that the user's shell reads already adds the install directory to PATH.
# Prints the first such file.
profile_adding_dir() {
	z=${ZDOTDIR:-$HOME}
	case $shell_name in
	zsh) set -- "$z/.zshenv" "$z/.zprofile" "$z/.zshrc" "$z/.zlogin" ;;
	bash) set -- "$HOME/.bash_profile" "$HOME/.bash_login" "$HOME/.profile" "$HOME/.bashrc" ;;
	fish) set -- "$HOME/.config/fish/config.fish" ;;
	*) set -- "$HOME/.profile" ;;
	esac
	for f in "$@"; do
		if names_dir "$f"; then
			printf '%s' "$f"
			return 0
		fi
	done
	return 1
}

# Whether the user can be asked: /dev/tty can be opened, as the standard input is the script.
have_tty() {
	(: </dev/tty) 2>/dev/null
}

setup_path() {
	case ":$PATH:" in
	*":$dir:"* | *":$dir/:"*) return 0 ;;
	esac

	shell_name=${SHELL:-}
	shell_name=${shell_name##*/}

	if f=$(profile_adding_dir); then
		say ""
		say "$dir is not on PATH in this shell, but $f adds it to PATH in new shells:"
		say "open a new terminal, or log out and in again (or run: exec \"\$SHELL\" -l)."
		say "Until then, run lip as $dir/lip."
		return 0
	fi

	case $dir in
	"$HOME"/*) shown=\$HOME/${dir#"$HOME"/} ;;
	*) shown=$dir ;;
	esac
	case $shell_name in
	zsh) rc=${ZDOTDIR:-$HOME}/.zshrc line="export PATH=\"$shown:\$PATH\"" ;;
	bash)
		# Login shells, as Terminal on macOS starts, read only the first of these that exists.
		if [ "$(uname -s)" = Darwin ]; then
			rc=$HOME/.bash_profile
			[ -f "$rc" ] || [ ! -f "$HOME/.bash_login" ] || rc=$HOME/.bash_login
			[ -f "$rc" ] || [ ! -f "$HOME/.profile" ] || rc=$HOME/.profile
		else
			rc=$HOME/.bashrc
		fi
		line="export PATH=\"$shown:\$PATH\""
		;;
	fish) rc=$HOME/.config/fish/config.fish line="fish_add_path \"$shown\"" ;;
	*) rc=$HOME/.profile line="export PATH=\"$shown:\$PATH\"" ;;
	esac

	if [ "${LIP_NO_MODIFY_PATH:-}" != 1 ] && have_tty; then
		say ""
		printf '%s is not on PATH. Add it to PATH in %s? [Y/n] ' "$dir" "$rc" >/dev/tty
		answer=
		read -r answer </dev/tty || answer=n
		case $answer in
		'' | [Yy] | [Yy][Ee][Ss])
			mkdir -p "$(dirname "$rc")"
			if ! { [ -f "$rc" ] && grep -qxF "$line" "$rc"; }; then
				# After an empty line, and after a line break when the file does not end with one.
				if [ -s "$rc" ]; then
					[ -z "$(tail -c 1 "$rc")" ] || printf '\n' >>"$rc"
					printf '\n' >>"$rc"
				fi
				printf '# Added by the lip installer\n%s\n' "$line" >>"$rc"
			fi
			say "Added $dir to PATH in $rc. Open a new terminal, or run: . \"$rc\""
			[ "$shell_name" != fish ] || say "(in fish: source \"$rc\")"
			say "Until then, run lip as $dir/lip."
			return 0
			;;
		esac
	fi

	say ""
	say "$dir is not on PATH. To add it, add this line to $rc:"
	say ""
	say "    $line"
	say ""
	say "Until then, run lip as $dir/lip."
}

main() {
	command -v curl >/dev/null 2>&1 || die "curl is needed to download lip; install it and try again"
	[ -n "${HOME:-}" ] || die "HOME is not set"
	command -v sha256sum >/dev/null 2>&1 || command -v shasum >/dev/null 2>&1 ||
		die "sha256sum or shasum is needed to check the download; install one and try again"

	detect_platform
	case $platform in
	linux-*)
		ext=tar.xz
		command -v xz >/dev/null 2>&1 ||
			die "xz is needed to extract lip; install it (on Debian and Ubuntu: apt install xz-utils) and try again"
		;;
	*) ext=zip ;;
	esac

	proxy=$(proxy_prefix)

	version=${LIP_VERSION:-}
	version=${version#v}
	if [ -z "$version" ]; then
		version=$(latest_version)
	fi
	case $version in
	'' | *[!0-9A-Za-z.+-]*) die "\"$version\" is not a version of lip" ;;
	esac

	dir=${LIP_INSTALL_DIR:-$HOME/.local/bin}
	case $dir in
	/*) ;;
	*) die "LIP_INSTALL_DIR must be an absolute path, not $dir" ;;
	esac
	while [ "$dir" != / ] && [ "${dir%/}" != "$dir" ]; do dir=${dir%/}; done

	name=lip-$version-$platform.$ext
	base=https://github.com/LiteLDev/lip/releases/download/v$version

	tmp=$(mktemp -d 2>/dev/null || mktemp -d -t lip)
	trap 'rm -rf "$tmp"' EXIT
	trap 'exit 1' HUP INT TERM

	say "Downloading lip $version for $platform..."
	download "$base/$name" "$tmp/$name" || die "could not download $base/$name"
	download "$base/SHA256SUMS" "$tmp/SHA256SUMS" || die "could not download $base/SHA256SUMS"

	expected=$(awk -v n="$name" '$2 == n || $2 == "*" n { print $1; exit }' "$tmp/SHA256SUMS")
	[ -n "$expected" ] || die "SHA256SUMS of lip $version does not list $name"
	actual=$(sha256 "$tmp/$name")
	[ "$actual" = "$expected" ] ||
		die "the SHA-256 checksum of $name is $actual, but SHA256SUMS says $expected; nothing was installed"

	mkdir "$tmp/x"
	case $ext in
	tar.xz) tar -xJf "$tmp/$name" -C "$tmp/x" ;;
	zip)
		if command -v ditto >/dev/null 2>&1; then
			ditto -x -k "$tmp/$name" "$tmp/x"
		else
			unzip -q "$tmp/$name" -d "$tmp/x"
		fi
		;;
	esac
	[ -f "$tmp/x/lip" ] || die "$name has no file lip"

	mkdir -p "$dir" || die "could not create $dir"
	# Each file is written to P.tmp in the directory, then renamed to P.
	(cd "$tmp/x" && find . -type f) | while IFS= read -r f; do
		f=${f#./}
		mkdir -p "$dir/$(dirname "$f")"
		cp "$tmp/x/$f" "$dir/$f.tmp"
		chmod +x "$dir/$f.tmp"
		mv -f "$dir/$f.tmp" "$dir/$f"
	done

	say "Installed lip to $dir/lip"
	if ! v=$("$dir/lip" -v 2>&1); then
		printf '%s\n' "$v" >&2
		die "$dir/lip was installed, but does not run"
	fi
	say "$v"

	# Only when the directory is on PATH already: the line that setup_path adds puts it first.
	existing=
	case ":$PATH:" in
	*":$dir:"* | *":$dir/:"*) existing=$(command -v lip 2>/dev/null || true) ;;
	esac
	setup_path
	if [ -n "$existing" ] && [ "$(dirname "$existing")" != "$dir" ]; then
		say ""
		say "Note: another lip, $existing, comes first on PATH."
	fi

	say ""
	say "To set up a server, run:"
	say ""
	say "    mkdir servers && cd servers && lip"
	if [ "$platform" = osx-arm64 ]; then
		say ""
		say "lip runs the server through Wine, which needs Rosetta 2 on Apple silicon."
		say "When Rosetta 2 is missing, lip offers to install it."
	fi
}

main "$@"
