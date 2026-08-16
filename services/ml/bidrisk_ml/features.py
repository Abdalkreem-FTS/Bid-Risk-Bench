from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
import numpy.typing as npt

# Imported by train.py and by the service, so training and serving cannot disagree.
FEATURE_NAMES: tuple[str, ...] = (
    "log_amount_ratio",
    "log_seconds_since_prev",
    "bidder_bids_on_lot",
    "log_account_age_days",
    "bidder_lot_share",
)

SECONDS_SINCE_PREV_CAP = 86_400.0

MIN_PRICE = 0.01

AMOUNT_RATIO_LOG_CAP = math.log(10.0)
MIN_AMOUNT_RATIO = 0.01


@dataclass(frozen=True)
class BidFacts:
    amount: float
    current_price: float
    seconds_since_prev_bid: float | None
    bidder_bids_on_lot: int
    lot_total_bids: int
    account_age_days: float


def extract(facts: BidFacts) -> list[float]:
    price = max(facts.current_price, MIN_PRICE)
    amount_ratio = max(facts.amount / price, MIN_AMOUNT_RATIO)
    log_amount_ratio = min(math.log(amount_ratio), AMOUNT_RATIO_LOG_CAP)

    gap = (
        SECONDS_SINCE_PREV_CAP
        if facts.seconds_since_prev_bid is None
        else facts.seconds_since_prev_bid
    )
    gap = min(max(gap, 0.0), SECONDS_SINCE_PREV_CAP)
    log_seconds_since_prev = math.log1p(gap)

    log_account_age_days = math.log1p(max(facts.account_age_days, 0.0))

    total = max(facts.lot_total_bids, 1)
    bidder_lot_share = min(facts.bidder_bids_on_lot / total, 1.0)

    return [
        log_amount_ratio,
        log_seconds_since_prev,
        float(facts.bidder_bids_on_lot),
        log_account_age_days,
        bidder_lot_share,
    ]


def extract_matrix(facts: list[BidFacts]) -> npt.NDArray[np.float64]:
    if not facts:
        return np.empty((0, len(FEATURE_NAMES)), dtype=np.float64)
    return np.asarray([extract(f) for f in facts], dtype=np.float64)
