#!/usr/bin/env bash
# Unit tests and the contract test, in containers. Nothing to install locally.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

echo "▸ python tests"
build_dev_image
run_in_dev /app/services/ml "python -m pytest -q"

echo "▸ dotnet tests"
"${REPO_ROOT}/scripts/dotnet-test.sh"
