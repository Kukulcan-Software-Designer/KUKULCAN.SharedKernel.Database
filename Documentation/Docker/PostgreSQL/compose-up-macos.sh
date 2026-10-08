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
  if [ -z "$value" ]; then value="$default_value"; fi
  printf '%s' "$value"
}

POSTGRES_CONTAINER="$(env_value POSTGRES_HOST mypostgres)"
POSTGRES_DB="$(env_value POSTGRES_DB Atlas)"
POSTGRES_USER="$(env_value POSTGRES_USER postgres)"
HTTP_PORT="$(env_value KUKULCAN_I18N_HTTP_PORT 8080)"

ensure_network() {
  if docker network inspect "$NETWORK_NAME" >/dev/null 2>&1; then return; fi
  printf '[INFO] Creating Docker network %s.\n' "$NETWORK_NAME"
  docker network create "$NETWORK_NAME" >/dev/null
}

container_exists() {
  docker container inspect "$1" >/dev/null 2>&1
}

container_running() {
  [ "$(docker inspect "$1" -f '{{.State.Running}}')" = "true" ]
}

container_env_value() {
  local container="$1" key="$2" default_value="$3" value
  value="$(docker inspect "$container" -f '{{range .Config.Env}}{{println .}}{{end}}' |
    awk -v key="$key" -F= '$1 == key { sub(/^[^=]*=/, ""); print; exit }')"
  if [ -z "$value" ]; then value="$default_value"; fi
  printf '%s' "$value"
}

container_on_network() {
  docker network inspect "$NETWORK_NAME" \
    --format '{{range .Containers}}{{.Name}}{{"\n"}}{{end}}' |
    grep -Fxq "$1"
}

ensure_network_membership() {
  local container="$1"
  if container_on_network "$container"; then return; fi
  printf '[INFO] Connecting %s to %s.\n' "$container" "$NETWORK_NAME"
  docker network connect "$NETWORK_NAME" "$container"
}

ensure_service() {
  local service="$1" container="$2"

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
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --no-deps "$service"
}

ensure_i18n_service() {
  if container_exists "$I18N_CONTAINER"; then
    ensure_service kukulcan-i18n "$I18N_CONTAINER"
    return
  fi

  if [ -z "$KUKULCAN_I18N_JWT_SECRET" ]; then
    if command -v openssl >/dev/null 2>&1; then
      export KUKULCAN_I18N_JWT_SECRET="$(openssl rand -base64 48 | tr -d '\n')"
    else
      export KUKULCAN_I18N_JWT_SECRET="$(head -c 48 /dev/urandom | base64 | tr -d '\n')"
    fi
    printf '[INFO] Generated a temporary local JWT secret for kukulcan-i18n.\n'
  fi

  ensure_service kukulcan-i18n "$I18N_CONTAINER"
}

wait_for_postgres() {
  local attempt
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
  local name="$1" url="$2" attempt
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

if container_exists "$POSTGRES_CONTAINER"; then
  POSTGRES_DB="$(container_env_value "$POSTGRES_CONTAINER" POSTGRES_DB "$POSTGRES_DB")"
  POSTGRES_USER="$(container_env_value "$POSTGRES_CONTAINER" POSTGRES_USER "$POSTGRES_USER")"
  POSTGRES_PASSWORD="$(container_env_value "$POSTGRES_CONTAINER" POSTGRES_PASSWORD "$(env_value POSTGRES_PASSWORD "")")"
fi

export POSTGRES_HOST="$POSTGRES_CONTAINER"
export POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD

ensure_service mypostgres "$POSTGRES_CONTAINER"
ensure_network_membership "$POSTGRES_CONTAINER"
wait_for_postgres

ensure_i18n_service
ensure_network_membership "$I18N_CONTAINER"

wait_for_http "i18n liveness" "http://127.0.0.1:$HTTP_PORT/health/live"
wait_for_http "i18n readiness" "http://127.0.0.1:$HTTP_PORT/health/ready"

printf '[100%%] Docker Compose deployment is ready.\n'
printf '      PostgreSQL: %s / %s\n' "$POSTGRES_CONTAINER" "$POSTGRES_DB"
printf '      i18n:       %s\n' "$I18N_CONTAINER"
printf '      API:        http://127.0.0.1:%s\n' "$HTTP_PORT"
