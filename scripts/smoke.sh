#!/usr/bin/env bash
# One call over each transport. Run it before every commit.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

NETWORK="bid-risk-bench_default"
LLM_CHECKS="${LLM_CHECKS:-on}"

if [ "$LLM_CHECKS" = "off" ]; then
  echo "▸ smoke (streaming legs skipped: LLM_CHECKS=off)"
  FILTER="Category=Smoke"
else
  echo "▸ smoke"
  FILTER="Category=Smoke|Category=SmokeLlm"
fi

"${REPO_ROOT}/scripts/system-tests.sh" "$NETWORK" "$FILTER"

echo
echo "✓ smoke passed"
