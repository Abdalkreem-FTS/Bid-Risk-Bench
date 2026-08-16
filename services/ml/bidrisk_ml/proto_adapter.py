from __future__ import annotations

from datetime import UTC, datetime

from bidrisk import bidrisk_pb2

from bidrisk_ml.features import BidFacts
from bidrisk_ml.predictor import LEVEL_HIGH, LEVEL_LOW, LEVEL_MEDIUM, Prediction

SECONDS_PER_DAY = 86_400.0

_LEVEL_TO_PROTO = {
    LEVEL_LOW: bidrisk_pb2.RISK_LEVEL_LOW,
    LEVEL_MEDIUM: bidrisk_pb2.RISK_LEVEL_MEDIUM,
    LEVEL_HIGH: bidrisk_pb2.RISK_LEVEL_HIGH,
}


def level_to_proto(level: str) -> bidrisk_pb2.RiskLevel.ValueType:
    return _LEVEL_TO_PROTO.get(level, bidrisk_pb2.RISK_LEVEL_UNSPECIFIED)


def facts_from_context(context: bidrisk_pb2.BidContext) -> BidFacts:
    bid_time = (
        context.bid_time.ToDatetime(tzinfo=UTC)
        if context.HasField("bid_time")
        else datetime.now(UTC)
    )

    seconds_since_prev: float | None = None
    if context.HasField("previous_bid_time"):
        previous = context.previous_bid_time.ToDatetime(tzinfo=UTC)
        seconds_since_prev = max((bid_time - previous).total_seconds(), 0.0)

    account_age_days = 0.0
    if context.HasField("bidder_account_created"):
        created = context.bidder_account_created.ToDatetime(tzinfo=UTC)
        account_age_days = max((bid_time - created).total_seconds(), 0.0) / SECONDS_PER_DAY

    return BidFacts(
        amount=context.amount,
        current_price=context.current_price,
        seconds_since_prev_bid=seconds_since_prev,
        bidder_bids_on_lot=context.bidder_bids_on_lot,
        lot_total_bids=context.lot_total_bids,
        account_age_days=account_age_days,
    )


def assessment_to_proto(
    prediction: Prediction, inference_ms: float, model_version: str
) -> bidrisk_pb2.RiskAssessment:
    return bidrisk_pb2.RiskAssessment(
        score=prediction.score,
        level=level_to_proto(prediction.level),
        reasons=prediction.reasons,
        inference_ms=inference_ms,
        model_version=model_version,
    )
