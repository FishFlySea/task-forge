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
The hard run budget permits exactly one implementation run and at most one
correction run. Each Codex invocation also enables Codex rollout-budget tracking:
40k weighted tokens for implementation and 20k for correction by default.
Both token limits are configurable through environment variables.

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
TASKFORGE_CODEX_IMPLEMENTATION_TOKEN_BUDGET=40000
TASKFORGE_CODEX_CORRECTION_TOKEN_BUDGET=20000
TASKFORGE_DOTNET_EXECUTABLE=dotnet
```

## Usage

One-shot execution remains available:

```bash
dotnet run --project src/TaskForge.Cli -- run "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- run --repo ../some-repo "Fix the failing test"
```

For quota-sensitive work, prepare the task without starting Codex:

```bash
dotnet run --project src/TaskForge.Cli -- plan --repo ../some-repo "Fix the failing test"
```

This performs Planner + Explorer locally, stores `task-packet.json`, and stops in
`ReadyToApply` with `CodexRuns = 0`. Apply that exact saved packet later:

```bash
dotnet run --project src/TaskForge.Cli -- apply tf-20261002-120000-a1b2
```

Inspect runs:

```bash
dotnet run --project src/TaskForge.Cli -- runs
dotnet run --project src/TaskForge.Cli -- show <run-id>
```

Set `TASKFORGE_RUNS_DIRECTORY` to override the local run storage directory.

Each run stores its plan, exploration result, task packet, Codex JSONL trace,
build/test logs, diagnosis (if needed), git snapshot, review, and final state.

TaskForge also parses the final `turn.completed.usage` event from each Codex JSONL
stream and accumulates input, cached-input, cache-write, output, and reasoning-output
token counts in `state.json`. The `runs` command shows aggregate input/output usage;
`show <run-id>` shows the detailed breakdown. If Codex terminates without a usage
event, the workflow continues and the counters remain unavailable for that invocation.
