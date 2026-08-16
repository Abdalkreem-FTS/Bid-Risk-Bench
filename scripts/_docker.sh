# Shared helpers. Sourced, not executed.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

DEV_IMAGE="bidrisk-ml-dev:local"

build_dev_image() {
  docker build \
    --quiet \
    --target dev \
    --file "${REPO_ROOT}/services/ml/Dockerfile" \
    --tag "$DEV_IMAGE" \
    "${REPO_ROOT}" >/dev/null
}

run_in_dev() {
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -e PYTHONDONTWRITEBYTECODE=1 \
    -v "${REPO_ROOT}:/app" \
    -w "${1}" \
    "$DEV_IMAGE" \
    bash -c "${2}"
}

DOTNET_SDK_TAG="${DOTNET_SDK_TAG:-10.0}"
TOOL_PUBLISH_DIR="tools/BidRisk.Bench/bin/publish"
NUGET_CACHE="${REPO_ROOT}/.cache/nuget"
mkdir -p "$NUGET_CACHE"

build_bench_tool() {
  local dll="${REPO_ROOT}/${TOOL_PUBLISH_DIR}/BidRisk.Bench.dll"

  # Rebuild if any source is newer. Checking only that the dll exists runs stale code.

  if [ -f "$dll" ] && [ -z "${TOOL_REBUILD:-}" ] && [ -z "$(
      find "${REPO_ROOT}/tools/BidRisk.Bench" "${REPO_ROOT}/services/contracts" \
        \( -name bin -o -name obj \) -prune -o \
        \( -name '*.cs' -o -name '*.csproj' \) -newer "$dll" -print -quit
    )" ]; then
    return 0
  fi

  echo "▸ building the .NET tooling console"
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
    dotnet publish tools/BidRisk.Bench/BidRisk.Bench.csproj \
      -c Release -o "$TOOL_PUBLISH_DIR" --nologo -v quiet

  if [ ! -f "$dll" ]; then
    echo "✗ the bench tool did not build — see the output above" >&2
    return 1
  fi
}

run_bench_tool() {
  local workdir="$1"
  shift

  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e DOTNET_NOLOGO=1 \
    ${TOOL_NETWORK:+--network "$TOOL_NETWORK"} \
    ${TOOL_CPUS:+--cpus "$TOOL_CPUS"} \
    ${TOOL_ENV:-} \
    -v "${REPO_ROOT}:/src" \
    -w "$workdir" \
    "mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG}" \
    dotnet "/src/${TOOL_PUBLISH_DIR}/BidRisk.Bench.dll" "$@"
}
