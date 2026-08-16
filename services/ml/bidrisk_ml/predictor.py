from __future__ import annotations

import time
from dataclasses import dataclass
from pathlib import Path

import joblib
import numpy as np
import numpy.typing as npt

from bidrisk_ml import reasons as reasons_mod
from bidrisk_ml.features import FEATURE_NAMES, BidFacts, extract_matrix

LEVEL_LOW = "LOW"
LEVEL_MEDIUM = "MEDIUM"
LEVEL_HIGH = "HIGH"


@dataclass(frozen=True)
class Prediction:
    score: float
    level: str
    reasons: list[str]


class ModelArtifactError(RuntimeError):
    """The model file is missing, unreadable, or does not match this build of the code."""


# A free function so lot_risk.py buckets a whole lot with exactly these thresholds.
def level_for(score: float, low_max: float, medium_max: float) -> str:
    if score < low_max:
        return LEVEL_LOW
    if score < medium_max:
        return LEVEL_MEDIUM
    return LEVEL_HIGH


# The model is loaded once at startup and kept in memory. Never per request.
class RiskPredictor:
    def __init__(self, artifact_path: str | Path, low_max: float, medium_max: float) -> None:
        path = Path(artifact_path)
        if not path.exists():
            raise ModelArtifactError(
                f"no model at {path}. The model is a build artifact, not source — run `make train`."
            )

        artifact = joblib.load(path)
        stored_features = tuple(artifact.get("feature_names", ()))
        if stored_features != FEATURE_NAMES:
            raise ModelArtifactError(
                "model was trained on different features than this code computes.\n"
                f"  model: {stored_features}\n  code : {FEATURE_NAMES}\nRun `make train`."
            )

        self._pipeline = artifact["pipeline"]
        self._scaler = self._pipeline.named_steps["scaler"]
        self._coefficients: npt.NDArray[np.float64] = self._pipeline.named_steps["model"].coef_[0]
        self.model_version: str = artifact["model_version"]
        self.trained_at: str = artifact["trained_at"]
        self._low_max = low_max
        self._medium_max = medium_max

    def level_for(self, score: float) -> str:
        return level_for(score, self._low_max, self._medium_max)

    # One throwaway prediction so the first real request does not pay for lazy setup.
    def warm_up(self) -> float:
        sample = BidFacts(
            amount=110.0,
            current_price=100.0,
            seconds_since_prev_bid=120.0,
            bidder_bids_on_lot=1,
            lot_total_bids=5,
            account_age_days=365.0,
        )
        started = time.perf_counter()
        self._pipeline.predict_proba(extract_matrix([sample]))
        return (time.perf_counter() - started) * 1000.0

    # Only predict_proba is timed. That number is the model time the report quotes.
    def predict(self, facts: list[BidFacts]) -> tuple[list[Prediction], float]:
        if not facts:
            return [], 0.0

        matrix = extract_matrix(facts)

        started = time.perf_counter()
        probabilities = self._pipeline.predict_proba(matrix)[:, 1]
        inference_ms = (time.perf_counter() - started) * 1000.0

        scaled = self._scaler.transform(matrix)
        contributions = scaled * self._coefficients

        predictions = [
            Prediction(
                score=float(probability),
                level=self.level_for(float(probability)),
                reasons=reasons_mod.build(
                    fact, dict(zip(FEATURE_NAMES, row.tolist(), strict=True))
                ),
            )
            for probability, fact, row in zip(probabilities, facts, contributions, strict=True)
        ]
        return predictions, inference_ms

    def predict_one(self, facts: BidFacts) -> tuple[Prediction, float]:
        predictions, inference_ms = self.predict([facts])
        return predictions[0], inference_ms
