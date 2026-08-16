from __future__ import annotations

import json
import subprocess
from pathlib import Path

import pytest
from bidrisk import bidrisk_pb2

REPO_ROOT = Path(__file__).resolve().parents[3]
GOLDEN_DIR = REPO_ROOT / "tests" / "contract" / "golden"
GENERATED_DIR = REPO_ROOT / "services" / "ml" / "generated"
HASH_SCRIPT = REPO_ROOT / "scripts" / "proto-hash.sh"

REGENERATE_HINT = "run `make golden`"


@pytest.fixture(scope="module")
def expected() -> dict:
    path = GOLDEN_DIR / "expected.json"
    if not path.exists():
        pytest.fail(f"missing {path} — {REGENERATE_HINT}")
    return json.loads(path.read_text(encoding="utf-8"))


def load_golden(name: str) -> bidrisk_pb2.PlaceBidResponse:
    path = GOLDEN_DIR / name
    if not path.exists():
        pytest.fail(f"missing {path} — {REGENERATE_HINT}")
    message = bidrisk_pb2.PlaceBidResponse()
    message.ParseFromString(path.read_bytes())
    return message


def test_accepted_golden_decodes_to_the_expected_values(expected: dict) -> None:
    response = load_golden("accepted.bin")
    assert response.WhichOneof("outcome") == expected["accepted"]["outcome_case"]

    bid = response.accepted.bid
    want = expected["accepted"]["bid"]
    assert bid.id == want["id"]
    assert bid.lot_id == want["lot_id"]
    assert bid.bidder_id == want["bidder_id"]
    assert bid.amount == want["amount"]
    assert bid.placed_at.seconds == want["placed_at_seconds"]
    assert bid.placed_at.nanos == want["placed_at_nanos"]
    assert bid.risk_score == want["risk_score"]
    assert bidrisk_pb2.RiskLevel.Name(bid.risk_level) == want["risk_level"]
    assert bid.accepted == want["accepted"]
    assert bidrisk_pb2.RejectReason.Name(bid.reject_reason) == want["reject_reason"]


def test_rejected_golden_decodes_to_the_expected_values(expected: dict) -> None:
    response = load_golden("rejected.bin")
    assert response.WhichOneof("outcome") == expected["rejected"]["outcome_case"]

    rejected = response.rejected
    want = expected["rejected"]
    assert bidrisk_pb2.RejectReason.Name(rejected.reason) == want["reason"]
    assert int(rejected.reason) == want["reason_number"]
    assert rejected.message == want["message"]


@pytest.mark.parametrize("golden", ["accepted.bin", "rejected.bin"])
def test_the_risk_assessment_survives_in_both_arms(golden: str, expected: dict) -> None:
    response = load_golden(golden)
    risk = response.accepted.risk if golden == "accepted.bin" else response.rejected.risk
    want = expected["risk"]

    assert risk.score == want["score"]
    assert bidrisk_pb2.RiskLevel.Name(risk.level) == want["level"]
    assert int(risk.level) == want["level_number"]
    assert list(risk.reasons) == want["reasons"]
    assert risk.inference_ms == want["inference_ms"]
    assert risk.model_version == want["model_version"]


def test_the_oneof_arms_are_mutually_exclusive() -> None:
    assert load_golden("accepted.bin").WhichOneof("outcome") == "accepted"
    assert load_golden("rejected.bin").WhichOneof("outcome") == "rejected"
    assert bidrisk_pb2.PlaceBidResponse().WhichOneof("outcome") is None


def test_generated_python_code_is_up_to_date_with_the_proto() -> None:
    stored = GENERATED_DIR / ".proto_hash"
    if not stored.exists():
        pytest.fail(f"missing {stored} — run `make proto`")

    current = subprocess.run(
        ["bash", str(HASH_SCRIPT)], capture_output=True, text=True, check=True
    ).stdout.strip()

    assert (
        stored.read_text(encoding="utf-8").strip() == current
    ), "the .proto has changed since the Python code was generated — run `make proto`"
