from __future__ import annotations

from bidrisk_ml.grounding import numbers_in, unsupported_numbers

FACTS = """FACTS
lot.id = 42
lot.current_price_usd = 717.08
lot.total_bids = 15
bids.highest_risk_score = 0.42
bids.average_gap_seconds = 4830
"""


def test_every_number_in_the_text_is_found() -> None:
    assert [token for token, _ in numbers_in("price 717.08 after 15 bids")] == ["717.08", "15"]


def test_thousands_separators_are_understood() -> None:
    assert numbers_in("4,830 seconds")[0][1] == 4830.0


def test_an_answer_built_from_the_facts_is_clean() -> None:
    answer = "Lot 42 has had 15 bids and stands at 717.08, with a highest risk score of 0.42."
    assert unsupported_numbers(answer, FACTS) == []


def test_an_invented_number_is_caught() -> None:
    answer = "Lot 42 has had 15 bids and stands at 1240.00."
    assert unsupported_numbers(answer, FACTS) == ["1240.00"]


def test_a_sensibly_rounded_number_is_accepted() -> None:
    assert unsupported_numbers("The price is about 717 dollars.", FACTS) == []


def test_rounding_tolerance_does_not_excuse_a_different_number() -> None:
    assert unsupported_numbers("The price is about 780 dollars.", FACTS) == ["780"]


def test_several_invented_numbers_are_all_reported_in_order() -> None:
    answer = "There were 15 bids from 9 bidders totalling 12000."
    assert unsupported_numbers(answer, FACTS) == ["9", "12000"]


def test_an_answer_with_no_numbers_is_clean() -> None:
    assert unsupported_numbers("The bidding looks unremarkable.", FACTS) == []


def test_with_no_facts_every_number_is_unsupported() -> None:
    assert unsupported_numbers("The price is 717.08.", "") == ["717.08"]
