# TaskForge orchestrator design

- **Status:** Active design
- **Updated:** 2026-10-02
- **Architecture decisions:** [ADR-0001](../adr/0001-controlled-multi-agent-architecture.md), [ADR-0002](../adr/0002-workspace-isolation-and-sandbox.md)

Этот документ описывает изменяемый implementation design. Архитектурные инварианты находятся в ADR и не должны дублироваться здесь как отдельные решения.

## 1. Goals

Orchestrator должен:

- дешёво подготовить контекст;
- запускать coding worker только в явно разрешённых точках;
- иметь один канонический workflow;
- жёстко ограничивать число и длительность Codex runs;
- сохранять replayable artifacts;
- отделять LLM reasoning от deterministic control plane;
- не доверять self-reported confidence как управляющему сигналу;
- изолировать side effects в disposable workspace.

## 2. Invariants

1. Только orchestrator меняет workflow state.
2. Каждый запущенный Codex process расходует один run budget.
3. На task допускается максимум два Codex runs: Implementation и, при необходимости, Correction.
4. Correction может быть запрошен только после authoritative build/test failure и local Diagnostic step.
5. Reviewer не запускает correction автоматически.
6. Planner/Explorer/Diagnostic/Reviewer не имеют произвольного shell access.
7. Build/test запускаются orchestrator и являются source of truth.
8. Coding worker не пишет в основной checkout.
9. TaskPacket versioned, validated и bounded.
10. Ни один LLM loop не является неограниченным.

## 3. Canonical state machine

Единственная каноническая схема:

~~~text
Created
  |
  v
Planning
  |
  v
Exploring
  |
  v
ReadyToApply
  |
  v
Implementing -----------+
  |                      |
  v                      |
Building                 |
  | success              |
  v                      |
Testing                  |
  | success              |
  v                      |
Reviewing                 |
  |                       |
  +--> Completed          |
  +--> NeedsUser          |
                          |
Building/Testing failure  |
  |                       |
  v                       |
Diagnosing                |
  |                       |
  +--> NeedsUser          |
  +--> Failed             |
  +--> Correcting --------+
       |
       v
     Building
~~~

Если после Correction build/test снова падает:

- Diagnostic выполняется ещё раз только для классификации результата;
- новый Correction run не разрешается;
- при запросе ещё одного Codex run результат становится `BudgetExceeded`;
- при environment/user problem возможен `NeedsUser`;
- при окончательной deterministic failure — `Failed`.

Terminal states:

~~~text
Completed
BudgetExceeded
NeedsUser
Failed
Cancelled
~~~

`PacketReady` может временно приниматься кодом как legacy compatibility state, но не является отдельным состоянием канонического workflow.

### Что означает terminal result

«Вернуть текущий результат» означает сохранить и показать:

- terminal state;
- task/run id;
- base commit;
- worktree diff;
- changed files;
- build/test результаты;
- diagnosis/review;
- Codex usage;
- путь/идентификатор сохранённых artifacts.

Никакого implicit merge в основной checkout нет.

## 4. Execution tiers

### Tier 0 — deterministic

Используется, когда операция заранее известна и не требует reasoning:

- git metadata;
- file/symbol search;
- diff;
- build;
- test;
- structured file reading;
- context size accounting;
- schema validation;
- write-scope validation.

### Tier 1 — local LLM

Используется для reasoning без source-code side effects:

- Planner;
- Explorer ranking/summarization;
- Diagnostic;
- Reviewer.

Local LLM получает bounded input и structured-output schema.

### Tier 2 — coding worker

Используется для изменения repository.

В MVP единственный Tier-2 backend — Codex CLI worker.

### Routing rules

Routing определяется типом шага workflow, а не фразами вроде «простая» или «сложная задача».

| Operation | Tier | Rule |
| --- | --- | --- |
| repository search | 0 | всегда deterministic first |
| plan/decomposition | 1 | local structured output |
| context selection | 0 + 1 | deterministic candidates, local ranking |
| source edit | 2 | Codex worker в MVP |
| build/test | 0 | orchestrator-owned |
| failure diagnosis | 1 | local structured output |
| correction | 2 | только после Diagnostic и при наличии run budget |
| review | 1 | не может породить новый Codex run |

`Confidence` от local model допускается хранить в trace, но не использовать для эскалации до появления eval dataset и calibration measurements.

## 5. Local model abstraction

Не следует создавать собственный универсальный `ILlmProvider` только ради разных chat backends.

Базовый .NET transport abstraction:

~~~csharp
Microsoft.Extensions.AI.IChatClient
~~~

Поверх него TaskForge может иметь маленький adapter для единой structured-output policy:

~~~csharp
public interface IStructuredChatClient
{
    Task<T> CompleteAsync<T>(
        ChatRequest request,
        CancellationToken cancellationToken);
}
~~~

Adapter отвечает за:

- JSON Schema;
- deserialization;
- schema validation;
- один bounded repair/retry при syntactically invalid structured output;
- telemetry;
- provider/model metadata.

Неудачный structured output после лимита не запускает бесконечный repair loop: workflow переходит в `NeedsUser` или `Failed` в зависимости от шага.

Для Ollama `IChatClient`/adapter должен использовать temperature 0 по умолчанию. Seed сохраняется, если backend его поддерживает.

## 6. Coding worker contract

Codex — не chat provider. Он имеет side effects и отдельный контракт.

~~~csharp
public interface ICodingWorker
{
    Task<CodingWorkerResult> ExecuteAsync(
        CodingTask task,
        WorkspaceHandle workspace,
        WorkerBudget budget,
        CancellationToken cancellationToken);
}
~~~

~~~csharp
public sealed record CodingWorkerResult
{
    public required int ExitCode { get; init; }

    public required IReadOnlyList<string> ChangedFiles { get; init; }

    public required string Diff { get; init; }

    public required string Report { get; init; }

    public WorkerUsage? Usage { get; init; }

    public string? TraceArtifact { get; init; }

    public bool Success => ExitCode == 0;
}
~~~

Codex-specific CLI arguments, JSONL parsing и feature flags находятся внутри infrastructure implementation, например `CodexCliWorker`.

Application layer не должен знать о `codex exec`.

### Local coding worker

Он не входит в MVP.

Если он будет добавлен, допустимы два разных design:

1. bounded agent loop с явным набором tools;
2. генерация patch -> deterministic validation -> `git apply`.

Прямой «Ollama provider, который правит файлы» запрещён как неявная абстракция.

## 7. TaskPacket

TaskPacket — immutable contract между дешёвой подготовкой контекста и дорогим coding worker.

### 7.1 Required fields

Целевая схема:

~~~json
{
  "schemaVersion": 2,
  "taskId": "tf-20261002-120000-a1b2",
  "baseCommit": "0123456789abcdef...",
  "goal": "Fix cleanup after Segment deletion",
  "constraints": [
    "Do not change public API"
  ],
  "acceptanceCriteria": [
    "Deleting Segment removes related bindings",
    "Relevant tests pass"
  ],
  "relevantFiles": [
    "src/Operations/PropertyBindingService.cs"
  ],
  "contextSpans": [
    {
      "path": "src/Operations/PropertyBindingService.cs",
      "startLine": 120,
      "endLine": 210,
      "content": "..."
    }
  ],
  "observations": [
    "DeleteSegmentAsync deletes Segment before PropertyBinding"
  ],
  "writeScope": [
    "src/Operations/**",
    "tests/Operations.Tests/**"
  ],
  "allowedCommands": [
    {
      "tool": "dotnet-test",
      "project": "tests/Operations.Tests",
      "filter": "FullyQualifiedName~DeleteSegment"
    }
  ],
  "testTargets": [
    {
      "project": "tests/Operations.Tests",
      "filter": "FullyQualifiedName~DeleteSegment"
    }
  ],
  "budget": {
    "codexRunsRemaining": 1,
    "runTokenBudget": 40000,
    "packetTokenBudget": 12000
  },
  "diagnostics": null
}
~~~

### 7.2 Paths и excerpts

Передавать только file paths недостаточно: worker вынужден заново читать полные файлы, и экономия контекста становится случайной.

Поэтому TaskPacket содержит оба слоя:

- `relevantFiles` — навигационный набор;
- `contextSpans` — выбранные выдержки, которые обосновывают plan/observations.

Worker имеет право дочитать другие файлы в worktree, если это необходимо для корректной реализации. Каждый такой context expansion логируется.

### 7.3 Packet token budget

До запуска Codex deterministic counter проверяет размер packet/prompt.

Если пакет не помещается:

1. сначала сокращаются excerpts с низким ranking;
2. затем observations объединяются/дедуплицируются;
3. acceptance criteria и policy fields не выбрасываются;
4. если минимальный packet всё равно превышает limit — `NeedsUser`.

LLM не решает самостоятельно увеличить token budget.

### 7.4 Schema validation

Planner/Explorer outputs формируются через structured output и валидируются до TaskPacketFactory.

TaskPacketFactory дополнительно проверяет:

- `schemaVersion`;
- repository-relative normalized paths;
- отсутствие path traversal;
- непустой `baseCommit`;
- write scope;
- budget values;
- общий packet size.

## 8. Context discovery

Главный архитектурный риск — garbage-in: Explorer может не включить критичный файл.

MVP discovery pipeline:

~~~text
Task
 |
 v
Planner search terms
 |
 v
deterministic search
 |-- filenames
 |-- text search
 |-- project references
 |-- tests
 |-- optional symbol/repo map
 |
 v
candidate excerpts
 |
 v
Explorer local LLM
 |
 v
TaskPacket
~~~

Mitigations:

- несколько independent deterministic search strategies;
- candidate cap и explicit ranking;
- выбранные excerpts, а не только filenames;
- worker может boundedly дочитать недостающий context;
- telemetry отмечает additional files;
- correction diagnosis классифицирует «missing context» отдельно.

### NeedsMoreContext

В MVP отдельного циклического `NeedsMoreContext` state нет.

Если позже будет добавлен второй Explorer pass:

~~~text
MaxExplorationPasses = 2
~~~

После исчерпания лимита — `NeedsUser`. Бесконечный Explorer <-> Planner loop запрещён.

## 9. Budget model

Одна архитектурная модель:

~~~csharp
public sealed record CodexBudgetOptions
{
    public int MaxCodexRuns { get; init; } = 2;

    public int MaxConcurrentCodexRuns { get; init; } = 1;

    public TimeSpan CodexRunTimeout { get; init; } =
        TimeSpan.FromMinutes(20);

    public int ImplementationTokenBudget { get; init; } = 40_000;

    public int CorrectionTokenBudget { get; init; } = 20_000;
}
~~~

Нет отдельного `MaxRetries`.

### Run semantics

Run budget списывается непосредственно перед process launch и не возвращается при:

- non-zero exit code;
- timeout;
- cancellation после фактического запуска;
- malformed worker output.

Это делает worst-case consumption предсказуемым.

### Concurrency

Global gate ограничивает одновременные Codex processes.

Первый default:

~~~text
MaxConcurrentCodexRuns = 1
~~~

Зависший process не держит slot бесконечно, потому что каждый run имеет timeout и process-tree kill.

### Token usage

Если backend выдаёт machine-readable usage:

- usage сохраняется по каждому run;
- агрегируется в run metadata;
- используется для metrics;
- отсутствие usage event не ломает workflow, но отмечается как missing telemetry.

Per-run token budget и observed usage — разные понятия.

## 10. Codex launch policy

Launcher формирует effective policy сам и не полагается на user-level Codex config.

Required properties:

- multi-agent disabled;
- non-interactive exec mode;
- workspace-write sandbox;
- network deny by default;
- explicit approval policy;
- ephemeral session, если backend поддерживает;
- JSON/event output;
- external timeout.

Prompt с `Do not spawn subagents` остаётся вторым слоем.

Integration tests должны проверять launch arguments/config для поддерживаемой версии Codex CLI.

## 11. Workspace lifecycle

Подробности — ADR-0002.

Application abstraction:

~~~csharp
public interface IWorkspaceManager
{
    Task<WorkspaceHandle> CreateAsync(
        string repositoryPath,
        string baseCommit,
        TaskId taskId,
        CancellationToken cancellationToken);

    Task<WorkspaceSnapshot> SnapshotAsync(
        WorkspaceHandle workspace,
        CancellationToken cancellationToken);

    Task CleanupAsync(
        WorkspaceHandle workspace,
        CancellationToken cancellationToken);
}
~~~

`WorkspaceSnapshot` включает:

- changed files;
- diff;
- untracked files;
- write-scope violations.

Apply phase никогда не использует исходный checkout как worker cwd.

## 12. Build/test ownership

Codex может получить разрешение на targeted checks, но они не влияют напрямую на state.

После worker:

1. TaskForge получает workspace snapshot;
2. валидирует write scope;
3. запускает authoritative build;
4. запускает targeted tests;
5. при необходимости запускает более широкий test set;
6. только эти результаты передаются в Diagnostic/Reviewer.

Для .NET restore должен быть отдельным deterministic шагом. В изолированном режиме build/test предпочтительно выполняются с `--no-restore`.

## 13. Diagnostic

Diagnostic получает bounded input:

- TaskPacket;
- failing command identity;
- truncated stdout/stderr;
- failing test names;
- current diff summary;
- changed files.

Structured result:

~~~csharp
public enum DiagnosticAction
{
    RequestCorrection,
    NeedsUser,
    Fail
}
~~~

~~~csharp
public sealed record DiagnosticResult(
    DiagnosticAction Action,
    string Summary,
    IReadOnlyList<string> Evidence);
~~~

Diagnostic не изменяет файлы в MVP.

Даже если он возвращает `RequestCorrection`, окончательное решение принимает orchestrator после проверки state и remaining budget.

## 14. Review

Reviewer получает:

- goal;
- acceptance criteria;
- TaskPacket;
- authoritative build/test results;
- changed-files list;
- bounded diff.

Reviewer возвращает findings и recommendation.

Он не запускает worker.

Если build/tests успешны, но review не может подтвердить приемлемость результата, terminal state — `NeedsUser`, а не автоматический третий run.

## 15. Security model

Security assumptions определены ADR-0002.

Основные правила:

- repository content — untrusted data;
- local reasoning agents не имеют arbitrary tools;
- secrets не включаются в automatic context;
- Codex и build/test исполняются в изолированном workspace;
- worktree не считается host sandbox;
- network deny by default;
- write scope проверяется после worker;
- command allowlist должен быть enforceable там, где orchestrator владеет process launch.

До появления полноценного sandbox для build/test TaskForge предназначен только для trusted repositories.

## 16. Reproducibility artifacts

Для каждого run сохраняются:

~~~text
request.json
plan.json
exploration.json
task-packet.json
workflow-state.json
workspace.json
prompt-manifest.json
codex-01.jsonl
codex-01-result.json
build-01.*
test-01.*
diagnostic-01.json
codex-02.jsonl
codex-02-result.json
build-02.*
test-02.*
review.json
result.json
~~~

`prompt-manifest.json` должен содержать:

- prompt template id/version/hash;
- local model/provider;
- local inference parameters;
- Codex model/config if explicitly known;
- Codex CLI version;
- effective launch policy;
- seed or `null`;
- tool versions where practical.

Reproducibility здесь означает воспроизводимость входов и control flow, а не deterministic LLM output.

## 17. Observability and success metrics

Минимальные task-level metrics:

- `codex_runs_per_task`;
- `codex_input_tokens_per_task`;
- `codex_output_tokens_per_task`;
- `codex_weighted_tokens_per_task`, если backend это предоставляет;
- `correction_rate`;
- `first_pass_verification_success_rate`;
- `budget_exhausted_rate`;
- `needs_user_rate`;
- `codex_timeout_rate`;
- `missing_usage_rate`;
- `context_expansion_file_count`;
- `missing_context_correction_rate`;
- durations по каждому state.

Routing policy меняется только после накопления eval/production measurements, а не по subjective `Confidence`.

## 18. Ready-made frameworks

### Microsoft.Extensions.AI

Использовать как стандартную .NET chat abstraction для local reasoning backends и middleware/telemetry. Это уменьшает необходимость поддерживать собственный generic LLM provider.

### Microsoft Agent Framework

Является основным кандидатом на замену самописного workflow runtime при росте сложности. Его explicit workflow model соответствует TaskForge лучше, чем fully autonomous coordinator.

Пока не принимается как dependency orchestration core, потому что текущая state machine мала, а budget/workspace policy остаётся TaskForge-specific независимо от framework.

### Semantic Kernel

Не является первым выбором для новой workflow orchestration, но может оставаться источником integrations. При пересмотре следует сравнивать его с актуальным Microsoft Agent Framework, а не оценивать изолированно.

### LangGraph

Архитектурно подходит для explicit graph + state, но требует отдельного Python/TypeScript runtime вокруг .NET-first приложения.

### OpenHands

Подходит как полноценный coding environment/harness, но слишком широк для роли контролируемого leaf worker внутри текущего TaskForge.

### Aider

Не заменяет orchestrator, но его repo-map/context-budgeting подход полезен для будущего Explorer. Возможная отдельная задача — сравнить current deterministic discovery с repo-map подходом на eval corpus.

## 19. Current implementation gaps

На 2026-10-02 код уже имеет:

- deterministic state machine;
- local Planner/Explorer/Diagnostic/Reviewer;
- structured Ollama output;
- two-run implementation/correction workflow;
- Codex multi-agent disable;
- per-run timeout;
- process-tree kill;
- Codex JSONL usage parsing;
- plan/apply split;
- persistent run artifacts.

Для соответствия ADR остаются изменения:

1. ввести `IWorkspaceManager` и git worktree lifecycle;
2. добавить `baseCommit`, `schemaVersion`, `writeScope`, context spans, command policy и budgets в TaskPacket;
3. заменить `MaxCodexRunsPerTask + MaxCodexRetries` на одну семантику `MaxCodexRuns`;
4. заменить application-level `ICodexClient/IImplementerAgent` на `ICodingWorker`, оставив Codex CLI внутри infrastructure;
5. перейти с собственного generic local client к `Microsoft.Extensions.AI.IChatClient` или adapter поверх него;
6. добавить write-scope validation;
7. сделать approval/network policy launch-time explicit;
8. сохранить prompt/model/tool manifest;
9. добавить metrics для context quality и terminal-state rates;
10. изолировать build/test process boundary либо явно оставить trusted-repository limitation до реализации.

Эти gaps являются implementation backlog, а не альтернативными архитектурными вариантами.

## 20. References

- Microsoft.Extensions.AI: https://learn.microsoft.com/dotnet/core/extensions/artificial-intelligence
- Microsoft Agent Framework overview: https://learn.microsoft.com/agent-framework/overview
- Agent Framework workflows: https://learn.microsoft.com/agent-framework/concepts/workflows/
- LangGraph: https://docs.langchain.com/oss/
- OpenHands: https://docs.openhands.dev/
- Aider repository map: https://aider.chat/docs/repomap.html
