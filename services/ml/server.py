#!/usr/bin/env python3

from __future__ import annotations

import asyncio
import logging
import signal
from collections.abc import AsyncIterator

import grpc
from bidrisk import bidrisk_pb2, bidrisk_pb2_grpc
from grpc_health.v1 import health, health_pb2, health_pb2_grpc
from grpc_reflection.v1alpha import reflection

from bidrisk_ml import lot_risk, proto_adapter
from bidrisk_ml.answers import AnswerService, AnswerStats
from bidrisk_ml.auction_client import AuctionClient, AuctionUnavailableError
from bidrisk_ml.broadcast import Broadcaster
from bidrisk_ml.config import Config
from bidrisk_ml.ollama_client import OllamaClient, OllamaUnavailableError
from bidrisk_ml.predictor import ModelArtifactError, RiskPredictor

LOG = logging.getLogger("ml-service")

RISK_SERVICE_NAME = bidrisk_pb2.DESCRIPTOR.services_by_name["RiskService"].full_name


# grpc.aio because AskAboutData holds many token streams at once. The fan-out benchmark
# runs fifty.
class RiskService(bidrisk_pb2_grpc.RiskServiceServicer):
    def __init__(
        self,
        predictor: RiskPredictor,
        auction: AuctionClient,
        answers: AnswerService,
        low_max: float,
        medium_max: float,
    ) -> None:
        self._predictor = predictor
        self._auction = auction
        self._answers = answers
        self._low_max = low_max
        self._medium_max = medium_max

    async def PredictBidRisk(  # noqa: N802 — name fixed by the generated base class
        self,
        request: bidrisk_pb2.PredictBidRiskRequest,
        context: grpc.aio.ServicerContext,
    ) -> bidrisk_pb2.PredictBidRiskResponse:
        if not request.HasField("context"):
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "context is required")

        facts = proto_adapter.facts_from_context(request.context)
        prediction, inference_ms = self._predictor.predict_one(facts)

        return bidrisk_pb2.PredictBidRiskResponse(
            assessment=proto_adapter.assessment_to_proto(
                prediction, inference_ms, self._predictor.model_version
            )
        )

    async def PredictBatch(  # noqa: N802 — name fixed by the generated base class
        self,
        request: bidrisk_pb2.PredictBatchRequest,
        context: grpc.aio.ServicerContext,
    ) -> bidrisk_pb2.PredictBatchResponse:
        facts = [proto_adapter.facts_from_context(c) for c in request.contexts]
        predictions, inference_ms = self._predictor.predict(facts)

        return bidrisk_pb2.PredictBatchResponse(
            assessments=[
                proto_adapter.assessment_to_proto(p, 0.0, self._predictor.model_version)
                for p in predictions
            ],
            total_inference_ms=inference_ms,
        )

    async def AssessLotRisk(  # noqa: N802 — name fixed by the generated base class
        self,
        request: bidrisk_pb2.AssessLotRiskRequest,
        context: grpc.aio.ServicerContext,
    ) -> bidrisk_pb2.AssessLotRiskResponse:
        try:
            bids = await self._auction.bid_history(request.lot_id, limit=0)
        except AuctionUnavailableError as error:
            await context.abort(grpc.StatusCode.UNAVAILABLE, f"auction-service: {error}")

        return bidrisk_pb2.AssessLotRiskResponse(
            risk=lot_risk.aggregate(request.lot_id, bids, self._low_max, self._medium_max)
        )

    async def AssessLotRiskBatch(  # noqa: N802 — name fixed by the generated base class
        self,
        request: bidrisk_pb2.AssessLotRiskBatchRequest,
        context: grpc.aio.ServicerContext,
    ) -> bidrisk_pb2.AssessLotRiskBatchResponse:
        lot_ids = list(request.lot_ids)
        try:
            histories = await self._auction.bid_histories(lot_ids, limit_per_lot=0)
        except AuctionUnavailableError as error:
            await context.abort(grpc.StatusCode.UNAVAILABLE, f"auction-service: {error}")

        return bidrisk_pb2.AssessLotRiskBatchResponse(
            risks=[
                lot_risk.aggregate(
                    lot_id, histories.get(lot_id, []), self._low_max, self._medium_max
                )
                for lot_id in lot_ids
            ]
        )

    async def AskAboutData(  # noqa: N802 — name fixed by the generated base class
        self,
        request: bidrisk_pb2.AskAboutDataRequest,
        context: grpc.aio.ServicerContext,
    ) -> AsyncIterator[bidrisk_pb2.AskAboutDataResponse]:
        question = request.question.strip()
        if not question:
            await context.abort(grpc.StatusCode.INVALID_ARGUMENT, "question is required")

        try:
            async for item in self._answers.stream(question, request.broadcast_id):
                if isinstance(item, AnswerStats):
                    yield bidrisk_pb2.AskAboutDataResponse(
                        stats=bidrisk_pb2.StreamStats(
                            time_to_first_token_ms=item.time_to_first_token_ms,
                            total_ms=item.total_ms,
                            token_count=item.token_count,
                        )
                    )
                else:
                    yield bidrisk_pb2.AskAboutDataResponse(token=item)
        except OllamaUnavailableError as error:
            await context.abort(grpc.StatusCode.UNAVAILABLE, f"ollama: {error}")


async def _warm_up_llm(ollama: OllamaClient) -> None:
    try:
        elapsed_ms = await ollama.warm_up()
    except Exception as error:
        LOG.warning("llm warm-up failed (%s); the first answer will be slow", error)
    else:
        LOG.info("llm warm-up complete in %.0f ms", elapsed_ms)


async def serve() -> None:
    config = Config.from_env()
    logging.basicConfig(
        level=config.log_level, format="%(asctime)s %(levelname)-5s %(name)s · %(message)s"
    )

    predictor = RiskPredictor(
        config.model_path, config.risk_level_low_max, config.risk_level_medium_max
    )
    warm_ms = predictor.warm_up()
    LOG.info(
        "model %s loaded (trained %s) · warm-up %.2f ms",
        predictor.model_version,
        predictor.trained_at,
        warm_ms,
    )

    auction = AuctionClient(config.auction_target, config.auction_deadline_seconds)
    ollama = OllamaClient(
        base_url=config.ollama_url,
        model=config.ollama_model,
        temperature=config.llm_temperature,
        max_tokens=config.llm_max_tokens,
        request_timeout_seconds=config.llm_request_timeout_seconds,
        num_thread=config.llm_num_thread,
    )
    answers = AnswerService(
        auction=auction,
        ollama=ollama,
        broadcaster=Broadcaster(),
        history_limit=config.llm_history_limit,
        bids_in_prompt=config.llm_bids_in_prompt,
        top_lots=config.llm_lot_window,
        risk_low_max=config.risk_level_low_max,
        risk_medium_max=config.risk_level_medium_max,
    )

    server = grpc.aio.server()
    bidrisk_pb2_grpc.add_RiskServiceServicer_to_server(
        RiskService(
            predictor=predictor,
            auction=auction,
            answers=answers,
            low_max=config.risk_level_low_max,
            medium_max=config.risk_level_medium_max,
        ),
        server,
    )

    health_servicer = health.aio.HealthServicer()
    health_pb2_grpc.add_HealthServicer_to_server(health_servicer, server)

    if config.reflection_enabled:
        reflection.enable_server_reflection(
            [RISK_SERVICE_NAME, health.SERVICE_NAME, reflection.SERVICE_NAME], server
        )

    server.add_insecure_port(f"[::]:{config.grpc_port}")
    await server.start()

    for name in ("", RISK_SERVICE_NAME):
        await health_servicer.set(name, health_pb2.HealthCheckResponse.SERVING)

    warm_up_task = asyncio.create_task(_warm_up_llm(ollama))

    LOG.info(
        "listening on :%d · auction %s · ollama %s (%s) · reflection %s",
        config.grpc_port,
        config.auction_target,
        config.ollama_url,
        config.ollama_model,
        "on" if config.reflection_enabled else "off",
    )

    stopping = asyncio.Event()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGTERM, signal.SIGINT):
        loop.add_signal_handler(sig, stopping.set)

    await stopping.wait()
    LOG.info("shutting down")
    warm_up_task.cancel()
    await health_servicer.enter_graceful_shutdown()
    await server.stop(grace=5.0)
    await auction.close()
    await ollama.close()


if __name__ == "__main__":
    try:
        asyncio.run(serve())
    except ModelArtifactError as exc:
        raise SystemExit(f"ml-service cannot start: {exc}") from exc
