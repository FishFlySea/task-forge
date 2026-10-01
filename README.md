# TaskForge

TaskForge is a local orchestrator for controlled AI-assisted software development.

Architecture:

- [ADR-0001](docs/adr/0001-controlled-multi-agent-architecture.md)
- [RFC-0001](docs/rfc/0001-mvp-implementation.md)

## Current status

The first end-to-end MVP workflow is implemented:

```text
Task
 ↓
Planner (Ollama)
 ↓
Explorer (deterministic search + Ollama)
 ↓
TaskPacket
 ↓
Codex implementation [1/2]
 ↓
dotnet build/test
 ↓
Diagnostic (Ollama, only on failure)
 ↓
optional Codex correction [2/2]
 ↓
dotnet build/test
 ↓
Review (Ollama)
 ↓
Completed / NeedsUser / Failed / BudgetExceeded
```

TaskForge launches Codex with the `multi_agent` feature explicitly disabled.
The budget permits exactly one implementation run and at most one correction run.

## Requirements

- .NET 10 SDK
- Git
- Ollama reachable from the TaskForge host
- a local coding model in Ollama
- Codex CLI installed and authenticated

Defaults:

```text
TASKFORGE_OLLAMA_URL=http://localhost:11434/
TASKFORGE_OLLAMA_MODEL=qwen3-coder
TASKFORGE_OLLAMA_TIMEOUT_SECONDS=120
TASKFORGE_CODEX_EXECUTABLE=codex
TASKFORGE_CODEX_TIMEOUT_SECONDS=1200
TASKFORGE_DOTNET_EXECUTABLE=dotnet
```

## Usage

```bash
dotnet run --project src/TaskForge.Cli -- run "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- run --repo ../some-repo "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- runs
dotnet run --project src/TaskForge.Cli -- show <run-id>
```

Set `TASKFORGE_RUNS_DIRECTORY` to override the local run storage directory.

Each run stores its plan, exploration result, task packet, Codex JSONL trace,
build/test logs, diagnosis (if needed), git snapshot, review, and final state.
