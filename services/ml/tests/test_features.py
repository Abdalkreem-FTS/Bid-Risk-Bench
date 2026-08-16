from __future__ import annotations

import math

import pytest

from bidrisk_ml.features import (
    AMOUNT_RATIO_LOG_CAP,
    FEATURE_NAMES,
    MIN_PRICE,
    SECONDS_SINCE_PREV_CAP,
    BidFacts,
    extract,
    extract_matrix,
)


def facts(**overrides: object) -> BidFacts:
    defaults: dict[str, object] = {
        "amount": 110.0,
        "current_price": 100.0,
        "seconds_since_prev_bid": 300.0,
        "bidder_bids_on_lot": 2,
        "lot_total_bids": 10,
        "account_age_days": 400.0,
    }
    defaults.update(overrides)
    return BidFacts(**defaults)  # type: ignore[arg-type]


def feature(name: str, f: BidFacts) -> float:
    return extract(f)[FEATURE_NAMES.index(name)]


def test_feature_order_is_the_contract_between_training_and_serving() -> None:
    assert extract(facts()) == pytest.approx(
        [
            math.log(1.1),
            math.log1p(300.0),
            2.0,
            math.log1p(400.0),
            0.2,
        ]
    )
    assert len(FEATURE_NAMES) == len(extract(facts()))


def test_amount_ratio_measures_the_jump_over_the_current_price() -> None:
    value = feature("log_amount_ratio", facts(amount=300.0, current_price=100.0))
    assert value == pytest.approx(math.log(3.0))


def test_amount_ratio_is_capped_so_the_logit_cannot_run_away() -> None:
    # The logit is linear in the features. Left unbounded, a 1000x bid produces a logit in the
    # thousands and a score of exactly 1.0, from a region the model was never fitted on.
    value = feature("log_amount_ratio", facts(amount=99_999.0, current_price=100.0))
    assert value == pytest.approx(AMOUNT_RATIO_LOG_CAP)


def test_amount_ratio_survives_a_zero_current_price() -> None:
    value = feature("log_amount_ratio", facts(amount=50.0, current_price=0.0))
    assert value == pytest.approx(min(math.log(50.0 / MIN_PRICE), AMOUNT_RATIO_LOG_CAP))
    assert math.isfinite(value)


def test_first_bid_on_a_lot_is_treated_as_the_longest_possible_gap() -> None:
    first_bid = feature("log_seconds_since_prev", facts(seconds_since_prev_bid=None))
    assert first_bid == pytest.approx(math.log1p(SECONDS_SINCE_PREV_CAP))
    assert first_bid > feature("log_seconds_since_prev", facts(seconds_since_prev_bid=2.0))


def test_gaps_are_capped_so_one_idle_lot_cannot_dominate_the_scale() -> None:
    a_week = feature("log_seconds_since_prev", facts(seconds_since_prev_bid=7 * 86_400.0))
    a_day = feature("log_seconds_since_prev", facts(seconds_since_prev_bid=86_400.0))
    assert a_week == pytest.approx(a_day)


def test_an_instant_rebid_is_well_defined() -> None:
    assert feature("log_seconds_since_prev", facts(seconds_since_prev_bid=0.0)) == 0.0


def test_negative_clock_skew_does_not_produce_a_negative_gap() -> None:
    assert feature("log_seconds_since_prev", facts(seconds_since_prev_bid=-5.0)) == 0.0


def test_a_brand_new_account_scores_zero_age_not_minus_infinity() -> None:
    assert feature("log_account_age_days", facts(account_age_days=0.0)) == 0.0
    assert feature("log_account_age_days", facts(account_age_days=-3.0)) == 0.0


def test_bidder_lot_share_is_the_fraction_of_the_lot_the_bidder_owns() -> None:
    assert feature(
        "bidder_lot_share", facts(bidder_bids_on_lot=9, lot_total_bids=10)
    ) == pytest.approx(0.9)


def test_bidder_lot_share_handles_an_empty_lot_and_never_exceeds_one() -> None:
    assert feature("bidder_lot_share", facts(bidder_bids_on_lot=0, lot_total_bids=0)) == 0.0
    assert feature("bidder_lot_share", facts(bidder_bids_on_lot=5, lot_total_bids=3)) == 1.0


def test_extract_matrix_shape_matches_the_batch() -> None:
    matrix = extract_matrix([facts(), facts(amount=200.0)])
    assert matrix.shape == (2, len(FEATURE_NAMES))


def test_extract_matrix_of_nothing_still_has_the_right_width() -> None:
    assert extract_matrix([]).shape == (0, len(FEATURE_NAMES))
