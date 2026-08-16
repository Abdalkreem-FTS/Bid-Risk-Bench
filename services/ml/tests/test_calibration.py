from __future__ import annotations

import math

import numpy as np
import pytest

from bidrisk_ml.features import AMOUNT_RATIO_LOG_CAP, FEATURE_NAMES, BidFacts, extract
from bidrisk_ml.model import build_pipeline

SEED = 42


def _dataset(n: int = 4_000, positive_rate: float = 0.2) -> tuple[np.ndarray, np.ndarray]:
    rng = np.random.default_rng(SEED)
    y = (rng.random(n) < positive_rate).astype(np.int64)
    rows = []
    for label in y:
        if label:
            facts = BidFacts(
                amount=float(rng.uniform(150, 300)),
                current_price=100.0,
                seconds_since_prev_bid=float(rng.uniform(1, 90)),
                bidder_bids_on_lot=int(rng.integers(4, 20)),
                lot_total_bids=20,
                account_age_days=float(rng.uniform(0, 20)),
            )
        else:
            facts = BidFacts(
                amount=float(rng.uniform(101, 140)),
                current_price=100.0,
                seconds_since_prev_bid=float(rng.uniform(120, 5_000)),
                bidder_bids_on_lot=int(rng.integers(1, 4)),
                lot_total_bids=20,
                account_age_days=float(rng.uniform(60, 2_000)),
            )
        rows.append(extract(facts))
    return np.asarray(rows, dtype=np.float64), y


def test_predicted_probabilities_match_the_true_rate() -> None:
    x, y = _dataset()
    pipeline = build_pipeline(SEED)
    pipeline.fit(x, y)

    proba = pipeline.predict_proba(x)[:, 1]
    assert proba.mean() == pytest.approx(y.mean(), rel=0.10)


def test_a_wild_bid_does_not_run_the_logit_off_the_end_of_the_scale() -> None:
    wild = extract(
        BidFacts(
            amount=99_999.0,
            current_price=100.0,
            seconds_since_prev_bid=120.0,
            bidder_bids_on_lot=1,
            lot_total_bids=5,
            account_age_days=365.0,
        )
    )
    index = FEATURE_NAMES.index("log_amount_ratio")
    assert wild[index] == pytest.approx(AMOUNT_RATIO_LOG_CAP)
    assert wild[index] < math.log(1_000.0)


def test_the_cap_still_ranks_a_worse_bid_higher() -> None:
    def ratio_feature(amount: float) -> float:
        facts = BidFacts(
            amount=amount,
            current_price=100.0,
            seconds_since_prev_bid=120.0,
            bidder_bids_on_lot=1,
            lot_total_bids=5,
            account_age_days=365.0,
        )
        return extract(facts)[FEATURE_NAMES.index("log_amount_ratio")]

    assert ratio_feature(110.0) < ratio_feature(200.0) < ratio_feature(500.0)
