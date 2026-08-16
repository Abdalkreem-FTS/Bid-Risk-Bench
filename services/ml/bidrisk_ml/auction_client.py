from __future__ import annotations

import grpc
from bidrisk import bidrisk_pb2, bidrisk_pb2_grpc


class AuctionUnavailableError(RuntimeError):
    """auction-service could not be reached in time."""


class AuctionClient:
    def __init__(self, target: str, deadline_seconds: float) -> None:
        self._target = target
        self._deadline = deadline_seconds
        self._channel = grpc.aio.insecure_channel(target)
        self._stub = bidrisk_pb2_grpc.AuctionServiceStub(self._channel)

    async def close(self) -> None:
        await self._channel.close(grace=1.0)

    async def get_lot(self, lot_id: int) -> bidrisk_pb2.Lot | None:
        try:
            response = await self._stub.GetLot(
                bidrisk_pb2.GetLotRequest(lot_id=lot_id), timeout=self._deadline
            )
        except grpc.aio.AioRpcError as error:
            if error.code() == grpc.StatusCode.NOT_FOUND:
                return None
            raise AuctionUnavailableError(str(error.code())) from error
        return response.lot

    async def list_lots(self, first: int) -> list[bidrisk_pb2.Lot]:
        try:
            response = await self._stub.ListLots(
                bidrisk_pb2.ListLotsRequest(first=first), timeout=self._deadline
            )
        except grpc.aio.AioRpcError as error:
            raise AuctionUnavailableError(str(error.code())) from error
        return list(response.lots)

    async def bid_history(self, lot_id: int, limit: int) -> list[bidrisk_pb2.Bid]:
        try:
            response = await self._stub.GetBidHistory(
                bidrisk_pb2.GetBidHistoryRequest(lot_id=lot_id, limit=limit),
                timeout=self._deadline,
            )
        except grpc.aio.AioRpcError as error:
            raise AuctionUnavailableError(str(error.code())) from error
        return list(response.bids)

    async def bid_histories(
        self, lot_ids: list[int], limit_per_lot: int
    ) -> dict[int, list[bidrisk_pb2.Bid]]:
        if not lot_ids:
            return {}
        try:
            response = await self._stub.GetBidHistoryBatch(
                bidrisk_pb2.GetBidHistoryBatchRequest(lot_ids=lot_ids, limit_per_lot=limit_per_lot),
                timeout=self._deadline,
            )
        except grpc.aio.AioRpcError as error:
            raise AuctionUnavailableError(str(error.code())) from error
        return {history.lot_id: list(history.bids) for history in response.histories}
