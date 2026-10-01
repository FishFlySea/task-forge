# TaskForge

TaskForge is a local orchestrator for controlled AI-assisted software development.

Architecture:

- [ADR-0001](docs/adr/0001-controlled-multi-agent-architecture.md)
- [RFC-0001](docs/rfc/0001-mvp-implementation.md)

## Current status

Phase 1 and Phase 2 foundations are implemented:

- .NET 10 solution;
- explicit workflow state machine;
- local JSON run store and artifacts;
- local Ollama structured-output client;
- Planner agent;
- deterministic repository search;
- Explorer agent;
- TaskPacket generation;
- CLI commands `run`, `runs`, and `show`;
- tests and GitHub Actions CI.

The current workflow intentionally stops at `NeedsUser` after producing
`task-packet.json`. Codex implementation is Phase 3.

## Requirements

- .NET 10 SDK
- Ollama reachable from the machine running TaskForge
- a configured local coding model

Defaults:

```text
TASKFORGE_OLLAMA_URL=http://localhost:11434/
TASKFORGE_OLLAMA_MODEL=qwen3-coder
TASKFORGE_OLLAMA_TIMEOUT_SECONDS=120
```

## Usage

```bash
dotnet run --project src/TaskForge.Cli -- run "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- run --repo ../some-repo "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- runs
dotnet run --project src/TaskForge.Cli -- show <run-id>
```

Set `TASKFORGE_RUNS_DIRECTORY` to override the local run storage directory.
