from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator, Callable


# Callers passing the same broadcast_id share one generation. Without this, 50 listeners
# would mean 50 runs of the model.
class SharedAnswer:
    def __init__(self) -> None:
        self.tokens: list[str] = []
        self.done = False
        self.failure: BaseException | None = None
        self.summary: object | None = None
        self._condition = asyncio.Condition()

    async def append(self, token: str) -> None:
        async with self._condition:
            self.tokens.append(token)
            self._condition.notify_all()

    async def finish(
        self, failure: BaseException | None = None, summary: object | None = None
    ) -> None:
        async with self._condition:
            self.done = True
            self.failure = failure
            if summary is not None:
                self.summary = summary
            self._condition.notify_all()

    async def follow(self) -> AsyncIterator[str]:
        index = 0
        while True:
            async with self._condition:
                while index >= len(self.tokens) and not self.done:
                    await self._condition.wait()

                if index < len(self.tokens):
                    token = self.tokens[index]
                    index += 1
                else:
                    if self.failure is not None:
                        raise self.failure
                    return

            yield token


class Broadcaster:
    def __init__(self) -> None:
        self._shared: dict[str, SharedAnswer] = {}
        self._lock = asyncio.Lock()
        self._running: set[asyncio.Task[None]] = set()

    def active(self) -> int:
        return len(self._shared)

    async def subscribe(
        self, key: str, produce: Callable[[SharedAnswer], AsyncIterator[str]]
    ) -> tuple[SharedAnswer, bool]:
        async with self._lock:
            shared = self._shared.get(key)
            if shared is not None:
                return shared, False

            shared = SharedAnswer()
            self._shared[key] = shared

        task = asyncio.create_task(self._produce(key, shared, produce))
        self._running.add(task)
        task.add_done_callback(self._running.discard)
        return shared, True

    async def _produce(
        self,
        key: str,
        shared: SharedAnswer,
        produce: Callable[[SharedAnswer], AsyncIterator[str]],
    ) -> None:
        try:
            async for token in produce(shared):
                await shared.append(token)
        except (Exception, asyncio.CancelledError) as failure:
            await shared.finish(failure)
        else:
            await shared.finish()
        finally:
            async with self._lock:
                self._shared.pop(key, None)
