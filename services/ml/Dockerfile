ARG PYTHON_IMAGE_TAG=3.12-slim-bookworm
ARG BUF_IMAGE_TAG=1.72.0

FROM bufbuild/buf:${BUF_IMAGE_TAG} AS protogen
WORKDIR /w
COPY buf.yaml buf.gen.yaml ./
COPY proto ./proto
RUN ["buf", "generate"]

FROM python:${PYTHON_IMAGE_TAG} AS deps
WORKDIR /app/services/ml
COPY services/ml/requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

FROM deps AS dev
COPY services/ml/requirements-dev.txt ./
RUN pip install --no-cache-dir -r requirements-dev.txt

FROM deps AS trainer
ARG TRAIN_SAMPLES=5000
ARG TRAIN_SEED=42
ARG TRAIN_SUSPICIOUS_RATE=0.15
ARG TRAIN_LABEL_NOISE=0.05
ARG RISK_REJECT_THRESHOLD=0.70
ENV TRAIN_SAMPLES=${TRAIN_SAMPLES} \
    TRAIN_SEED=${TRAIN_SEED} \
    TRAIN_SUSPICIOUS_RATE=${TRAIN_SUSPICIOUS_RATE} \
    TRAIN_LABEL_NOISE=${TRAIN_LABEL_NOISE} \
    RISK_REJECT_THRESHOLD=${RISK_REJECT_THRESHOLD} \
    PYTHONPATH=/app/services/ml
COPY services/ml/bidrisk_ml ./bidrisk_ml
COPY services/ml/generate.py services/ml/train.py ./
RUN python generate.py && python train.py

FROM deps AS runtime
COPY --from=protogen /w/services/ml/generated ./generated
COPY services/ml/bidrisk_ml ./bidrisk_ml
COPY services/ml/server.py services/ml/healthcheck.py ./
COPY --from=trainer /app/models /app/models

ENV PYTHONPATH=/app/services/ml:/app/services/ml/generated \
    PYTHONUNBUFFERED=1 \
    MODEL_PATH=/app/models/bidrisk.joblib

RUN useradd --create-home --uid 10001 appuser && chown -R appuser /app
USER appuser

EXPOSE 5002
CMD ["python", "server.py"]
