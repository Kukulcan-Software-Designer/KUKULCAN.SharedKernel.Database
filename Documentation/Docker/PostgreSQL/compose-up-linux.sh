#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "$BASH_SOURCE")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/compose.yml"
ENV_FILE="$KUKULCAN_POSTGRES_ENV_FILE"
if [ -z "$ENV_FILE" ]; then ENV_FILE="$SCRIPT_DIR/.env"; fi

NETWORK_NAME="kukulcan-local"
I18N_CONTAINER="kukulcan-i18n"

fail() {
  printf '[ERROR] %s\n' "$1" >&2
  exit 1
}

[ -f "$COMPOSE_FILE" ] || fail "compose.yml was not found."
[ -f "$ENV_FILE" ] || fail ".env was not found. Create it from the local Docker configuration."

env_value() {
  local key="$1"
  local default_value="$2"
  local value

  value="$(awk -v key="$key" -F= '$1 == key { sub(/^[^=]*=/, ""); print; exit }' "$ENV_FILE")"
  if [ -z "$value" ]; then
    value="$default_value"
  fi
  printf '%s' "$value"
}

POSTGRES_CONTAINER="$(env_value POSTGRES_HOST mypostgres)"
POSTGRES_DB="$(env_value POSTGRES_DB Atlas)"
POSTGRES_USER="$(env_value POSTGRES_USER postgres)"
HTTP_PORT="$(env_value KUKULCAN_I18N_HTTP_PORT 8080)"

ensure_network() {
  if docker network inspect "$NETWORK_NAME" >/dev/null 2>&1; then
    return
  fi

  printf '[INFO] Creating Docker network %s.\n' "$NETWORK_NAME"
  docker network create "$NETWORK_NAME" >/dev/null
}

container_exists() {
  docker container inspect "$1" >/dev/null 2>&1
}

container_running() {
  [ "$(docker inspect -f '{{.State.Running}}' "$1")" = "true" ]
}

container_on_network() {
  docker network inspect "$NETWORK_NAME" \
    --format '{{range .Containers}}{{.Name}}{{"\n"}}{{end}}' \
    | grep -Fxq "$1"
}

ensure_network_membership() {
  local container="$1"

  if container_on_network "$container"; then
    return
  fi

  printf '[INFO] Connecting %s to %s.\n' "$container" "$NETWORK_NAME"
  docker network connect "$NETWORK_NAME" "$container"
}

ensure_service() {
  local service="$1"
  local container="$2"

  if container_exists "$container"; then
    ensure_network_membership "$container"

    if container_running "$container"; then
      printf '[INFO] Using existing running container %s.\n' "$container"
    else
      printf '[INFO] Starting existing container %s.\n' "$container"
      docker start "$container" >/dev/null
    fi
    return
  fi

  printf '[INFO] Container %s does not exist; creating service %s with Compose.\n' "$container" "$service"
  docker compose \
    --env-file "$ENV_FILE" \
    -f "$COMPOSE_FILE" \
    up -d --no-deps "$service"
}

wait_for_postgres() {
  local attempt

  printf '[INFO] Waiting for PostgreSQL readiness.\n'
  for attempt in $(seq 1 30); do
    if docker exec "$POSTGRES_CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1; then
      printf '[INFO] PostgreSQL is ready.\n'
      return
    fi
    sleep 2
  done

  docker logs "$POSTGRES_CONTAINER" >&2 2>/dev/null || true
  fail "PostgreSQL did not become ready."
}

wait_for_http() {
  local name="$1"
  local url="$2"
  local attempt

  printf '[INFO] Waiting for %s.\n' "$name"
  for attempt in $(seq 1 30); do
    if curl --silent --show-error --fail "$url" >/dev/null 2>&1; then
      printf '[INFO] %s is UP.\n' "$name"
      return
    fi
    sleep 2
  done

  docker logs "$I18N_CONTAINER" >&2 2>/dev/null || true
  fail "$name did not become available."
}

ensure_network
ensure_service mypostgres "$POSTGRES_CONTAINER"
ensure_network_membership "$POSTGRES_CONTAINER"
wait_for_postgres

ensure_service kukulcan-i18n "$I18N_CONTAINER"
ensure_network_membership "$I18N_CONTAINER"

wait_for_http "i18n liveness" "http://127.0.0.1:$HTTP_PORT/health/live"
wait_for_http "i18n readiness" "http://127.0.0.1:$HTTP_PORT/health/ready"

printf '[100%%] Docker Compose deployment is ready.\n'
printf '      PostgreSQL: %s / %s\n' "$POSTGRES_CONTAINER" "$POSTGRES_DB"
printf '      i18n:       %s\n' "$I18N_CONTAINER"
printf '      API:        http://127.0.0.1:%s\n' "$HTTP_PORT"
