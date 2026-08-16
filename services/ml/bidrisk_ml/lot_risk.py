from __future__ import annotations

from bidrisk import bidrisk_pb2

from bidrisk_ml.predictor import level_for
from bidrisk_ml.proto_adapter import level_to_proto


def aggregate(
    lot_id: int,
    bids: list[bidrisk_pb2.Bid],
    low_max: float,
    medium_max: float,
) -> bidrisk_pb2.LotRisk:
    scores = [bid.risk_score for bid in bids if bid.risk_score > 0.0]
    if not scores:
        return bidrisk_pb2.LotRisk(
            lot_id=lot_id,
            level=bidrisk_pb2.RISK_LEVEL_UNSPECIFIED,
            max_score=0.0,
            mean_score=0.0,
            scored_bids=0,
        )

    highest = max(scores)
    return bidrisk_pb2.LotRisk(
        lot_id=lot_id,
        level=level_to_proto(level_for(highest, low_max, medium_max)),
        max_score=highest,
        mean_score=sum(scores) / len(scores),
        scored_bids=len(scores),
    )
