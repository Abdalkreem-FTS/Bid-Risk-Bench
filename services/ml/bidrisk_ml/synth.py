from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from bidrisk_ml.features import BidFacts

ARCHETYPE_NORMAL = "normal"
ARCHETYPE_SHILL = "shill_inflation"
ARCHETYPE_SNIPE = "snipe_anomaly"
ARCHETYPE_SELF = "self_competition"

SUSPICIOUS_ARCHETYPES = (ARCHETYPE_SHILL, ARCHETYPE_SNIPE, ARCHETYPE_SELF)


@dataclass(frozen=True)
class LabelledBid:
    facts: BidFacts
    is_suspicious: int
    archetype: str


def _normal_bid(rng: np.random.Generator) -> BidFacts:
    current_price = float(rng.uniform(10.0, 1_000.0))
    amount = current_price * float(rng.uniform(1.02, 1.12))
    seconds_since_prev = float(rng.lognormal(mean=5.7, sigma=1.4))
    lot_total_bids = int(rng.integers(3, 40))
    bidder_bids_on_lot = int(min(rng.integers(1, 5), lot_total_bids))
    account_age_days = float(rng.lognormal(mean=5.8, sigma=1.0))
    return BidFacts(
        amount=amount,
        current_price=current_price,
        seconds_since_prev_bid=seconds_since_prev,
        bidder_bids_on_lot=bidder_bids_on_lot,
        lot_total_bids=lot_total_bids,
        account_age_days=account_age_days,
    )


# Three shapes of suspicious bid, so "suspicious" is not one straight line in the data.
def _shill_bid(rng: np.random.Generator) -> BidFacts:
    current_price = float(rng.uniform(10.0, 1_000.0))
    amount = current_price * float(rng.uniform(1.25, 2.60))
    seconds_since_prev = float(rng.uniform(2.0, 90.0))
    bidder_bids_on_lot = int(rng.integers(4, 16))
    lot_total_bids = int(bidder_bids_on_lot / float(rng.uniform(0.55, 0.95)))
    account_age_days = float(rng.uniform(0.2, 14.0))
    return BidFacts(
        amount=amount,
        current_price=current_price,
        seconds_since_prev_bid=seconds_since_prev,
        bidder_bids_on_lot=bidder_bids_on_lot,
        lot_total_bids=max(lot_total_bids, bidder_bids_on_lot),
        account_age_days=account_age_days,
    )


def _snipe_bid(rng: np.random.Generator) -> BidFacts:
    current_price = float(rng.uniform(10.0, 1_000.0))
    amount = current_price * float(rng.uniform(1.30, 3.00))
    seconds_since_prev = float(rng.uniform(0.3, 6.0))
    lot_total_bids = int(rng.integers(5, 45))
    bidder_bids_on_lot = int(min(rng.integers(1, 6), lot_total_bids))
    account_age_days = float(rng.lognormal(mean=5.2, sigma=1.2))
    return BidFacts(
        amount=amount,
        current_price=current_price,
        seconds_since_prev_bid=seconds_since_prev,
        bidder_bids_on_lot=bidder_bids_on_lot,
        lot_total_bids=lot_total_bids,
        account_age_days=account_age_days,
    )


def _self_competition_bid(rng: np.random.Generator) -> BidFacts:
    current_price = float(rng.uniform(10.0, 1_000.0))
    amount = current_price * float(rng.uniform(1.03, 1.18))
    seconds_since_prev = float(rng.uniform(10.0, 240.0))
    bidder_bids_on_lot = int(rng.integers(6, 21))
    lot_total_bids = int(bidder_bids_on_lot / float(rng.uniform(0.65, 0.98)))
    account_age_days = float(rng.lognormal(mean=4.5, sigma=1.1))
    return BidFacts(
        amount=amount,
        current_price=current_price,
        seconds_since_prev_bid=seconds_since_prev,
        bidder_bids_on_lot=bidder_bids_on_lot,
        lot_total_bids=max(lot_total_bids, bidder_bids_on_lot),
        account_age_days=account_age_days,
    )


_ARCHETYPE_BUILDERS = {
    ARCHETYPE_SHILL: _shill_bid,
    ARCHETYPE_SNIPE: _snipe_bid,
    ARCHETYPE_SELF: _self_competition_bid,
}


def generate(
    n_samples: int,
    seed: int,
    suspicious_rate: float,
    label_noise: float,
) -> list[LabelledBid]:
    rng = np.random.default_rng(seed)
    rows: list[LabelledBid] = []

    for _ in range(n_samples):
        if rng.random() < suspicious_rate:
            archetype = str(rng.choice(SUSPICIOUS_ARCHETYPES))
            facts = _ARCHETYPE_BUILDERS[archetype](rng)
            label = 1
        else:
            archetype = ARCHETYPE_NORMAL
            facts = _normal_bid(rng)
            label = 0

        if rng.random() < label_noise:
            label = 1 - label

        rows.append(LabelledBid(facts=facts, is_suspicious=label, archetype=archetype))

    for i in rng.choice(len(rows), size=max(1, len(rows) // 100), replace=False):
        row = rows[int(i)]
        rows[int(i)] = LabelledBid(
            facts=BidFacts(
                amount=row.facts.amount,
                current_price=row.facts.current_price,
                seconds_since_prev_bid=None,
                bidder_bids_on_lot=1,
                lot_total_bids=1,
                account_age_days=row.facts.account_age_days,
            ),
            is_suspicious=row.is_suspicious,
            archetype=row.archetype,
        )

    return rows


CSV_COLUMNS = (
    "amount",
    "current_price",
    "seconds_since_prev_bid",
    "bidder_bids_on_lot",
    "lot_total_bids",
    "account_age_days",
    "archetype",
    "is_suspicious",
)


def to_csv_row(row: LabelledBid) -> list[str]:
    f = row.facts
    gap = "" if f.seconds_since_prev_bid is None else f"{f.seconds_since_prev_bid:.3f}"
    return [
        f"{f.amount:.2f}",
        f"{f.current_price:.2f}",
        gap,
        str(f.bidder_bids_on_lot),
        str(f.lot_total_bids),
        f"{f.account_age_days:.3f}",
        row.archetype,
        str(row.is_suspicious),
    ]


def from_csv_row(row: dict[str, str]) -> LabelledBid:
    gap_raw = row["seconds_since_prev_bid"].strip()
    return LabelledBid(
        facts=BidFacts(
            amount=float(row["amount"]),
            current_price=float(row["current_price"]),
            seconds_since_prev_bid=float(gap_raw) if gap_raw else None,
            bidder_bids_on_lot=int(row["bidder_bids_on_lot"]),
            lot_total_bids=int(row["lot_total_bids"]),
            account_age_days=float(row["account_age_days"]),
        ),
        is_suspicious=int(row["is_suspicious"]),
        archetype=row["archetype"],
    )
