#!/usr/bin/env bash
# The tests that need a running stack. integration.sh and smoke.sh both call this.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

NETWORK="${1:?usage: system-tests.sh <network> <filter>}"
FILTER="${2:?usage: system-tests.sh <network> <filter>}"

mkdir -p "${REPO_ROOT}/.cache/nuget"

docker run --rm \
  --user "$(id -u):$(id -g)" \
  --network "$NETWORK" \
  -e HOME=/tmp \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e NUGET_PACKAGES=/nuget \
  -e AUCTION_GRPC_HOST=auction-service -e AUCTION_GRPC_PORT=5001 \
  -e ML_GRPC_HOST=ml-service -e ML_GRPC_PORT=5002 \
  -e GATEWAY_HOST=gateway -e GATEWAY_HTTP_PORT=8080 \
  -v "${REPO_ROOT}:/src" \
  -v "${REPO_ROOT}/.cache/nuget:/nuget" \
  -w /src \
  "mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG}" \
  dotnet test tests/BidRisk.SystemTests/BidRisk.SystemTests.csproj \
    --filter "$FILTER" --nologo --verbosity quiet
