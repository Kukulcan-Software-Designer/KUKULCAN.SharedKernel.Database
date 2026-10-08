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
grep -Fq 'Kukulcan__Database__Provider' "$MAC_SCRIPT" || fail "The macOS launcher must configure the i18n provider."
grep -Fq 'Kukulcan__Database__ConnectionString' "$MAC_SCRIPT" || fail "The macOS launcher must configure the i18n connection string."
grep -Fq 'Jwt__SecretKey' "$MAC_SCRIPT" || fail "The macOS launcher must configure the JWT secret."
grep -Fq 'ConnectionStrings__Redis' "$MAC_SCRIPT" || fail "The macOS launcher must disable Redis by default."

command -v pwsh >/dev/null 2>&1 || fail "PowerShell (pwsh) is required to validate the Windows launcher."

WIN_SCRIPT_PATH="$WIN_SCRIPT" pwsh -NoProfile -Command '
$path = $env:WIN_SCRIPT_PATH
$script = Get-Content -LiteralPath $path -Raw
[scriptblock]::Create($script) | Out-Null
if ($script -notmatch "KUKULCAN_POSTGRES_ENV_FILE") { throw "Windows launcher must support KUKULCAN_POSTGRES_ENV_FILE." }
if ($script -notmatch "Kukulcan__Database__Provider") { throw "Windows launcher must configure the i18n provider." }
if ($script -notmatch "Kukulcan__Database__ConnectionString") { throw "Windows launcher must configure the i18n connection string." }
if ($script -notmatch "Jwt__SecretKey") { throw "Windows launcher must configure the JWT secret." }
if ($script -notmatch "ConnectionStrings__Redis") { throw "Windows launcher must disable Redis by default." }
' || fail "The Windows Compose launcher failed PowerShell validation."

printf '[PASS] macOS and Windows Compose launcher contracts validated.\n'
