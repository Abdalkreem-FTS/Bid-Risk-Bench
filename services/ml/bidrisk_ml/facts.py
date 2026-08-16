from __future__ import annotations

from dataclasses import dataclass, field
from datetime import UTC, datetime

from bidrisk import bidrisk_pb2

# Flat `key = value` lines, not prose. Small models copy a labelled number more reliably
# than one buried in a sentence.
HEADER = "FACTS (the only data you may use; every number in your answer must appear here)"

# Keys use letters, not digits. A digit in a key looks like data to the grounding check.
_LETTERS = "abcdefghijklmnopqrstuvwxyz"


def _ordinal(index: int) -> str:
    return _LETTERS[index % len(_LETTERS)]


@dataclass
class FactsBlock:
    lines: list[str] = field(default_factory=list)

    def add(self, key: str, value: object) -> None:
        self.lines.append(f"{key} = {value}")

    def add_number(self, key: str, value: float, decimals: int = 2) -> None:
        self.lines.append(f"{key} = {value:.{decimals}f}")

    def render(self) -> str:
        return HEADER + "\n" + "\n".join(self.lines)


def _lot_lines(block: FactsBlock, lot: bidrisk_pb2.Lot, now: datetime, prefix: str) -> None:
    block.add(f"{prefix}.id", lot.id)
    block.add(f"{prefix}.title", lot.title)
    block.add_number(f"{prefix}.reserve_price_usd", lot.reserve_price)
    block.add_number(f"{prefix}.current_price_usd", lot.current_price)
    block.add(f"{prefix}.total_bids", lot.bid_count)
    block.add(f"{prefix}.status", "closed" if lot.closed else "open")

    closes_at = lot.closes_at.ToDatetime(tzinfo=UTC)
    hours = (closes_at - now).total_seconds() / 3600.0
    if hours >= 0:
        block.add_number(f"{prefix}.closes_in_hours", hours, decimals=1)
    else:
        block.add_number(f"{prefix}.closed_hours_ago", -hours, decimals=1)


def _bid_lines(
    block: FactsBlock, bids: list[bidrisk_pb2.Bid], now: datetime, shown: int, prefix: str
) -> None:
    if not bids:
        block.add(f"{prefix}.count", 0)
        return

    scored = [b.risk_score for b in bids if b.risk_score > 0]
    block.add(f"{prefix}.count_in_window", len(bids))
    block.add(f"{prefix}.accepted_in_window", sum(1 for b in bids if b.accepted))
    block.add(f"{prefix}.rejected_in_window", sum(1 for b in bids if not b.accepted))
    block.add(f"{prefix}.distinct_bidders_in_window", len({b.bidder_id for b in bids}))

    if scored:
        block.add_number(f"{prefix}.highest_risk_score", max(scored))
        block.add_number(f"{prefix}.average_risk_score", sum(scored) / len(scored))

    times = [b.placed_at.ToDatetime(tzinfo=UTC) for b in bids]
    if len(times) > 1:
        gaps = [(times[i] - times[i + 1]).total_seconds() for i in range(len(times) - 1)]
        block.add_number(f"{prefix}.shortest_gap_seconds", min(gaps), decimals=0)
        block.add_number(f"{prefix}.average_gap_seconds", sum(gaps) / len(gaps), decimals=0)

    for index, bid in enumerate(bids[:shown]):
        placed = bid.placed_at.ToDatetime(tzinfo=UTC)
        label = f"{prefix}.recent_{_ordinal(index)}"
        block.add(f"{label}.bid_id", bid.id)
        block.add(f"{label}.bidder_id", bid.bidder_id)
        block.add_number(f"{label}.amount_usd", bid.amount)
        block.add_number(f"{label}.minutes_ago", (now - placed).total_seconds() / 60.0, decimals=1)
        block.add(f"{label}.accepted", "yes" if bid.accepted else "no")
        if bid.risk_score > 0:
            block.add_number(f"{label}.risk_score", bid.risk_score)


def lot_summary(
    lot: bidrisk_pb2.Lot, bids: list[bidrisk_pb2.Bid], now: datetime, shown: int
) -> FactsBlock:
    block = FactsBlock()
    _lot_lines(block, lot, now, "lot")
    _bid_lines(block, bids, now, shown, "bids")
    return block


def why_flagged(
    bid: bidrisk_pb2.Bid, lot: bidrisk_pb2.Lot, now: datetime, previous: bidrisk_pb2.Bid | None
) -> FactsBlock:
    block = FactsBlock()
    block.add("bid.id", bid.id)
    block.add("bid.bidder_id", bid.bidder_id)
    block.add_number("bid.amount_usd", bid.amount)
    block.add("bid.accepted", "yes" if bid.accepted else "no")
    if bid.reject_reason != bidrisk_pb2.REJECT_REASON_UNSPECIFIED:
        block.add("bid.reject_reason", bidrisk_pb2.RejectReason.Name(bid.reject_reason))
    block.add_number("bid.risk_score", bid.risk_score)
    block.add("bid.risk_level", bidrisk_pb2.RiskLevel.Name(bid.risk_level))

    for index, reason in enumerate(bid.risk_reasons):
        block.add(f"bid.model_reason_{_ordinal(index)}", reason)

    placed = bid.placed_at.ToDatetime(tzinfo=UTC)
    block.add_number("bid.minutes_ago", (now - placed).total_seconds() / 60.0, decimals=1)

    if previous is not None:
        previous_at = previous.placed_at.ToDatetime(tzinfo=UTC)
        block.add_number(
            "bid.seconds_after_previous_bid", (placed - previous_at).total_seconds(), decimals=0
        )
        block.add_number("bid.previous_bid_amount_usd", previous.amount)
        block.add_number(
            "bid.times_the_previous_bid",
            bid.amount / previous.amount if previous.amount else 0.0,
        )

    _lot_lines(block, lot, now, "lot")
    return block


def most_suspicious(
    ranked: list[tuple[bidrisk_pb2.Lot, bidrisk_pb2.LotRisk]], now: datetime
) -> FactsBlock:
    block = FactsBlock()
    block.add("lots_ranked", len(ranked))
    for position, (lot, risk) in enumerate(ranked):
        prefix = f"rank_{_ordinal(position)}"
        _lot_lines(block, lot, now, prefix)
        block.add_number(f"{prefix}.highest_risk_score", risk.max_score)
        block.add_number(f"{prefix}.average_risk_score", risk.mean_score)
        block.add(f"{prefix}.risk_level", bidrisk_pb2.RiskLevel.Name(risk.level))
        block.add(f"{prefix}.scored_bids", risk.scored_bids)
    return block


def overview(lots: list[bidrisk_pb2.Lot], now: datetime) -> FactsBlock:
    block = FactsBlock()
    open_lots = [lot for lot in lots if not lot.closed]
    block.add("auction.lots_known", len(lots))
    block.add("auction.lots_open", len(open_lots))
    block.add("auction.total_bids", sum(lot.bid_count for lot in lots))
    if open_lots:
        block.add_number(
            "auction.highest_current_price_usd", max(lot.current_price for lot in open_lots)
        )
        soonest = min(open_lots, key=lambda lot: lot.closes_at.ToDatetime(tzinfo=UTC))
        _lot_lines(block, soonest, now, "auction.closing_soonest")
    return block
