from __future__ import annotations

import re
from dataclasses import dataclass
from enum import Enum


class Intent(str, Enum):
    LOT_SUMMARY = "lot_summary"
    WHY_FLAGGED = "why_flagged"
    MOST_SUSPICIOUS = "most_suspicious"
    OVERVIEW = "overview"


@dataclass(frozen=True)
class Question:
    text: str
    intent: Intent
    lot_id: int | None = None
    bid_id: int | None = None


_LOT_ID = re.compile(r"\blots?\s*#?\s*(\d+)", re.IGNORECASE)
_BID_ID = re.compile(r"\bbids?\s*#?\s*(\d+)", re.IGNORECASE)

_WHY = re.compile(r"\bwhy\b|\bflagged\b|\breason", re.IGNORECASE)
_BID_WORD = re.compile(r"\bbid\b", re.IGNORECASE)

_SUSPICIOUS = re.compile(r"suspicious|riskiest|most risky|dodgy|fraud", re.IGNORECASE)
_LOT_WORD = re.compile(r"\blots?\b", re.IGNORECASE)


def route(question: str) -> Question:
    text = question.strip()

    lot_match = _LOT_ID.search(text)
    bid_match = _BID_ID.search(text)
    lot_id = int(lot_match.group(1)) if lot_match else None
    bid_id = int(bid_match.group(1)) if bid_match else None

    if _WHY.search(text) and _BID_WORD.search(text):
        return Question(text=text, intent=Intent.WHY_FLAGGED, lot_id=lot_id, bid_id=bid_id)

    if lot_id is not None:
        return Question(text=text, intent=Intent.LOT_SUMMARY, lot_id=lot_id)

    if _SUSPICIOUS.search(text) and _LOT_WORD.search(text):
        return Question(text=text, intent=Intent.MOST_SUSPICIOUS)

    return Question(text=text, intent=Intent.OVERVIEW)
