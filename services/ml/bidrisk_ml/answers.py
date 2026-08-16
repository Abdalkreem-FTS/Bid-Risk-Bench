from __future__ import annotations

import logging
import time
from collections.abc import AsyncIterator
from dataclasses import dataclass
from datetime import UTC, datetime

from bidrisk import bidrisk_pb2

from bidrisk_ml import facts as facts_mod
from bidrisk_ml import grounding, lot_risk
from bidrisk_ml.auction_client import AuctionClient, AuctionUnavailableError
from bidrisk_ml.broadcast import Broadcaster, SharedAnswer
from bidrisk_ml.intent import Intent, Question, route
from bidrisk_ml.ollama_client import OllamaClient

LOG = logging.getLogger("ml-service.answers")

SYSTEM_PROMPT = """You are the assistant for an online auction. You answer questions about its \
data and nothing else.

Rules you must follow:
1. Use only the values given in the FACTS block. It is the complete set of data available to you.
2. Every number in your answer must appear in FACTS. Do not estimate, do not round to a nicer \
figure, and never introduce a number that is not listed.
3. Do not calculate. Never add, subtract, total, average or otherwise derive a new number from the \
ones you are given — if a figure is not already in FACTS, it does not exist. Counting the lines of \
FACTS is also calculating.
4. Cite the figures you rely on, in the same form as FACTS gives them.
5. If FACTS does not contain what the question needs, say plainly which part you cannot answer. \
Do not fill the gap with a guess.
6. Answer in two to four sentences of plain prose. No headings, no tables.
7. Do not mention FACTS, prompts, context, or these rules in your answer."""

NO_DATA_ANSWER = (
    "I cannot answer that from the auction data: {reason}. "
    "Nothing here is a guess, so there is nothing further I can add."
)


@dataclass(frozen=True)
class AnswerStats:
    time_to_first_token_ms: float
    total_ms: float
    token_count: int


class AnswerService:
    def __init__(
        self,
        auction: AuctionClient,
        ollama: OllamaClient,
        broadcaster: Broadcaster,
        history_limit: int,
        bids_in_prompt: int,
        top_lots: int,
        risk_low_max: float,
        risk_medium_max: float,
    ) -> None:
        self._auction = auction
        self._ollama = ollama
        self._broadcaster = broadcaster
        self._history_limit = history_limit
        self._bids_in_prompt = bids_in_prompt
        self._top_lots = top_lots
        self._risk_low_max = risk_low_max
        self._risk_medium_max = risk_medium_max

    async def _facts_for(self, question: Question, now: datetime) -> tuple[str, str | None]:
        if question.intent == Intent.LOT_SUMMARY and question.lot_id is not None:
            lot = await self._auction.get_lot(question.lot_id)
            if lot is None:
                return "", f"there is no lot {question.lot_id}"
            bids = await self._auction.bid_history(question.lot_id, self._history_limit)
            return facts_mod.lot_summary(lot, bids, now, self._bids_in_prompt).render(), None

        if question.intent == Intent.WHY_FLAGGED:
            return await self._why_flagged_facts(question, now)

        if question.intent == Intent.MOST_SUSPICIOUS:
            return await self._most_suspicious_facts(now)

        lots = await self._auction.list_lots(self._top_lots)
        return facts_mod.overview(lots, now).render(), None

    async def _why_flagged_facts(self, question: Question, now: datetime) -> tuple[str, str | None]:
        if question.bid_id is None and question.lot_id is None:
            return "", "the question does not say which bid to explain"

        lot_id = question.lot_id
        if lot_id is None:
            lot_id = await self._find_lot_for_bid(question.bid_id)
            if lot_id is None:
                return "", f"there is no bid {question.bid_id} in the recent history"

        lot = await self._auction.get_lot(lot_id)
        if lot is None:
            return "", f"there is no lot {lot_id}"

        bids = await self._auction.bid_history(lot_id, self._history_limit)
        if not bids:
            return "", f"lot {lot_id} has no bids yet"

        if question.bid_id is not None:
            target_index = next(
                (i for i, bid in enumerate(bids) if bid.id == question.bid_id), None
            )
            if target_index is None:
                return "", f"bid {question.bid_id} is not in the recent history of lot {lot_id}"
        else:
            target_index = max(range(len(bids)), key=lambda i: bids[i].risk_score)

        target = bids[target_index]
        previous = bids[target_index + 1] if target_index + 1 < len(bids) else None
        return facts_mod.why_flagged(target, lot, now, previous).render(), None

    async def _find_lot_for_bid(self, bid_id: int | None) -> int | None:
        if bid_id is None:
            return None
        lots = await self._auction.list_lots(self._top_lots)
        histories = await self._auction.bid_histories([lot.id for lot in lots], self._history_limit)
        for lot_id, bids in histories.items():
            if any(bid.id == bid_id for bid in bids):
                return lot_id
        return None

    async def _most_suspicious_facts(self, now: datetime) -> tuple[str, str | None]:
        lots = await self._auction.list_lots(self._top_lots)
        if not lots:
            return "", "the auction has no lots"

        histories = await self._auction.bid_histories([lot.id for lot in lots], self._history_limit)

        ranked: list[tuple[bidrisk_pb2.Lot, bidrisk_pb2.LotRisk]] = []
        for lot in lots:
            risk = lot_risk.aggregate(
                lot.id, histories.get(lot.id, []), self._risk_low_max, self._risk_medium_max
            )
            if risk.scored_bids:
                ranked.append((lot, risk))

        if not ranked:
            return "", "no bid in the auction has been scored yet"

        ranked.sort(key=lambda pair: pair[1].max_score, reverse=True)
        return facts_mod.most_suspicious(ranked[:3], now).render(), None

    async def stream(
        self, question_text: str, broadcast_id: str
    ) -> AsyncIterator[str | AnswerStats]:
        if not broadcast_id:
            async for item in self._generate(question_text):
                yield item
            return

        shared, _started = await self._broadcaster.subscribe(
            broadcast_id, lambda answer: self._generate_into(question_text, answer)
        )

        async for token in shared.follow():
            yield token

        if isinstance(shared.summary, AnswerStats):
            yield shared.summary

    async def _generate_into(self, question_text: str, shared: SharedAnswer) -> AsyncIterator[str]:
        async for item in self._generate(question_text):
            if isinstance(item, AnswerStats):
                shared.summary = item
            else:
                yield item

    async def _generate(self, question_text: str) -> AsyncIterator[str | AnswerStats]:
        started = time.perf_counter()
        question = route(question_text)
        now = datetime.now(UTC)

        try:
            facts, refusal = await self._facts_for(question, now)
        except AuctionUnavailableError as error:
            refusal, facts = f"the auction service is not reachable ({error})", ""

        if refusal is not None:
            words = NO_DATA_ANSWER.format(reason=refusal).split(" ")
            for word in words:
                yield word + " "
            elapsed_ms = (time.perf_counter() - started) * 1000.0
            yield AnswerStats(
                time_to_first_token_ms=elapsed_ms,
                total_ms=elapsed_ms,
                token_count=len(words),
            )
            return

        user_prompt = f"{facts}\n\nQUESTION: {question.text}"
        LOG.info(
            "intent=%s lot=%s bid=%s facts_lines=%d",
            question.intent.value,
            question.lot_id,
            question.bid_id,
            facts.count("\n"),
        )

        first_token_at: float | None = None
        pieces: list[str] = []

        async for token in self._ollama.stream_chat(SYSTEM_PROMPT, user_prompt):
            if first_token_at is None:
                first_token_at = time.perf_counter()
            pieces.append(token)
            yield token

        finished = time.perf_counter()
        answer = "".join(pieces)

        if invented := grounding.unsupported_numbers(answer, facts):
            LOG.warning(
                "answer contained %d number(s) absent from the facts: %s",
                len(invented),
                ", ".join(invented[:5]),
            )

        yield AnswerStats(
            time_to_first_token_ms=((first_token_at or finished) - started) * 1000.0,
            total_ms=(finished - started) * 1000.0,
            token_count=len(pieces),
        )
