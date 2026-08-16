#!/usr/bin/env bash
# Run the whole benchmark matrix and write bench/results/.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

RUNS="${BENCH_RUNS:-3}"
NETWORK="bid-risk-bench_default"
GHZ_IMAGE="ghcr.io/bojand/ghz:0.120.0"
K6_IMAGE="grafana/k6:0.55.0"

GEN_CPUS="${BENCH_GEN_CPUS:-}"

RESULTS="${REPO_ROOT}/bench/results"
RAW="${RESULTS}/raw"
SAMPLED_SERVICES=(gateway auction-service ml-service ollama postgres)

net_bytes() {
  local service="$1"
  docker compose exec -T "$service" sh -c \
    'cat /sys/class/net/eth0/statistics/rx_bytes /sys/class/net/eth0/statistics/tx_bytes' \
    2>/dev/null | paste -sd' ' || echo "0 0"
}

snapshot_net() {
  local out="$1"
  {
    echo "{"
    local first=1
    for service in "${SAMPLED_SERVICES[@]}"; do
      read -r rx tx <<<"$(net_bytes "$service")"
      [ $first -eq 1 ] || echo ","
      first=0
      printf '  "%s": {"rx": %s, "tx": %s}' "$service" "${rx:-0}" "${tx:-0}"
    done
    echo
    echo "}"
  } > "$out"
}

STATS_PID=""
start_stats() {
  local out="$1"
  : > "$out"
  (
    while :; do
      docker stats --no-stream --format '{{.Name}},{{.CPUPerc}},{{.MemUsage}}' >> "$out" 2>/dev/null || true
      sleep 2
    done
  ) &
  STATS_PID=$!
}

cooldown() {
  local seconds="${1:-15}"
  echo "  … cooling down ${seconds}s"
  sleep "$seconds"
}

stop_stats() {
  [ -n "$STATS_PID" ] && kill "$STATS_PID" 2>/dev/null || true
  STATS_PID=""
}

measure() {
  local label="$1"; shift
  local dir="${RAW}/run${RUN}"
  mkdir -p "$dir"

  printf '  %-28s ' "$label"
  snapshot_net "${dir}/${label}.net.before.json"
  start_stats "${dir}/${label}.stats.csv"

  local started ended
  started=$(date +%s%N)
  if ! "$@" > "${dir}/${label}.log" 2>&1; then
    stop_stats
    echo "FAILED (see ${dir}/${label}.log)"
    return 1
  fi
  ended=$(date +%s%N)

  if [ ! -s "${dir}/${label}.tool.json" ]; then
    stop_stats
    echo "FAILED (no output — is the stack up? see ${dir}/${label}.log)"
    return 1
  fi

  stop_stats
  snapshot_net "${dir}/${label}.net.after.json"
  echo "{\"wall_ms\": $(( (ended - started) / 1000000 ))}" > "${dir}/${label}.wall.json"
  echo "$(( (ended - started) / 1000000 ))ms"
}

ghz_run() {
  local out="$1" call="$2" data="$3" n="$4" c="$5" target="$6"
  docker run --rm --network "$NETWORK" \
    ${GEN_CPUS:+--cpus "$GEN_CPUS"} \
    -v "${REPO_ROOT}/proto:/proto:ro" \
    -v "${REPO_ROOT}/bench/payloads:/payloads:ro" \
    -v "${RAW}/run${RUN}:/out" \
    --user "$(id -u):$(id -g)" \
    "$GHZ_IMAGE" \
    --insecure \
    --proto /proto/bidrisk/bidrisk.proto --import-paths /proto \
    --call "$call" \
    -D "/payloads/${data}" \
    -n "$n" -c "$c" \
    -O json -o "/out/${out}.tool.json" \
    "$target"
}

k6_run() {
  local out="$1" script="$2"; shift 2
  local env_args=()
  for pair in "$@"; do env_args+=(--env "$pair"); done

  docker run --rm --network "$NETWORK" \
    ${GEN_CPUS:+--cpus "$GEN_CPUS"} \
    -v "${REPO_ROOT}/bench/k6:/scripts:ro" \
    -v "${REPO_ROOT}/bench/payloads:/payloads:ro" \
    -v "${RAW}/run${RUN}:/out" \
    --user "$(id -u):$(id -g)" \
    -e K6_NO_USAGE_REPORT=true \
    "$K6_IMAGE" run --quiet \
    --env "GATEWAY_HOST=gateway" --env "GATEWAY_PORT=8080" \
    --env "OUT_NAME=${out}.tool" --env "RUN_ID=${RUN}" \
    "${env_args[@]}" \
    "/scripts/${script}"
}

probe_run() {
  local out="$1" listeners="$2" question="$3"
  TOOL_NETWORK="$NETWORK" \
  TOOL_CPUS="$GEN_CPUS" \
  TOOL_ENV="-e ML_GRPC_HOST=ml-service -e ML_GRPC_PORT=5002" \
    run_bench_tool /src/bench probe \
      --listeners "$listeners" \
      --question "$question" \
      --out "/src/bench/results/raw/run${RUN}/${out}.tool.json"
}

STREAM_LOTS=(5 6 10)
FANOUT_LOTS=(13 14 11)

lot_question() {
  local offset="$1"; shift
  local -a pool=("$@")
  echo "Summarise the bidding activity on lot ${pool[$(((RUN - 1 + offset) % ${#pool[@]}))]}."
}

GATEWAY_URL="http://localhost:${GATEWAY_HTTP_PORT:-8080}"

reset_calls() {
  curl -s -X POST "${GATEWAY_URL}/debug/grpc-calls/reset" >/dev/null || true
}

save_calls() {
  curl -s "${GATEWAY_URL}/debug/grpc-calls" > "${RAW}/run${RUN}/${1}.calls.json" || true
}

set_batching() {
  local value="$1"
  sed -i "s/^GATEWAY_BATCHING=.*/GATEWAY_BATCHING=${value}/" "${REPO_ROOT}/.env"
  docker compose up -d gateway >/dev/null 2>&1
  "${REPO_ROOT}/scripts/wait-healthy.sh" 120 >/dev/null
  k6_run warmup_discarded_gateway lots.js VUS=10 ITERATIONS=400 >/dev/null 2>&1 || true
  rm -f "${RAW}/run${RUN}"/warmup_discarded*.json
}

warm_up_stack() {
  echo "  … warming up"
  k6_run warmup_discarded score.js SCENARIO=one VUS=10 ITERATIONS=1500 >/dev/null 2>&1 || true
  k6_run warmup_discarded_ws score_ws.js SCENARIO=one VUS=10 ITERATIONS=1000 >/dev/null 2>&1 || true
  k6_run warmup_discarded_lots lots.js VUS=10 ITERATIONS=400 >/dev/null 2>&1 || true
  ghz_run warmup_discarded_grpc \
    bidrisk.RiskService.PredictBidRisk grpc_predict_many.json 1500 10 ml-service:5002 \
    >/dev/null 2>&1 || true
  ghz_run warmup_discarded_lots_grpc \
    bidrisk.AuctionService.ListLots grpc_list_lots.json 1500 10 auction-service:5001 \
    >/dev/null 2>&1 || true
  probe_run warmup_discarded_llm 1 "Summarise the bidding activity on lot 24." >/dev/null 2>&1 || true
  rm -f "${RAW}/run${RUN}"/warmup_discarded*.json
}

run_scenarios() {
  echo "── run ${RUN} of ${RUNS} ─────────────────────────────────────────"

  "${REPO_ROOT}/scripts/seed.sh" >/dev/null
  mkdir -p "${RAW}/run${RUN}"
  warm_up_stack

  measure score_one_grpc ghz_run score_one_grpc \
    bidrisk.RiskService.PredictBidRisk grpc_predict_many.json 2000 10 ml-service:5002
  measure score_one_graphql k6_run score_one_graphql score.js SCENARIO=one VUS=10 ITERATIONS=2000
  measure score_one_ws k6_run score_one_ws score_ws.js SCENARIO=one VUS=10 ITERATIONS=2000

  measure score_serial_grpc ghz_run score_serial_grpc \
    bidrisk.RiskService.PredictBidRisk grpc_predict_many.json 500 1 ml-service:5002
  measure score_serial_graphql k6_run score_serial_graphql score.js SCENARIO=one VUS=1 ITERATIONS=500
  measure score_serial_ws k6_run score_serial_ws score_ws.js \
    SCENARIO=serial VUS=1 ITERATIONS=1 SERIAL_MESSAGES=500

  measure score_batch_grpc ghz_run score_batch_grpc \
    bidrisk.RiskService.PredictBatch grpc_predict_batch.json 150 1 ml-service:5002
  measure score_batch_graphql k6_run score_batch_graphql score.js SCENARIO=batch VUS=1 ITERATIONS=150
  measure score_batch_ws k6_run score_batch_ws score_ws.js SCENARIO=batch VUS=1 ITERATIONS=150

  measure lots_grpc ghz_run lots_grpc \
    bidrisk.AuctionService.ListLots grpc_list_lots.json 4000 10 auction-service:5001

  set_batching off
  reset_calls
  measure lots_graphql_naive k6_run lots_graphql_naive lots.js VUS=10 ITERATIONS=500
  save_calls lots_graphql_naive

  set_batching on
  reset_calls
  measure lots_graphql_batched k6_run lots_graphql_batched lots.js VUS=10 ITERATIONS=500
  save_calls lots_graphql_batched
  set_batching off

  if [ "${BENCH_SKIP_LLM:-0}" = "1" ]; then
    echo "  (streaming scenarios skipped)"
    return
  fi

  measure stream_one_grpc probe_run stream_one_grpc 1 "$(lot_question 0 "${STREAM_LOTS[@]}")"
  measure stream_one_graphql k6_run stream_one_graphql stream.js \
    TRANSPORT=graphql LISTENERS=1 "QUESTION=$(lot_question 1 "${STREAM_LOTS[@]}")"
  measure stream_one_ws k6_run stream_one_ws stream.js \
    TRANSPORT=raw LISTENERS=1 "QUESTION=$(lot_question 2 "${STREAM_LOTS[@]}")"

  measure fanout_50_grpc probe_run fanout_50_grpc 50 "$(lot_question 0 "${FANOUT_LOTS[@]}")"
  measure fanout_50_graphql k6_run fanout_50_graphql stream.js \
    TRANSPORT=graphql LISTENERS=50 "QUESTION=$(lot_question 1 "${FANOUT_LOTS[@]}")"
  measure fanout_50_ws k6_run fanout_50_ws stream.js \
    TRANSPORT=raw LISTENERS=50 "QUESTION=$(lot_question 2 "${FANOUT_LOTS[@]}")"
}

trap stop_stats EXIT

echo "▸ preparing"
build_bench_tool
"${REPO_ROOT}/scripts/wait-healthy.sh" 300 >/dev/null
run_bench_tool /src/bench payloads --out /src/bench/payloads

rm -rf "$RAW"
mkdir -p "$RAW"

for RUN in $(seq 1 "$RUNS"); do
  [ "$RUN" -gt 1 ] && cooldown 20
  run_scenarios
done

if [ "$RUNS" -lt 3 ] || [ "${BENCH_SKIP_LLM:-0}" = "1" ]; then
  RESULTS_FILE="results/results-partial.json"
  CHARTS_DIR="results/charts-partial"
  UPDATE_README=0
  echo "▸ partial run — writing ${RESULTS_FILE}, leaving results.json and README untouched"
else
  RESULTS_FILE="results/results.json"
  CHARTS_DIR="results/charts"
  UPDATE_README=1
fi

echo "▸ payload sizes"
run_bench_tool /src/bench sizes --out /src/bench/results/payload-sizes.json

echo "▸ aggregating"
run_bench_tool /src/bench aggregate \
  --raw /src/bench/results/raw --out "/src/bench/${RESULTS_FILE}" --runs "${RUNS}"
run_bench_tool /src/bench charts \
  --results "/src/bench/${RESULTS_FILE}" --out "/src/bench/${CHARTS_DIR}"

if [ "$UPDATE_README" = "1" ]; then
  echo "▸ report tables"
  run_bench_tool /src/bench tables \
    --results "/src/bench/${RESULTS_FILE}" \
    --payload-sizes /src/bench/results/payload-sizes.json \
    --readme /src/README.md
  echo "✓ bench/${RESULTS_FILE} · bench/${CHARTS_DIR}/ · README updated"
else
  echo "✓ bench/${RESULTS_FILE} · bench/${CHARTS_DIR}/ (README not touched)"
fi
