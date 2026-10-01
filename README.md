# TaskForge

Local orchestrator for controlled AI-assisted software development.

Architecture:

- [ADR-0001](docs/adr/0001-controlled-multi-agent-architecture.md)
- [RFC-0001](docs/rfc/0001-mvp-implementation.md)

## Current status

Phase 1 skeleton:

- .NET 10 solution;
- workflow state machine;
- local JSON run store;
- CLI commands `run`, `runs`, `show`;
- tests and CI.

The current `run` command intentionally stops at `NeedsUser`.
Ollama Planner/Explorer integration is Phase 2.

## Usage

```bash
dotnet run --project src/TaskForge.Cli -- run "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- run --repo ../some-repo "Fix the failing test"
dotnet run --project src/TaskForge.Cli -- runs
dotnet run --project src/TaskForge.Cli -- show <run-id>
```

Set `TASKFORGE_RUNS_DIRECTORY` to override the local run storage directory.
