from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from bidrisk_ml.features import extract_matrix
from bidrisk_ml.model import build_pipeline, save_artifact
from bidrisk_ml.predictor import RiskPredictor
from bidrisk_ml.synth import generate

TEST_SEED = 7
LOW_MAX = 0.40
MEDIUM_MAX = 0.70


@pytest.fixture(scope="session")
def model_path(tmp_path_factory: pytest.TempPathFactory) -> Path:
    rows = generate(n_samples=600, seed=TEST_SEED, suspicious_rate=0.2, label_noise=0.0)
    x = extract_matrix([r.facts for r in rows])
    y = np.asarray([r.is_suspicious for r in rows], dtype=np.int64)

    pipeline = build_pipeline(TEST_SEED)
    pipeline.fit(x, y)

    path = tmp_path_factory.mktemp("models") / "test-model.joblib"
    save_artifact(
        path=path,
        pipeline=pipeline,
        model_version="test-model",
        trained_at="2026-01-01T00:00:00+00:00",
        reject_threshold=MEDIUM_MAX,
        metrics={},
    )
    return path


@pytest.fixture(scope="session")
def predictor(model_path: Path) -> RiskPredictor:
    return RiskPredictor(model_path, low_max=LOW_MAX, medium_max=MEDIUM_MAX)
