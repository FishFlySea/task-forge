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

TaskForge launches Codex with an explicit command-line policy rather than relying on
user defaults: multi-agent is disabled, the sandbox is `workspace-write`, approval
policy is `never` for unattended execution, sandboxed shell network access is
disabled, and the built-in web-search tool is disabled. User-level MCP servers are a
separate configuration surface and are not yet isolated by TaskForge.

Codex execution uses one shared budget model for max runs, concurrency, timeout and
per-kind rollout/token limits. There is no separate retry counter: the default
`MaxCodexRuns=2` means one Implementation plus at most one Correction. JSONL usage
is persisted when available.

Apply runs now use disposable detached Git worktrees created from the exact
`baseCommit` stored by `plan`. Planning requires a clean source checkout so the
selected context matches that commit. Codex, authoritative build/test, diagnostics,
and review operate in the isolated worktree; final workspace status/diff is persisted
before cleanup.

TaskPacket schema v3 now carries a deterministic write scope, bounded prepared
context, typed verification command policy, and a snapshot of the maximum Codex
execution budget captured at plan time.
After Explorer selects relevant files, a deterministic context collector extracts
line ranges around the plan's search terms. The default context budget is 32,000
characters with at most 8,000 characters per span; the packet records actual
characters and an approximate token count. Codex receives these excerpts before it
decides whether additional file reads are necessary.

At apply time, the current Codex configuration may be equal to or stricter than the
saved execution budget, but it may not be more permissive. The command policy
explicitly authorizes the orchestrator-owned `DotnetBuild` and targeted
`DotnetTest` operations.

After every Codex coding run, TaskForge snapshots changed files before build/test and
rejects out-of-scope writes as `NeedsUser`; policy violations never receive an
automatic corrective Codex run.

The architecture review still has additional hardening gaps: isolation from user-level
Codex MCP/config surfaces, prompt/model/tool manifests, an environment allowlist, and
a common host sandbox boundary for build/test.

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
TASKFORGE_CODEX_MAX_RUNS=2
TASKFORGE_CODEX_MAX_CONCURRENT_RUNS=1
TASKFORGE_CODEX_TIMEOUT_SECONDS=1200
TASKFORGE_CODEX_IMPLEMENTATION_TOKEN_BUDGET=40000
TASKFORGE_CODEX_CORRECTION_TOKEN_BUDGET=20000
TASKFORGE_PACKET_MAX_CONTEXT_CHARS=32000
TASKFORGE_PACKET_MAX_SPAN_CHARS=8000
TASKFORGE_PACKET_APPROX_CHARS_PER_TOKEN=4
TASKFORGE_DOTNET_EXECUTABLE=dotnet
TASKFORGE_WORKTREES_DIRECTORY=<OS local app data>/TaskForge/worktrees
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

This requires a clean Git working tree, records the exact HEAD as `baseCommit`,
performs Planner + Explorer locally, stores `task-packet.json`, and stops in
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
shows acceptance criteria, relevant files, prepared context line ranges, context
budget, observations, write scope and test targets, and reports whether the run is
currently safe to pass to `apply`.

Set `TASKFORGE_RUNS_DIRECTORY` to override the local run storage directory.

Each run stores its plan, exploration result, task packet, Codex JSONL trace,
build/test logs, diagnosis (if needed), git snapshot, review, and final state.

TaskForge parses the final usage event from Codex JSONL when present and
accumulates input, cached-input, cache-write, output, and reasoning-output token
counts in `state.json`. Missing usage telemetry does not fail the workflow.
