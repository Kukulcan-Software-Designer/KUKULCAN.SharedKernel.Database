#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "$BASH_SOURCE")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose.yml"
LAUNCHER="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-linux.sh"

fail() {
  printf '[FAIL] %s\n' "$1" >&2
  exit 1
}

[ -f "$COMPOSE_FILE" ] || fail "compose.yml was not found."
[ -x "$LAUNCHER" ] || fail "compose-up-linux.sh must exist and be executable."

grep -Fq 'external: true' "$COMPOSE_FILE" || fail "kukulcan-local must be declared as an external network."
grep -Fq 'Kukulcan__Database__Provider' "$COMPOSE_FILE" || fail "The i18n provider configuration key is missing."
grep -Fq 'Kukulcan__Database__ConnectionString' "$COMPOSE_FILE" || fail "The i18n connection-string configuration key is missing."
grep -Fq 'Jwt__SecretKey' "$COMPOSE_FILE" || fail "The i18n JWT secret configuration is missing."
grep -Fq 'ConnectionStrings__Redis' "$COMPOSE_FILE" || fail "The i18n Redis override is missing."

printf '[PASS] Docker Compose contract validated.\n'
