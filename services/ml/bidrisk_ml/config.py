from __future__ import annotations

import os
from dataclasses import dataclass


def _env_float(name: str, default: float) -> float:
    return float(os.getenv(name, str(default)))


def _env_int(name: str, default: int) -> int:
    return int(os.getenv(name, str(default)))


def _env_bool(name: str, default: bool) -> bool:
    return os.getenv(name, str(default)).strip().lower() in {"1", "true", "yes", "on"}


@dataclass(frozen=True)
class Config:
    grpc_port: int
    model_path: str
    risk_level_low_max: float
    risk_level_medium_max: float
    reflection_enabled: bool
    log_level: str
    auction_host: str
    auction_port: int
    auction_deadline_seconds: float

    ollama_host: str
    ollama_port: int
    ollama_model: str
    llm_temperature: float
    llm_max_tokens: int
    llm_request_timeout_seconds: float
    llm_history_limit: int
    llm_bids_in_prompt: int
    llm_lot_window: int
    llm_num_thread: int

    @property
    def auction_target(self) -> str:
        return f"{self.auction_host}:{self.auction_port}"

    @property
    def ollama_url(self) -> str:
        return f"http://{self.ollama_host}:{self.ollama_port}"

    @classmethod
    def from_env(cls) -> Config:
        return cls(
            grpc_port=_env_int("ML_GRPC_PORT", 5002),
            model_path=os.getenv("MODEL_PATH", "/app/models/bidrisk.joblib"),
            risk_level_low_max=_env_float("RISK_LEVEL_LOW_MAX", 0.40),
            risk_level_medium_max=_env_float("RISK_LEVEL_MEDIUM_MAX", 0.70),
            reflection_enabled=_env_bool("GRPC_REFLECTION", True),
            log_level=os.getenv("LOG_LEVEL", "INFO").upper(),
            auction_host=os.getenv("AUCTION_GRPC_HOST", "auction-service"),
            auction_port=_env_int("AUCTION_GRPC_PORT", 5001),
            auction_deadline_seconds=_env_int("AUCTION_DEADLINE_MS", 2000) / 1000.0,
            ollama_host=os.getenv("OLLAMA_HOST", "ollama"),
            ollama_port=_env_int("OLLAMA_PORT", 11434),
            ollama_model=os.getenv("OLLAMA_MODEL", "llama3.2:1b"),
            llm_temperature=_env_float("LLM_TEMPERATURE", 0.1),
            llm_max_tokens=_env_int("LLM_MAX_TOKENS", 200),
            llm_request_timeout_seconds=_env_int("LLM_REQUEST_TIMEOUT_MS", 120_000) / 1000.0,
            llm_history_limit=_env_int("LLM_HISTORY_LIMIT", 30),
            llm_bids_in_prompt=_env_int("LLM_BIDS_IN_PROMPT", 5),
            llm_lot_window=_env_int("LLM_LOT_WINDOW", 40),
            llm_num_thread=_env_int("LLM_NUM_THREAD", 2),
        )
