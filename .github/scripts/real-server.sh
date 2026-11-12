#!/usr/bin/env bash
# Installs LeviLamina into a new workspace with the lip given as the first argument, runs the
# server with lip run until it has started, asks it to stop, and fails unless LeviLamina was
# loaded, the server started, and lip run exited with 0.
set -uo pipefail

lip=$1
workspace=$(mktemp -d)
cd "$workspace" || exit 1

"$lip" install github.com/LiteLDev/LeviLamina || exit 1

: > server.log
# Writes the line stop once the server has started or exited, or after ten minutes.
feed() {
  for ((i = 0; i < 600; i++)); do
    if grep -q 'Server started' server.log || [ -e exited ]; then
      break
    fi
    sleep 1
  done
  echo stop
}
"$lip" run < <(feed) 2>&1 | tee server.log
code=${PIPESTATUS[0]}
touch exited

if ! grep -q 'LeviLamina Loaded' server.log; then
  echo "::error::LeviLamina was not loaded (lip run exited with $code)."
  exit 1
fi
if ! grep -q 'Server started' server.log; then
  echo "::error::The server did not start (lip run exited with $code)."
  exit 1
fi
if [ "$code" -ne 0 ]; then
  echo "::error::The server started, but lip run exited with $code."
  exit 1
fi
