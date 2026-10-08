#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "$BASH_SOURCE")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
MAC_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-macos.sh"
WIN_SCRIPT="$REPO_ROOT/Documentation/Docker/PostgreSQL/compose-up-windows.ps1"

fail() {
  printf '[FAIL] %s\n' "$1" >&2
  exit 1
}

[ -f "$MAC_SCRIPT" ] || fail "The macOS Compose launcher is missing."
[ -f "$WIN_SCRIPT" ] || fail "The Windows Compose launcher is missing."

bash -n "$MAC_SCRIPT" || fail "The macOS Compose launcher has invalid Bash syntax."

grep -Fq 'KUKULCAN_POSTGRES_ENV_FILE' "$MAC_SCRIPT" || fail "The macOS launcher must support KUKULCAN_POSTGRES_ENV_FILE."
grep -Fq 'docker compose' "$MAC_SCRIPT" || fail "The macOS launcher must use Docker Compose when a service is missing."
grep -Fq 'ensure_i18n_service' "$MAC_SCRIPT" || fail "The macOS launcher must implement the i18n service decision."
grep -Fq 'ensure_service' "$MAC_SCRIPT" || fail "The macOS launcher must implement reusable service handling."

grep -Fq 'docker compose' "$MAC_SCRIPT" || fail "The macOS launcher must use Docker Compose when a service is missing."

grep -Fq 'KUKULCAN_I18N_JWT_SECRET' "$MAC_SCRIPT" || fail "The macOS launcher must support the i18n JWT secret."

command -v pwsh >/dev/null 2>&1 || fail "PowerShell (pwsh) is required to validate the Windows launcher."

WIN_SCRIPT_PATH="$WIN_SCRIPT" pwsh -NoProfile -Command '
$path = $env:WIN_SCRIPT_PATH
$script = Get-Content -LiteralPath $path -Raw
[scriptblock]::Create($script) | Out-Null
if ($script -notmatch "KUKULCAN_POSTGRES_ENV_FILE") { throw "Windows launcher must support KUKULCAN_POSTGRES_ENV_FILE." }
if ($script -notmatch "docker compose") { throw "Windows launcher must use Docker Compose when a service is missing." }
if ($script -notmatch "EnsureI18nService") { throw "Windows launcher must implement the i18n service decision." }
if ($script -notmatch "EnsureService") { throw "Windows launcher must implement reusable service handling." }
if ($script -notmatch "KUKULCAN_I18N_JWT_SECRET") { throw "Windows launcher must support the i18n JWT secret." }
' || fail "The Windows Compose launcher failed PowerShell validation."

printf '[PASS] macOS and Windows Compose launcher contracts validated.\n'
