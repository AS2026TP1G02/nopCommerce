# Simple helpers for the Omnichannel demo stack (docker compose).
# Run from the repo root. `make` (or `make help`) lists the targets.

.DEFAULT_GOAL := help
.PHONY: up down clean logs ps help

up: ## Build images and start the full stack (detached)
	docker compose up --build -d

down: ## Stop and remove containers (keeps volumes -> DB + install persist)
	docker compose down

clean: ## Stop and remove containers AND volumes (wipes DB + App_Data -> fresh slate)
	docker compose down -v --remove-orphans

logs: ## Follow logs from all services
	docker compose logs -f

ps: ## Show container status
	docker compose ps

help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  %-7s %s\n", $$1, $$2}'
