from __future__ import annotations

import pytest

from bidrisk_ml.intent import Intent, route


@pytest.mark.parametrize(
    "question",
    [
        "Summarise the bidding activity on lot 42.",
        "summarize the bidding activity on lot 42",
        "What happened on lot 42?",
        "Tell me about lot #42",
        "lot 42",
    ],
)
def test_questions_about_one_lot_are_summaries(question: str) -> None:
    routed = route(question)
    assert routed.intent == Intent.LOT_SUMMARY
    assert routed.lot_id == 42


@pytest.mark.parametrize(
    "question",
    [
        "Why was this bid flagged as risky?",
        "Why was bid 649 flagged?",
        "What is the reason bid #649 was rejected?",
    ],
)
def test_questions_about_a_bid_ask_why_it_was_flagged(question: str) -> None:
    assert route(question).intent == Intent.WHY_FLAGGED


def test_a_named_bid_is_picked_out_of_the_question() -> None:
    assert route("Why was bid 649 flagged as risky?").bid_id == 649


@pytest.mark.parametrize(
    "question",
    [
        "Which lots look most suspicious right now, and why?",
        "what are the riskiest lots",
        "show me suspicious lots",
    ],
)
def test_questions_about_several_lots_rank_them(question: str) -> None:
    assert route(question).intent == Intent.MOST_SUSPICIOUS


def test_the_suspicious_lots_question_is_not_mistaken_for_a_bid_explanation() -> None:
    assert route("Which lots look most suspicious right now, and why?").intent == (
        Intent.MOST_SUSPICIOUS
    )


def test_a_lot_named_alongside_a_bid_question_is_still_a_bid_question() -> None:
    routed = route("Why was bid 12 on lot 5 flagged?")
    assert routed.intent == Intent.WHY_FLAGGED
    assert routed.bid_id == 12
    assert routed.lot_id == 5


@pytest.mark.parametrize(
    "question",
    ["What is going on?", "hello", "how many lots are there"],
)
def test_anything_else_falls_back_rather_than_failing(question: str) -> None:
    assert route(question).intent == Intent.OVERVIEW
