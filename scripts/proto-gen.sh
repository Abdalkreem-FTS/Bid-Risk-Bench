#!/usr/bin/env bash
# Regenerate gRPC code for both languages from the one .proto file.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

BUF_IMAGE_TAG="${BUF_IMAGE_TAG:-1.72.0}"

buf() {
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -v "${REPO_ROOT}:/w" \
    -w /w \
    "bufbuild/buf:${BUF_IMAGE_TAG}" "$@"
}

PY_OUT="services/ml/generated"
CS_OUT="services/contracts/Generated"

echo "▸ buf lint"
buf lint

if git rev-parse --verify --quiet origin/main >/dev/null; then
  echo "▸ buf breaking (against origin/main)"
  buf breaking --against '.git#branch=origin/main'
else
  echo "▸ buf breaking — skipped (no origin/main baseline yet)"
fi

echo "▸ buf generate (Python + C#)"
rm -rf "$PY_OUT" "$CS_OUT"
mkdir -p "$PY_OUT" "$CS_OUT"
buf generate

touch "$PY_OUT/bidrisk/__init__.py"

echo "▸ writing proto hash"
"$REPO_ROOT/scripts/proto-hash.sh" > "$PY_OUT/.proto_hash"
cp "$PY_OUT/.proto_hash" "$CS_OUT/.proto_hash"

echo "✓ generated $(find "$PY_OUT" "$CS_OUT" -type f -name '*.py' -o -name '*.pyi' -o -name '*.cs' | wc -l) files · hash $(cat "$PY_OUT/.proto_hash")"
