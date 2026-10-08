#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "$BASH_SOURCE")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
LINUX_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-linux.sh"
MAC_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-macos.sh"
WINDOWS_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-windows.ps1"

fail() {
  printf '[FAIL] %s\n' "$1" >&2
  exit 1
}

for script in "$LINUX_SCRIPT" "$MAC_SCRIPT" "$WINDOWS_SCRIPT"; do
  [ -f "$script" ] || fail "Expected launcher is missing: $script"
done

assert_no_shell_literals() {
  local script="$1"

  if grep -Eq '^[[:space:]]*POSTGRES_(DB|USER|PASSWORD)="[^"$]+"' "$script"; then
    fail "Concrete PostgreSQL configuration literal found in $script."
  fi

  if grep -Eq '^[[:space:]]*POSTGRES_(DB|USER|PASSWORD)=[^$[:space:]" ]+' "$script"; then
    fail "Concrete PostgreSQL configuration literal found in $script."
  fi
}

assert_no_powershell_literals() {
  local script="$1"

  grep -Eq "Postgres(Db|User|Password)[[:space:]]*=[[:space:]]*[\"'][^\"']+[\"']" "$script" &&
    fail "Concrete PostgreSQL configuration literal found in $script."
}

assert_no_shell_literals "$LINUX_SCRIPT"
assert_no_shell_literals "$MAC_SCRIPT"
assert_no_powershell_literals "$WINDOWS_SCRIPT"

grep -Fq 'read -r -p' "$LINUX_SCRIPT" ||
  fail "Linux launcher must prompt for database configuration."

grep -Fq 'read -r -s -p' "$LINUX_SCRIPT" ||
  fail "Linux launcher must request the password without echoing it."

grep -Fq 'read -r -p' "$MAC_SCRIPT" ||
  fail "macOS launcher must prompt for database configuration."

grep -Fq 'read -r -s -p' "$MAC_SCRIPT" ||
  fail "macOS launcher must request the password without echoing it."

grep -Fq 'Read-Host' "$WINDOWS_SCRIPT" ||
  fail "Windows launcher must prompt for database configuration."

grep -Fq "Read-Host 'PostgreSQL password' -AsSecureString" "$WINDOWS_SCRIPT" ||
  fail "Windows launcher must request the password as a SecureString."

printf '[PASS] Docker Compose launchers require user-supplied database configuration.\n'
