from __future__ import annotations

from pathlib import Path
from typing import Any

import joblib
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from bidrisk_ml.features import FEATURE_NAMES


def build_pipeline(seed: int) -> Pipeline:
    return Pipeline(
        [
            ("scaler", StandardScaler()),
            (
                "model",
                LogisticRegression(
                    max_iter=1_000,
                    random_state=seed,
                ),
            ),
        ]
    )


def save_artifact(
    path: Path,
    pipeline: Pipeline,
    model_version: str,
    trained_at: str,
    reject_threshold: float,
    metrics: dict[str, Any],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    joblib.dump(
        {
            "pipeline": pipeline,
            "feature_names": list(FEATURE_NAMES),
            "model_version": model_version,
            "trained_at": trained_at,
            "reject_threshold": reject_threshold,
            "metrics": metrics,
        },
        path,
    )
