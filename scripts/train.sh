#!/usr/bin/env bash
# Generate the synthetic bids and train the model, in Docker.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

ENV_FILE=".env"
[ -f "$ENV_FILE" ] || ENV_FILE=".env.example"

echo "▸ building dependency image"
docker build \
  --quiet \
  --target deps \
  --file services/ml/Dockerfile \
  --tag bidrisk-ml-deps:local \
  . >/dev/null

echo "▸ generating data and training (config from ${ENV_FILE})"
docker run --rm \
  --user "$(id -u):$(id -g)" \
  --env-file "$ENV_FILE" \
  -e HOME=/tmp \
  -e PYTHONPATH=/app/services/ml \
  -v "${REPO_ROOT}:/app" \
  -w /app/services/ml \
  bidrisk-ml-deps:local \
  bash -c "python generate.py && python train.py"
