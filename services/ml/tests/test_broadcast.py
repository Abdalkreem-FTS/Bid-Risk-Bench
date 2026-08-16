from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator

from bidrisk_ml.broadcast import Broadcaster, SharedAnswer

TOKENS = ["one ", "two ", "three"]


async def collect(shared: SharedAnswer) -> str:
    return "".join([token async for token in shared.follow()])


async def test_a_single_subscriber_receives_every_token() -> None:
    broadcaster = Broadcaster()

    async def produce(_: SharedAnswer) -> AsyncIterator[str]:
        for token in TOKENS:
            yield token

    shared, started = await broadcaster.subscribe("q1", produce)
    assert started is True
    assert await collect(shared) == "one two three"


async def test_many_subscribers_share_one_generation() -> None:
    runs = 0

    async def produce(_: SharedAnswer) -> AsyncIterator[str]:
        nonlocal runs
        runs += 1
        for token in TOKENS:
            await asyncio.sleep(0)
            yield token

    broadcaster = Broadcaster()

    async def listen() -> str:
        shared, _ = await broadcaster.subscribe("shared-question", produce)
        return await collect(shared)

    answers = await asyncio.gather(*[listen() for _ in range(50)])

    assert runs == 1, "fifty listeners must not mean fifty generations"
    assert answers == ["one two three"] * 50


async def test_a_late_subscriber_still_gets_the_beginning() -> None:
    release = asyncio.Event()

    async def produce(_: SharedAnswer) -> AsyncIterator[str]:
        yield "one "
        await release.wait()
        yield "two "
        yield "three"

    broadcaster = Broadcaster()
    first, _ = await broadcaster.subscribe("late", produce)
    first_task = asyncio.create_task(collect(first))

    await asyncio.sleep(0.01)
    late, started = await broadcaster.subscribe("late", produce)
    assert started is False

    release.set()
    assert await collect(late) == "one two three"
    assert await first_task == "one two three"


async def test_different_ids_get_their_own_answers() -> None:
    async def produce_for(name: str):
        async def produce(_: SharedAnswer) -> AsyncIterator[str]:
            yield name

        return produce

    broadcaster = Broadcaster()
    a, _ = await broadcaster.subscribe("a", await produce_for("answer-a"))
    b, _ = await broadcaster.subscribe("b", await produce_for("answer-b"))

    assert await collect(a) == "answer-a"
    assert await collect(b) == "answer-b"


async def test_a_failing_generation_is_reported_to_every_listener() -> None:
    async def produce(_: SharedAnswer) -> AsyncIterator[str]:
        yield "partial "
        raise RuntimeError("ollama fell over")

    broadcaster = Broadcaster()
    shared, _ = await broadcaster.subscribe("boom", produce)

    received: list[str] = []
    try:
        async for token in shared.follow():
            received.append(token)
    except RuntimeError as error:
        assert str(error) == "ollama fell over"
    else:  # pragma: no cover
        raise AssertionError("the failure should have reached the listener")

    assert received == ["partial "]


async def test_a_summary_attached_during_the_answer_survives_completion() -> None:
    async def produce(shared: SharedAnswer) -> AsyncIterator[str]:
        yield "hello"
        shared.summary = {"tokens": 1}

    broadcaster = Broadcaster()
    shared, _ = await broadcaster.subscribe("with-summary", produce)
    assert await collect(shared) == "hello"
    assert shared.summary == {"tokens": 1}


async def test_every_listener_receives_the_same_summary() -> None:
    async def produce(shared: SharedAnswer) -> AsyncIterator[str]:
        for token in TOKENS:
            await asyncio.sleep(0)
            yield token
        shared.summary = "timing"

    broadcaster = Broadcaster()

    async def listen() -> object:
        shared, _ = await broadcaster.subscribe("summary-fanout", produce)
        await collect(shared)
        return shared.summary

    assert await asyncio.gather(*[listen() for _ in range(10)]) == ["timing"] * 10


async def test_the_id_is_released_so_a_later_question_is_answered_afresh() -> None:
    calls = 0

    async def produce(_: SharedAnswer) -> AsyncIterator[str]:
        nonlocal calls
        calls += 1
        yield f"answer {calls}"

    broadcaster = Broadcaster()

    first, _ = await broadcaster.subscribe("reused", produce)
    assert await collect(first) == "answer 1"
    await asyncio.sleep(0.01)
    assert broadcaster.active() == 0

    second, started = await broadcaster.subscribe("reused", produce)
    assert started is True
    assert await collect(second) == "answer 2"
