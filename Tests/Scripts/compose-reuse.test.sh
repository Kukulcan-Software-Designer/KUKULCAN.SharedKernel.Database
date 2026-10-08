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
[ -f "$LAUNCHER" ] || fail "compose-up-linux.sh was not found."

grep -Fq 'external: true' "$COMPOSE_FILE" || fail "kukulcan-local must be declared as an external network."
grep -Fq 'Kukulcan__Database__Provider' "$COMPOSE_FILE" || fail "The i18n provider configuration key is missing."
grep -Fq 'Kukulcan__Database__ConnectionString' "$COMPOSE_FILE" || fail "The i18n connection-string configuration key is missing."
grep -Fq 'Jwt__SecretKey' "$COMPOSE_FILE" || fail "The i18n JWT secret configuration is missing."
grep -Fq 'ConnectionStrings__Redis' "$COMPOSE_FILE" || fail "The i18n Redis override is missing."

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

FAKE_BIN="$TMP_DIR/bin"
STATE_DIR="$TMP_DIR/state"
LOG_FILE="$TMP_DIR/docker.log"
mkdir -p "$FAKE_BIN" "$STATE_DIR"

cat > "$STATE_DIR/.env" <<'ENV'
POSTGRES_HOST=mypostgres
POSTGRES_DB=Atlas
POSTGRES_USER=postgres
KUKULCAN_I18N_HTTP_PORT=8080
ENV

cat > "$FAKE_BIN/docker" <<'FAKE_DOCKER'
#!/usr/bin/env bash
set -eo pipefail

STATE_DIR="$FAKE_DOCKER_STATE_DIR"
LOG_FILE="$FAKE_DOCKER_LOG"
printf '%s\n' "$*" >> "$LOG_FILE"

cmd="$1"
shift

case "$cmd" in
  network)
    sub="$1"
    shift
    case "$sub" in
      inspect)
        name="$1"
        [ "$name" = "kukulcan-local" ]
        [ -f "$STATE_DIR/network" ]
        cat "$STATE_DIR/network-containers" 2>/dev/null || true
        ;;
      create)
        name="$1"
        [ "$name" = "kukulcan-local" ]
        touch "$STATE_DIR/network"
        : > "$STATE_DIR/network-containers"
        ;;
      connect)
        network="$1"
        container="$2"
        [ "$network" = "kukulcan-local" ]
        touch "$STATE_DIR/network"
        grep -Fxq "$container" "$STATE_DIR/network-containers" 2>/dev/null || printf '%s\n' "$container" >> "$STATE_DIR/network-containers"
        ;;
      *) exit 1 ;;
    esac
    ;;
  container)
    sub="$1"
    shift
    [ "$sub" = "inspect" ]
    name="$1"
    [ -f "$STATE_DIR/container.$name" ]
    ;;
  inspect)
    name="$1"
    state_file="$STATE_DIR/container.$name"
    [ -f "$state_file" ]
    state="$(cat "$state_file")"
    if [ "$1" = "$name" ]; then
      shift
    fi
    if [ "$1" = "-f" ]; then
      printf '%s\n' "$([ "$state" = "running" ] && echo true || echo false)"
    fi
    ;;
  start)
    name="$1"
    printf 'running\n' > "$STATE_DIR/container.$name"
    ;;
  exec)
    name="$1"
    [ "$name" = "mypostgres" ]
    ;;
  compose)
    service=""
    while [ "$#" -gt 0 ]; do
      case "$1" in
        --env-file|-f) shift 2 ;;
        up) shift ;;
        -d|--detach|--no-deps) shift ;;
        mypostgres|kukulcan-i18n) service="$1"; shift ;;
        *) shift ;;
      esac
    done
    [ -n "$service" ]
    printf 'running\n' > "$STATE_DIR/container.$service"
    printf '%s\n' "$service" >> "$STATE_DIR/compose-created"
    touch "$STATE_DIR/network"
    grep -Fxq "$service" "$STATE_DIR/network-containers" 2>/dev/null || printf '%s\n' "$service" >> "$STATE_DIR/network-containers"
    ;;
  *) exit 1 ;;
esac
FAKE_DOCKER

cat > "$FAKE_BIN/curl" <<'FAKE_CURL'
#!/usr/bin/env bash
exit 0
FAKE_CURL

chmod +x "$FAKE_BIN/docker" "$FAKE_BIN/curl"

export PATH="$FAKE_BIN:$PATH"
export FAKE_DOCKER_STATE_DIR="$STATE_DIR"
export FAKE_DOCKER_LOG="$LOG_FILE"
export KUKULCAN_POSTGRES_ENV_FILE="$STATE_DIR/.env"

reset_case() {
  rm -f "$STATE_DIR"/container.* "$STATE_DIR/compose-created" "$STATE_DIR/network" "$STATE_DIR/network-containers" "$LOG_FILE"
  : > "$LOG_FILE"
  touch "$STATE_DIR/network"
  : > "$STATE_DIR/network-containers"
}

reset_case
printf 'stopped\n' > "$STATE_DIR/container.mypostgres"
printf 'stopped\n' > "$STATE_DIR/container.kukulcan-i18n"
bash "$LAUNCHER" >/dev/null
grep -Fxq "start mypostgres" "$LOG_FILE" || fail "Existing PostgreSQL container was not started."
grep -Fxq "start kukulcan-i18n" "$LOG_FILE" || fail "Existing i18n container was not started."
! grep -Fq "compose" "$LOG_FILE" || fail "Existing containers were recreated through Compose."

reset_case
bash "$LAUNCHER" >/dev/null
grep -Fxq "running" "$STATE_DIR/container.mypostgres" || fail "Missing PostgreSQL container was not created."
grep -Fxq "running" "$STATE_DIR/container.kukulcan-i18n" || fail "Missing i18n container was not created."
grep -Fxq "mypostgres" "$STATE_DIR/compose-created" || fail "PostgreSQL service was not created through Compose."
grep -Fxq "kukulcan-i18n" "$STATE_DIR/compose-created" || fail "i18n service was not created through Compose."

reset_case
printf 'running\n' > "$STATE_DIR/container.mypostgres"
bash "$LAUNCHER" >/dev/null
grep -Fxq "running" "$STATE_DIR/container.kukulcan-i18n" || fail "Missing i18n container was not created."
grep -Fxq "kukulcan-i18n" "$STATE_DIR/compose-created" || fail "i18n service was not created through Compose."

reset_case
printf 'running\n' > "$STATE_DIR/container.mypostgres"
printf 'running\n' > "$STATE_DIR/container.kukulcan-i18n"
bash "$LAUNCHER" >/dev/null
! grep -Fq "compose" "$LOG_FILE" || fail "Running containers were recreated through Compose."

printf '[PASS] Docker Compose reuse/create behavior validated.\n'
