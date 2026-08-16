#!/usr/bin/env bash
# Regenerate the golden files the cross-language contract test reads.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_docker.sh"

build_dev_image
run_in_dev /app/services/ml/contract \
  "PYTHONPATH=/app/services/ml/generated python write_golden.py"
