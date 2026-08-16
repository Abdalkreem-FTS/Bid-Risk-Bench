#!/usr/bin/env bash
# Re-run the scoring scenarios to capture the model's own inference time.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

RUNS="${BENCH_RUNS:-3}"
NETWORK="bid-risk-bench_default"
K6_IMAGE="grafana/k6:0.55.0"
RAW="${REPO_ROOT}/bench/results/raw"

for run in $(seq 1 "$RUNS"); do
  echo "── run ${run} of ${RUNS} ─────────────────────────────────────────"
  for scenario in one batch; do
    name="score_${scenario}_graphql"
    printf '  %-28s ' "$name"
    docker run --rm --network "$NETWORK" \
      -v "${REPO_ROOT}/bench/k6:/scripts:ro" \
      -v "${REPO_ROOT}/bench/payloads:/payloads:ro" \
      -v "${RAW}/run${run}:/out" \
      --user "$(id -u):$(id -g)" \
      -e K6_NO_USAGE_REPORT=true \
      "$K6_IMAGE" run --quiet \
      --env GATEWAY_HOST=gateway --env GATEWAY_PORT=8080 \
      --env "OUT_NAME=${name}.tool" --env "RUN_ID=${run}" \
      --env "SCENARIO=${scenario}" \
      --env "VUS=$([ "$scenario" = batch ] && echo 1 || echo 10)" \
      --env "ITERATIONS=$([ "$scenario" = batch ] && echo 20 || echo 500)" \
      /scripts/score.js > /dev/null 2>&1
    echo "done"
  done
done

echo "▸ re-aggregating"
build_bench_tool
run_bench_tool /src/bench aggregate \
  --raw /src/bench/results/raw --out /src/bench/results/results.json --runs "${RUNS}"
run_bench_tool /src/bench charts \
  --results /src/bench/results/results.json --out /src/bench/results/charts
run_bench_tool /src/bench tables \
  --results /src/bench/results/results.json \
  --payload-sizes /src/bench/results/payload-sizes.json \
  --readme /src/README.md
echo "✓ updated"
