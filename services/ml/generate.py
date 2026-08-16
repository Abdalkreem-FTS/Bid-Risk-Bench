#!/usr/bin/env python3

from __future__ import annotations

import argparse
import csv
import os
from collections import Counter
from pathlib import Path

from bidrisk_ml.synth import CSV_COLUMNS, generate, to_csv_row

DEFAULT_OUT = Path(__file__).resolve().parents[2] / "data" / "synthetic_bids.csv"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--samples", type=int, default=int(os.getenv("TRAIN_SAMPLES", "5000")))
    parser.add_argument("--seed", type=int, default=int(os.getenv("TRAIN_SEED", "42")))
    parser.add_argument(
        "--suspicious-rate", type=float, default=float(os.getenv("TRAIN_SUSPICIOUS_RATE", "0.15"))
    )
    parser.add_argument(
        "--label-noise", type=float, default=float(os.getenv("TRAIN_LABEL_NOISE", "0.05"))
    )
    args = parser.parse_args()

    rows = generate(
        n_samples=args.samples,
        seed=args.seed,
        suspicious_rate=args.suspicious_rate,
        label_noise=args.label_noise,
    )

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        writer.writerow(CSV_COLUMNS)
        writer.writerows(to_csv_row(r) for r in rows)

    labels = Counter(r.is_suspicious for r in rows)
    archetypes = Counter(r.archetype for r in rows)
    print(f"✓ wrote {len(rows)} bids to {args.out}")
    print(
        f"  labels     : {labels[0]} clean · {labels[1]} suspicious ({labels[1] / len(rows):.1%})"
    )
    print("  archetypes : " + " · ".join(f"{k}={v}" for k, v in sorted(archetypes.items())))


if __name__ == "__main__":
    main()
