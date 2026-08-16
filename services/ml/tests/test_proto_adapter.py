from __future__ import annotations

from datetime import UTC, datetime, timedelta

import pytest
from bidrisk import bidrisk_pb2
from google.protobuf.timestamp_pb2 import Timestamp

from bidrisk_ml.predictor import LEVEL_HIGH, LEVEL_LOW, LEVEL_MEDIUM, Prediction
from bidrisk_ml.proto_adapter import assessment_to_proto, facts_from_context, level_to_proto

BID_TIME = datetime(2026, 8, 13, 12, 0, 0, tzinfo=UTC)


def ts(moment: datetime) -> Timestamp:
    stamp = Timestamp()
    stamp.FromDatetime(moment)
    return stamp


def context(**overrides: object) -> bidrisk_pb2.BidContext:
    message = bidrisk_pb2.BidContext(
        amount=110.0,
        current_price=100.0,
        bidder_bids_on_lot=2,
        lot_total_bids=10,
    )
    message.bid_time.CopyFrom(ts(BID_TIME))
    message.previous_bid_time.CopyFrom(ts(BID_TIME - timedelta(minutes=5)))
    message.bidder_account_created.CopyFrom(ts(BID_TIME - timedelta(days=365)))
    for field, value in overrides.items():
        setattr(message, field, value)
    return message


def test_scalar_fields_pass_through_unchanged() -> None:
    facts = facts_from_context(context())
    assert facts.amount == pytest.approx(110.0)
    assert facts.current_price == pytest.approx(100.0)
    assert facts.bidder_bids_on_lot == 2
    assert facts.lot_total_bids == 10


def test_the_gap_to_the_previous_bid_is_computed_in_seconds() -> None:
    assert facts_from_context(context()).seconds_since_prev_bid == pytest.approx(300.0)


def test_an_unset_previous_bid_stays_none_all_the_way_into_the_features() -> None:
    message = context()
    message.ClearField("previous_bid_time")
    assert facts_from_context(message).seconds_since_prev_bid is None


def test_account_age_is_converted_to_days() -> None:
    assert facts_from_context(context()).account_age_days == pytest.approx(365.0)


def test_an_account_created_after_the_bid_is_clamped_to_zero_age() -> None:
    message = context()
    message.bidder_account_created.CopyFrom(ts(BID_TIME + timedelta(days=1)))
    assert facts_from_context(message).account_age_days == 0.0


def test_a_bid_arriving_before_the_previous_one_is_clamped_to_zero_gap() -> None:
    message = context()
    message.previous_bid_time.CopyFrom(ts(BID_TIME + timedelta(seconds=30)))
    assert facts_from_context(message).seconds_since_prev_bid == 0.0


def test_a_missing_bid_time_falls_back_to_now() -> None:
    message = context()
    message.ClearField("bid_time")
    message.ClearField("previous_bid_time")
    facts = facts_from_context(message)
    assert facts.account_age_days > 0.0


@pytest.mark.parametrize(
    ("level", "expected"),
    [
        (LEVEL_LOW, bidrisk_pb2.RISK_LEVEL_LOW),
        (LEVEL_MEDIUM, bidrisk_pb2.RISK_LEVEL_MEDIUM),
        (LEVEL_HIGH, bidrisk_pb2.RISK_LEVEL_HIGH),
        ("nonsense", bidrisk_pb2.RISK_LEVEL_UNSPECIFIED),
    ],
)
def test_levels_map_onto_the_contract_enum(level: str, expected: int) -> None:
    assert level_to_proto(level) == expected


def test_an_assessment_carries_the_score_reasons_and_timing() -> None:
    message = assessment_to_proto(
        Prediction(score=0.83, level=LEVEL_HIGH, reasons=["bid is 3.0× the current price"]),
        inference_ms=1.25,
        model_version="bidrisk-test",
    )
    assert message.score == pytest.approx(0.83)
    assert message.level == bidrisk_pb2.RISK_LEVEL_HIGH
    assert list(message.reasons) == ["bid is 3.0× the current price"]
    assert message.inference_ms == pytest.approx(1.25)
    assert message.model_version == "bidrisk-test"
