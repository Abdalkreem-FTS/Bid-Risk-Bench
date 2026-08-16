#!/usr/bin/env bash
# Block until every container reports healthy.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

TIMEOUT="${1:-180}"
DEADLINE=$((SECONDS + TIMEOUT))

ONESHOT="ollama-init"

while :; do
  unhealthy=""
  while read -r name state health; do
    [ -z "$name" ] && continue
    case " $ONESHOT " in *" $name "*) continue ;; esac

    if [ "$health" = "healthy" ]; then
      continue
    fi
    if [ -z "$health" ] && [ "$state" = "running" ]; then
      continue  # no health check declared; running is the best signal available
    fi
    unhealthy="$unhealthy $name(${health:-$state})"
  done < <(docker compose ps -a --format '{{.Service}} {{.State}} {{.Health}}')

  if [ -z "$unhealthy" ]; then
    echo "✓ all services healthy in $((SECONDS))s"
    docker compose ps --format '{{.Service}}\t{{.State}}\t{{.Health}}' | column -t
    exit 0
  fi

  if [ "$SECONDS" -ge "$DEADLINE" ]; then
    echo "✗ still not healthy after ${TIMEOUT}s:$unhealthy" >&2
    docker compose ps --format '{{.Service}}\t{{.State}}\t{{.Health}}' | column -t >&2
    exit 1
  fi

  echo "waiting:$unhealthy"
  sleep 5
done
