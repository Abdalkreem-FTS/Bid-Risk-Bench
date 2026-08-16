#!/usr/bin/env bash
# One sha256 of the contract. The only definition: Python and C# tests both call this.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

{
  find proto -name '*.proto' -type f | LC_ALL=C sort | while read -r f; do
    printf '%s\n' "$f"
    cat "$f"
  done
} | sha256sum | cut -d' ' -f1
