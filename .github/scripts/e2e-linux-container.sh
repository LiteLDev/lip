#!/bin/sh
# Run as root in a new container of a Linux distribution by the job e2e of ci.yml, with the
# repository at /repo: installs only what install.sh needs, curl (with what the distribution
# installs with it) and xz, and then, as a new user without root, installs lip with install.sh,
# as curl -fsSL https://levimc.org/lip/install.sh | sh does, and runs e2e-server.sh with it.
#
# LIP_VERSION and LIP_GITHUB_PROXY are passed on to install.sh, so that it installs the lip that
# the job built.
set -eu

head -n 2 /etc/os-release
apt-get update -qq
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq curl xz-utils > /dev/null

useradd -m -s /bin/bash player
su - player -c "
  set -eu
  LIP_VERSION='$LIP_VERSION' LIP_GITHUB_PROXY='$LIP_GITHUB_PROXY' sh < /repo/install.sh
  bash /repo/.github/scripts/e2e-server.sh \"\$HOME/.local/bin/lip\"
"
