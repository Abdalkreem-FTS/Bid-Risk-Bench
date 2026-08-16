#!/usr/bin/env python3

from __future__ import annotations

import os
import sys

import grpc
from grpc_health.v1 import health_pb2, health_pb2_grpc

TIMEOUT_SECONDS = 3.0


def main() -> int:
    port = os.getenv("ML_GRPC_PORT", "5002")
    try:
        with grpc.insecure_channel(f"localhost:{port}") as channel:
            response = health_pb2_grpc.HealthStub(channel).Check(
                health_pb2.HealthCheckRequest(service=""), timeout=TIMEOUT_SECONDS
            )
    except grpc.RpcError as exc:
        print(f"unhealthy: {exc}", file=sys.stderr)
        return 1

    if response.status != health_pb2.HealthCheckResponse.SERVING:
        print(f"unhealthy: {health_pb2.HealthCheckResponse.ServingStatus.Name(response.status)}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
