#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "$BASH_SOURCE")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
MAC_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-macos.sh"
LINUX_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-linux.sh"
WINDOWS_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-windows.ps1"

fail() {
  printf '[FAIL] %s\n' "$1" >&2
  exit 1
}

for script in "$LINUX_SCRIPT" "$MAC_SCRIPT" "$WINDOWS_SCRIPT"; do
  [ -f "$script" ] || fail "Expected launcher is missing: $script"
done

assert_no_database_defaults() {
  local script="$1"

  if grep -Eq "POSTGRES_DB.*(Atlas|ATLAS)|PostgresDb.*['\" ]+Atlas['\"]" "$script"; then
    fail "Concrete database-name default found in $script."
  fi

  if grep -Eq "POSTGRES_USER.*(postgres)|PostgresUser.*['\" ]+postgres['\"]" "$script"; then
    fail "Concrete database-user default found in $script."
  fi

  if grep -Eq "POSTGRES_PASSWORD.*(vcl5nht4HtCkm5nJ|['\"][^'\"]{8,}['\"])" "$script"; then
    fail "Concrete database password or password literal found in $script."
  fi
}

assert_prompt_support() {
  local script="$1"

  grep -Eq 'POSTGRES_DB|PostgresDb' "$script" ||
    fail "Database-name configuration is missing from $script."

  grep -Eq 'POSTGRES_USER|PostgresUser' "$script" ||
    fail "Database-user configuration is missing from $script."

  grep -Eq 'POSTGRES_PASSWORD|postgresPassword' "$script" ||
    fail "Database-password configuration is missing from $script."
}

assert_no_database_defaults "$LINUX_SCRIPT"
assert_no_database_defaults "$MAC_SCRIPT"
assert_no_database_defaults "$WINDOWS_SCRIPT"

grep -Fq 'read -r -p' "$LINUX_SCRIPT" ||
  fail "Linux launcher must prompt for configuration values."

grep -Fq 'read -r -p' "$MAC_SCRIPT" ||
  fail "macOS launcher must prompt for configuration values."

grep -Fq 'Read-Host' "$WINDOWS_SCRIPT" ||
  fail "Windows launcher must prompt for configuration values."

assert_prompt_support "$LINUX_SCRIPT"
assert_prompt_support "$MAC_SCRIPT"
assert_prompt_support "$WINDOWS_SCRIPT"

printf '[PASS] Docker Compose launchers require user-supplied database configuration.\n'
