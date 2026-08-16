from __future__ import annotations

import re

# Finds numbers the answer states that the facts do not support. If the model invents a
# number, that is a bug in the prompt.
_NUMBER = re.compile(r"-?\d[\d,]*(?:\.\d+)?")

_RELATIVE_TOLERANCE = 0.01
_ABSOLUTE_TOLERANCE = 0.5


def numbers_in(text: str) -> list[tuple[str, float]]:
    found = []
    for match in _NUMBER.finditer(text):
        token = match.group(0)
        try:
            found.append((token, float(token.replace(",", ""))))
        except ValueError:  # pragma: no cover — the pattern cannot produce this
            continue
    return found


def _supported(value: float, allowed: list[float]) -> bool:
    return any(
        abs(value - candidate) <= max(_ABSOLUTE_TOLERANCE, _RELATIVE_TOLERANCE * abs(candidate))
        for candidate in allowed
    )


def unsupported_numbers(answer: str, facts: str) -> list[str]:
    allowed = [value for _, value in numbers_in(facts)]
    if not allowed:
        return [token for token, _ in numbers_in(answer)]

    return [token for token, value in numbers_in(answer) if not _supported(value, allowed)]
