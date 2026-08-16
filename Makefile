.DEFAULT_GOAL := help
.PHONY: help proto golden train up down logs ps seed seed-data test lint integration smoke bench bench-quick clean

help: ## Show this help
	@grep -hE '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

proto: ## Regenerate gRPC code for Python and C# from the one .proto file
	@./scripts/proto-gen.sh

golden: ## Regenerate the golden files the cross-language contract test reads
	@./scripts/golden.sh

train: ## Generate synthetic bids and train the risk model into models/
	@./scripts/train.sh

up: ## Build and start the whole stack
	@docker compose up -d --build

down: ## Stop the stack (add ARGS=-v to drop the volumes too)
	@docker compose down $(ARGS)

logs: ## Follow logs from every service
	@docker compose logs -f

ps: ## Show each service and its health
	@docker compose ps

test: ## Unit tests and the contract test
	@./scripts/test.sh

lint: ## Every quality gate CI runs: buf lint, ruff, mypy, C# warnings-as-errors
	@./scripts/lint.sh

seed: ## Load the fixed dataset the benchmarks run against
	@./scripts/seed.sh

seed-data: ## Rebuild data/seed/seed.sql (scores via ml-service, so the stack must be up), then load it
	@./scripts/seed.sh --regenerate

integration: ## Start the stack, seed it, and place bids end to end
	@./scripts/integration.sh

smoke: ## One query, one mutation, one subscription, one gRPC call, one raw WS message
	@./scripts/smoke.sh

bench: ## Run the full benchmark matrix 3x and write bench/results/ + charts
	@./scripts/bench.sh

bench-quick: ## One pass, skipping the slow LLM streaming scenarios
	@BENCH_RUNS=1 BENCH_SKIP_LLM=1 ./scripts/bench.sh

clean: ## Remove generated code, the trained model, build state and benchmark output
	@docker run --rm -v "$(CURDIR):/w" -w /w alpine:3.21 sh -c \
		'rm -rf services/ml/generated services/contracts/Generated models \
		        data/synthetic_bids.csv .cache && \
		 find . -type d \( -name bin -o -name obj \) -not -path "./.git/*" -prune \
		        -exec rm -rf {} +'
	@echo "removed generated code, models, build state and the package cache — run 'make proto train' to rebuild"
