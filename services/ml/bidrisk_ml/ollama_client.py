from __future__ import annotations

import json
import time
from collections.abc import AsyncIterator

import httpx


class OllamaUnavailableError(RuntimeError):
    """The local model could not be reached or refused the request."""


KEEP_ALIVE = "30m"

NUM_CTX = 2048

DEFAULT_NUM_THREAD = 2


# Every token is yielded the moment it arrives. Buffering would ruin time-to-first-token.
class OllamaClient:
    def __init__(
        self,
        base_url: str,
        model: str,
        temperature: float,
        max_tokens: int,
        request_timeout_seconds: float,
        num_thread: int = DEFAULT_NUM_THREAD,
    ) -> None:
        self._model = model
        self._temperature = temperature
        self._max_tokens = max_tokens
        self._num_thread = num_thread
        self._client = httpx.AsyncClient(
            base_url=base_url,
            timeout=httpx.Timeout(request_timeout_seconds, connect=5.0),
        )

    async def close(self) -> None:
        await self._client.aclose()

    # Ollama loads the weights on the first generation. Do it at startup so the wait does
    # not land inside a measurement.
    async def warm_up(self) -> float:
        started = time.perf_counter()
        payload = {
            "model": self._model,
            "messages": [{"role": "user", "content": "ready"}],
            "stream": False,
            "keep_alive": KEEP_ALIVE,
            "options": {
                "num_predict": 1,
                "temperature": 0.0,
                "num_ctx": NUM_CTX,
                "num_thread": self._num_thread,
            },
        }
        response = await self._client.post("/api/chat", json=payload)
        response.raise_for_status()
        return (time.perf_counter() - started) * 1000.0

    async def stream_chat(self, system: str, user: str) -> AsyncIterator[str]:
        payload = {
            "model": self._model,
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            "stream": True,
            "keep_alive": KEEP_ALIVE,
            "options": {
                "temperature": self._temperature,
                "num_predict": self._max_tokens,
                "num_ctx": NUM_CTX,
                "num_thread": self._num_thread,
            },
        }

        try:
            async with self._client.stream("POST", "/api/chat", json=payload) as response:
                if response.status_code != httpx.codes.OK:
                    body = (await response.aread()).decode("utf-8", "replace")
                    raise OllamaUnavailableError(f"ollama returned {response.status_code}: {body}")

                async for line in response.aiter_lines():
                    if not line.strip():
                        continue
                    try:
                        message = json.loads(line)
                    except json.JSONDecodeError:
                        continue

                    if error := message.get("error"):
                        raise OllamaUnavailableError(str(error))

                    token = message.get("message", {}).get("content", "")
                    if token:
                        yield token

                    if message.get("done"):
                        return
        except httpx.HTTPError as error:
            raise OllamaUnavailableError(str(error)) from error
