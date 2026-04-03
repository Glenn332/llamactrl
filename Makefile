.PHONY: build install dev clean help

RID ?= osx-arm64

help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-15s\033[0m %s\n", $$1, $$2}'

build: ## Build for target RID (default: osx-arm64). Usage: make build RID=linux-x64
	@bash build.sh $(RID)

install: ## Install binary to /usr/local/bin/llamactrl (run after build)
	@bash install.sh

dev: ## Run frontend dev server + backend in watch mode (requires two terminals)
	@echo "Starting development servers..."
	@echo ""
	@echo "In this terminal: backend (dotnet watch)"
	@echo "Open another terminal and run: cd src/frontend && npm run dev"
	@echo ""
	cd src/LlamaCtrl && dotnet watch run

frontend-dev: ## Run frontend dev server only
	cd src/frontend && npm run dev

clean: ## Clean build artifacts
	rm -rf dist/
	rm -rf src/LlamaCtrl/bin/ src/LlamaCtrl/obj/
	rm -rf src/LlamaCtrl/wwwroot/assets src/LlamaCtrl/wwwroot/index.html
	@echo "Cleaned."
