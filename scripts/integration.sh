#!/usr/bin/env bash
# Start the stack, seed it, and place real bids through GraphQL.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

NETWORK="bid-risk-bench_default"

echo "▸ starting the stack"
docker compose up -d --build

"${REPO_ROOT}/scripts/wait-healthy.sh" 300
"${REPO_ROOT}/scripts/seed.sh"

echo "▸ integration tests"
"${REPO_ROOT}/scripts/system-tests.sh" "$NETWORK" "Category=Integration"
