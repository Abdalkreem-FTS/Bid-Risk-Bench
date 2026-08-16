#!/usr/bin/env bash
# Every quality gate CI runs, in one command. A warning anywhere is a failure.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

BUF_IMAGE_TAG="${BUF_IMAGE_TAG:-1.72.0}"

echo "▸ buf lint"
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp \
  -v "${REPO_ROOT}:/w" -w /w "bufbuild/buf:${BUF_IMAGE_TAG}" lint

build_dev_image

echo "▸ ruff"
run_in_dev /app/services/ml "ruff check . && ruff format --check ."

echo "▸ mypy"
run_in_dev /app/services/ml \
  "mypy bidrisk_ml server.py train.py generate.py healthcheck.py contract/write_golden.py"

echo "▸ dotnet build (warnings are errors)"
if [ -d "${REPO_ROOT}/services/contracts/Generated" ]; then
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e NUGET_PACKAGES=/nuget \
    -v "${REPO_ROOT}:/src" -v "${NUGET_CACHE}:/nuget" -w /src \
    "mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG:-10.0}" \
    dotnet build --nologo --verbosity quiet
else
  echo "  skipped — no generated C# code yet, run ./scripts/proto-gen.sh"
fi

echo "✓ no warnings"
