from __future__ import annotations

from bidrisk_ml.features import BidFacts

MIN_CONTRIBUTION = 0.05
MAX_REASONS = 3

NOTHING_UNUSUAL = "nothing unusual about this bid"


def _phrase(feature: str, facts: BidFacts) -> str | None:
    if feature == "log_amount_ratio":
        ratio = facts.amount / max(facts.current_price, 0.01)
        return f"bid is {ratio:.1f}× the current price"

    if feature == "log_seconds_since_prev":
        gap = facts.seconds_since_prev_bid
        if gap is None:
            return None
        if gap < 60:
            return f"only {gap:.0f} seconds since the previous bid"
        return f"only {gap / 60:.0f} minutes since the previous bid"

    if feature == "bidder_bids_on_lot":
        return f"bidder has already placed {facts.bidder_bids_on_lot} bids on this lot"

    if feature == "log_account_age_days":
        days = facts.account_age_days
        if days < 1:
            return "account was created today"
        whole_days = round(days)
        return f"account is {whole_days} day{'' if whole_days == 1 else 's'} old"

    if feature == "bidder_lot_share":
        share = facts.bidder_bids_on_lot / max(facts.lot_total_bids, 1)
        return f"bidder placed {share:.0%} of all bids on this lot"

    return None


def build(facts: BidFacts, contributions: dict[str, float]) -> list[str]:
    ranked = sorted(
        ((name, value) for name, value in contributions.items() if value >= MIN_CONTRIBUTION),
        key=lambda item: item[1],
        reverse=True,
    )

    reasons = []
    for name, _ in ranked[:MAX_REASONS]:
        phrase = _phrase(name, facts)
        if phrase is not None:
            reasons.append(phrase)

    return reasons or [NOTHING_UNUSUAL]
