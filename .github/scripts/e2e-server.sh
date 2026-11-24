#!/usr/bin/env bash
# The part of the job e2e of ci.yml that every system shares, run by the user that runs lip:
# in a new directory, installs LeviLamina with lip install, as a user does, runs the server with
# lip run, sends it list once it has started and stop once it has answered, and fails unless
# LeviLamina was loaded, the server started and answered list, and lip run exited with 0.
#
# Usage: e2e-server.sh LIP, where LIP is the lip to run.

# Without -e: the checks below say how lip run failed.
set +e -uo pipefail

lip=$1
workspace=$(mktemp -d)
cd "$workspace" || exit 1

# --accept-rosetta-license: nothing here can answer the question of lip on a Mac with Apple silicon.
"$lip" install LeviLamina --accept-eula --accept-rosetta-license || exit 1

: > server.log
# Waits up to ten minutes until server.log has the line $1; fails once lip run has exited.
wait_for() {
  for ((i = 0; i < 600; i++)); do
    if grep -q "$1" server.log; then
      return 0
    fi
    if [ -e exited ]; then
      return 1
    fi
    sleep 1
  done
  return 1
}
# Writes list once the server has started, and stop once it has answered.
feed() {
  if wait_for 'Server started'; then
    echo list
    wait_for 'players online'
  fi
  echo stop
}
"$lip" run --accept-rosetta-license < <(feed) 2>&1 | tee server.log
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
if ! grep -q 'players online' server.log; then
  echo "::error::The server did not answer list (lip run exited with $code)."
  exit 1
fi
if [ "$code" -ne 0 ]; then
  echo "::error::The server started, but lip run exited with $code."
  exit 1
fi
echo "LeviLamina was loaded, the server started and answered list, and lip run exited with 0."
