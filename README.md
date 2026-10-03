# TaskForge

TaskForge is a local orchestrator for controlled AI-assisted software development.

Architecture:

- [ADR-0001 — Controlled AI-worker orchestration](docs/adr/0001-controlled-multi-agent-architecture.md)
- [ADR-0002 — Workspace isolation and sandbox](docs/adr/0002-workspace-isolation-and-sandbox.md)
- [Orchestrator design](docs/design/orchestrator.md)
- [RFC-0001 — historical MVP proposal](docs/rfc/0001-mvp-implementation.md)

## Current status

The first end-to-end execution workflow is implemented:

~~~text
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
~~~

TaskForge launches Codex with the multi-agent feature explicitly disabled.
Each Codex invocation has an external timeout and rollout/token budget, and JSONL
usage is persisted when available.

The architecture review identified additional hardening that is now normative in
ADR-0001/0002 but is not fully implemented yet: disposable git worktrees,
versioned TaskPacket policy fields, unified run-budget semantics, write-scope
validation, and a common sandbox boundary for build/test.

**Until ADR-0002 is fully implemented, run TaskForge only against repositories
you trust.** A git worktree alone will not be treated as a host security boundary.

## Requirements

- .NET 10 SDK
- Git
- Ollama reachable from the TaskForge host
- a local coding/reasoning model in Ollama
- Codex CLI installed and authenticated

Defaults:

~~~text
TASKFORGE_OLLAMA_URL=http://localhost:11434/
TASKFORGE_OLLAMA_MODEL=qwen3-coder
TASKFORGE_OLLAMA_TIMEOUT_SECONDS=120
TASKFORGE_CODEX_EXECUTABLE=codex
TASKFORGE_CODEX_TIMEOUT_SECONDS=1200
TASKFORGE_CODEX_IMPLEMENTATION_TOKEN_BUDGET=40000
TASKFORGE_CODEX_CORRECTION_TOKEN_BUDGET=20000
TASKFORGE_DOTNET_EXECUTABLE=dotnet
~~~

## Usage

One-shot execution remains available:

~~~bash
dotnet run --project src/TaskForge.Cli -- run "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- run --repo ../some-repo "Fix the failing test"
~~~

For quota-sensitive work, prepare the task without starting Codex:

~~~bash
dotnet run --project src/TaskForge.Cli -- plan --repo ../some-repo "Fix the failing test"
~~~

This performs Planner + Explorer locally, stores `task-packet.json`, and stops in
`ReadyToApply` with `CodexRuns = 0`. Apply that exact saved packet later:

~~~bash
dotnet run --project src/TaskForge.Cli -- apply tf-20261002-120000-a1b2
~~~

Inspect runs:

~~~bash
dotnet run --project src/TaskForge.Cli -- runs
dotnet run --project src/TaskForge.Cli -- show <run-id>
dotnet run --project src/TaskForge.Cli -- inspect <run-id>
~~~

`show` displays run metadata and Codex usage. `inspect` is a zero-model-cost
preflight view: it reads the saved request, plan, exploration result and TaskPacket,
shows acceptance criteria, relevant files, observations and test targets, and reports
whether the run is currently safe to pass to `apply`.

Set `TASKFORGE_RUNS_DIRECTORY` to override the local run storage directory.

Each run stores its plan, exploration result, task packet, Codex JSONL trace,
build/test logs, diagnosis (if needed), git snapshot, review, and final state.

TaskForge parses the final usage event from Codex JSONL when present and
accumulates input, cached-input, cache-write, output, and reasoning-output token
counts in `state.json`. Missing usage telemetry does not fail the workflow.
