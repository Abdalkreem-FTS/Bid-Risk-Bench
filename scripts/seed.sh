#!/usr/bin/env bash
# Load the fixed dataset. --regenerate rebuilds it first, which needs the stack up.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

SEED_SQL="${REPO_ROOT}/data/seed/seed.sql"

if [ "${1:-}" = "--regenerate" ]; then
  echo "▸ regenerating seed.sql"
  build_bench_tool
  TOOL_NETWORK="bid-risk-bench_default" \
  TOOL_ENV="-e ML_GRPC_HOST=ml-service -e ML_GRPC_PORT=5002" \
    run_bench_tool /src seed --out /src/data/seed/seed.sql
fi

if [ ! -f "$SEED_SQL" ]; then
  echo "✗ no ${SEED_SQL} — run: ./scripts/seed.sh --regenerate" >&2
  exit 1
fi

echo "▸ loading the fixed dataset"
docker compose exec -T postgres \
  psql --quiet --set ON_ERROR_STOP=1 \
    --username "${POSTGRES_USER:-bidrisk}" \
    --dbname "${POSTGRES_DB:-bidrisk}" < "$SEED_SQL"

docker compose exec -T postgres \
  psql --tuples-only --no-align \
    --username "${POSTGRES_USER:-bidrisk}" \
    --dbname "${POSTGRES_DB:-bidrisk}" \
    --command "SELECT 'loaded: ' || (SELECT count(*) FROM bidders) || ' bidders, '
                                 || (SELECT count(*) FROM lots) || ' lots, '
                                 || (SELECT count(*) FROM bids) || ' bids';"
