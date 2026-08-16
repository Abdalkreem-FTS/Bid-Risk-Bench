from __future__ import annotations

import pytest

from bidrisk_ml.features import BidFacts
from bidrisk_ml.predictor import (
    LEVEL_HIGH,
    LEVEL_LOW,
    LEVEL_MEDIUM,
    ModelArtifactError,
    RiskPredictor,
)
from bidrisk_ml.reasons import NOTHING_UNUSUAL

ORDINARY_BID = BidFacts(
    amount=105.0,
    current_price=100.0,
    seconds_since_prev_bid=600.0,
    bidder_bids_on_lot=1,
    lot_total_bids=8,
    account_age_days=800.0,
)

BLATANT_SHILL = BidFacts(
    amount=320.0,
    current_price=100.0,
    seconds_since_prev_bid=3.0,
    bidder_bids_on_lot=11,
    lot_total_bids=13,
    account_age_days=1.0,
)


def test_score_is_a_probability(predictor: RiskPredictor) -> None:
    prediction, _ = predictor.predict_one(ORDINARY_BID)
    assert 0.0 <= prediction.score <= 1.0


def test_inference_time_is_measured_and_plausible(predictor: RiskPredictor) -> None:
    _, inference_ms = predictor.predict_one(ORDINARY_BID)
    assert 0.0 < inference_ms < 100.0


def test_an_obvious_shill_outscores_an_ordinary_bid(predictor: RiskPredictor) -> None:
    shill, _ = predictor.predict_one(BLATANT_SHILL)
    ordinary, _ = predictor.predict_one(ORDINARY_BID)
    assert shill.score > ordinary.score


def test_reasons_cite_the_evidence_that_raised_the_score(predictor: RiskPredictor) -> None:
    prediction, _ = predictor.predict_one(BLATANT_SHILL)
    assert prediction.reasons
    assert NOTHING_UNUSUAL not in prediction.reasons
    assert any("current price" in reason for reason in prediction.reasons)


def test_an_unremarkable_bid_says_so_rather_than_inventing_a_reason(
    predictor: RiskPredictor,
) -> None:
    prediction, _ = predictor.predict_one(ORDINARY_BID)
    assert prediction.reasons == [NOTHING_UNUSUAL]


@pytest.mark.parametrize(
    ("score", "expected"),
    [
        (0.0, LEVEL_LOW),
        (0.39, LEVEL_LOW),
        (0.40, LEVEL_MEDIUM),
        (0.69, LEVEL_MEDIUM),
        (0.70, LEVEL_HIGH),
        (1.0, LEVEL_HIGH),
    ],
)
def test_level_boundaries_match_the_configured_thresholds(
    predictor: RiskPredictor, score: float, expected: str
) -> None:
    assert predictor.level_for(score) == expected


def test_batch_scores_match_scoring_one_at_a_time(predictor: RiskPredictor) -> None:
    bids = [ORDINARY_BID, BLATANT_SHILL, ORDINARY_BID]
    batched, _ = predictor.predict(bids)
    individually = [predictor.predict_one(b)[0] for b in bids]

    assert [p.score for p in batched] == pytest.approx([p.score for p in individually])
    assert [p.reasons for p in batched] == [p.reasons for p in individually]


def test_empty_batch_returns_nothing_rather_than_failing(predictor: RiskPredictor) -> None:
    predictions, inference_ms = predictor.predict([])
    assert predictions == []
    assert inference_ms == 0.0


def test_warm_up_runs_a_real_prediction(predictor: RiskPredictor) -> None:
    assert predictor.warm_up() > 0.0


def test_a_missing_model_file_names_the_command_that_creates_it(tmp_path: object) -> None:
    with pytest.raises(ModelArtifactError, match="make train"):
        RiskPredictor("/nonexistent/model.joblib", low_max=0.4, medium_max=0.7)


def test_a_model_trained_on_other_features_is_refused(
    predictor: RiskPredictor, model_path: object
) -> None:
    import joblib

    artifact = joblib.load(model_path)
    artifact["feature_names"] = ["amount_ratio", "something_we_no_longer_compute"]
    stale = str(model_path) + ".stale"
    joblib.dump(artifact, stale)

    with pytest.raises(ModelArtifactError, match="different features"):
        RiskPredictor(stale, low_max=0.4, medium_max=0.7)
