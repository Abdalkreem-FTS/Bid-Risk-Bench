#!/usr/bin/env bash
# C# tests inside the SDK image. The filter leaves out the tests that need a running stack.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

DOTNET_SDK_TAG="${DOTNET_SDK_TAG:-10.0}"

if [ ! -d "${REPO_ROOT}/services/contracts/Generated" ]; then
  echo "✗ no generated C# code — run: ./scripts/proto-gen.sh" >&2
  exit 1
fi

docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e NUGET_PACKAGES=/nuget \
  -v "${REPO_ROOT}:/src" \
  -v "${NUGET_CACHE}:/nuget" \
  -w /src \
  "mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG}" \
  dotnet test BidRiskBench.slnx --nologo --verbosity quiet \
    --filter "Category!=Integration&Category!=Smoke&Category!=SmokeLlm"
