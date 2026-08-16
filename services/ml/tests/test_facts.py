from __future__ import annotations

from datetime import UTC, datetime, timedelta

from bidrisk import bidrisk_pb2
from google.protobuf.timestamp_pb2 import Timestamp

from bidrisk_ml import facts as facts_mod
from bidrisk_ml.grounding import unsupported_numbers

NOW = datetime(2026, 8, 13, 12, 0, 0, tzinfo=UTC)


def ts(moment: datetime) -> Timestamp:
    stamp = Timestamp()
    stamp.FromDatetime(moment)
    return stamp


def lot(**overrides: object) -> bidrisk_pb2.Lot:
    message = bidrisk_pb2.Lot(
        id=42,
        title="Victorian mahogany writing desk #42",
        reserve_price=245.74,
        current_price=717.08,
        bid_count=15,
        closed=False,
    )
    message.closes_at.CopyFrom(ts(NOW + timedelta(hours=6)))
    for field, value in overrides.items():
        setattr(message, field, value)
    return message


def bid(bid_id: int, amount: float, minutes_ago: float, score: float = 0.2) -> bidrisk_pb2.Bid:
    message = bidrisk_pb2.Bid(
        id=bid_id,
        lot_id=42,
        bidder_id=100 + bid_id,
        amount=amount,
        risk_score=score,
        risk_level=bidrisk_pb2.RISK_LEVEL_LOW,
        accepted=True,
    )
    message.placed_at.CopyFrom(ts(NOW - timedelta(minutes=minutes_ago)))
    return message


HISTORY = [bid(i, 700.0 - i * 10, minutes_ago=i * 5.0) for i in range(1, 13)]


def test_a_lot_summary_states_the_lot_and_its_price() -> None:
    rendered = facts_mod.lot_summary(lot(), HISTORY, NOW, shown=3).render()
    assert "lot.id = 42" in rendered
    assert "lot.current_price_usd = 717.08" in rendered
    assert "lot.total_bids = 15" in rendered
    assert "lot.status = open" in rendered


def test_a_closed_lot_says_how_long_ago_it_closed() -> None:
    closed = lot(closed=True)
    closed.closes_at.CopyFrom(ts(NOW - timedelta(hours=2)))
    rendered = facts_mod.lot_summary(closed, HISTORY, NOW, shown=2).render()
    assert "lot.status = closed" in rendered
    assert "lot.closed_hours_ago = 2.0" in rendered


def test_only_the_requested_number_of_bids_is_listed() -> None:
    rendered = facts_mod.lot_summary(lot(), HISTORY, NOW, shown=3).render()
    assert "bids.recent_c.bid_id" in rendered
    assert "bids.recent_d.bid_id" not in rendered


def test_the_block_size_does_not_grow_with_a_busier_lot() -> None:
    busy = facts_mod.lot_summary(lot(), HISTORY, NOW, shown=4).render()
    busier = facts_mod.lot_summary(lot(), HISTORY * 3, NOW, shown=4).render()
    assert busy.count("recent") == busier.count("recent")


def test_aggregates_are_computed_over_the_whole_window_not_just_what_is_shown() -> None:
    rendered = facts_mod.lot_summary(lot(), HISTORY, NOW, shown=2).render()
    assert f"bids.count_in_window = {len(HISTORY)}" in rendered
    assert "bids.average_gap_seconds" in rendered


def test_a_lot_with_no_bids_says_so_instead_of_omitting_it() -> None:
    rendered = facts_mod.lot_summary(lot(bid_count=0), [], NOW, shown=5).render()
    assert "bids.count = 0" in rendered


def test_why_flagged_quotes_the_models_own_reasons() -> None:
    flagged = bid(99, 2400.0, minutes_ago=1.0, score=0.97)
    flagged.risk_reasons.extend(["bid is 3.2× the current price", "account is 1 day old"])
    rendered = facts_mod.why_flagged(flagged, lot(), NOW, previous=HISTORY[0]).render()

    assert "bid.risk_score = 0.97" in rendered
    assert "bid.model_reason_a = bid is 3.2× the current price" in rendered
    assert "bid.model_reason_b = account is 1 day old" in rendered
    assert "bid.seconds_after_previous_bid" in rendered
    assert "bid.times_the_previous_bid" in rendered


def test_why_flagged_works_for_the_first_bid_on_a_lot() -> None:
    rendered = facts_mod.why_flagged(bid(1, 100.0, 5.0), lot(), NOW, previous=None).render()
    assert "bid.seconds_after_previous_bid" not in rendered


def test_the_ranking_block_lists_each_lot_with_its_scores() -> None:
    risk = bidrisk_pb2.LotRisk(
        lot_id=42,
        level=bidrisk_pb2.RISK_LEVEL_HIGH,
        max_score=0.94,
        mean_score=0.31,
        scored_bids=12,
    )
    rendered = facts_mod.most_suspicious([(lot(), risk)], NOW).render()
    assert "rank_a.highest_risk_score = 0.94" in rendered
    assert "rank_a.risk_level = RISK_LEVEL_HIGH" in rendered
    assert "rank_a.scored_bids = 12" in rendered


def test_the_overview_counts_what_is_there_without_inventing_detail() -> None:
    rendered = facts_mod.overview([lot(), lot(id=43, closed=True)], NOW).render()
    assert "auction.lots_known = 2" in rendered
    assert "auction.lots_open = 1" in rendered


def test_no_key_contains_a_digit() -> None:
    flagged = bid(99, 2400.0, minutes_ago=1.0, score=0.97)
    flagged.risk_reasons.extend(["bid is 3.2× the current price"])
    risk = bidrisk_pb2.LotRisk(lot_id=42, max_score=0.94, mean_score=0.31, scored_bids=12)

    blocks = [
        facts_mod.lot_summary(lot(), HISTORY, NOW, shown=5),
        facts_mod.why_flagged(flagged, lot(), NOW, previous=HISTORY[0]),
        facts_mod.most_suspicious([(lot(), risk)] * 3, NOW),
        facts_mod.overview([lot()], NOW),
    ]

    for block in blocks:
        for line in block.lines:
            key = line.split(" = ", 1)[0]
            assert not any(
                character.isdigit() for character in key
            ), f"key {key!r} contains a digit the model can mistake for data"


def test_an_answer_quoting_the_block_passes_the_guardrail() -> None:
    rendered = facts_mod.lot_summary(lot(), HISTORY, NOW, shown=3).render()
    answer = "Lot 42 stands at 717.08 after 15 bids, and closes in 6.0 hours."
    assert unsupported_numbers(answer, rendered) == []
