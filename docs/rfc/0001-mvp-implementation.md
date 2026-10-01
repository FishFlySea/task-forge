# RFC-0001: MVP реализации TaskForge

- **Статус:** Implemented
- **Дата:** 2026-10-01
- **Связанный ADR:** [ADR-0001](../adr/0001-controlled-multi-agent-architecture.md)
- **Стек:** .NET 10, Ollama, Codex CLI, Git, dotnet CLI

## 1. Цель

Реализовать минимальную рабочую версию TaskForge — локального оркестратора для AI-assisted разработки, который:

- принимает задачу пользователя;
- исследует репозиторий дешёвыми средствами;
- формирует компактный TaskPacket;
- вызывает Codex только для реализации сложной части;
- запускает build/test самостоятельно;
- анализирует ошибки локальной моделью;
- допускает не более одного corrective Codex run;
- сохраняет trace выполнения и расход бюджета.

MVP должен доказать основную гипотезу ADR-0001:

> мультиагентность полезна, если координация и подготовка контекста выполняются дешёво, а Codex используется как ограниченный implementation worker.

## 2. Не входит в MVP

На первом этапе не реализуются:

- Web UI;
- HTTP API;
- постоянный daemon;
- PostgreSQL/SQLite;
- распределённая очередь задач;
- несколько одновременно изменяющих один репозиторий workflow;
- автоматический push;
- создание Pull Request;
- merge;
- рекурсивные агенты;
- самостоятельный spawn Codex subagents;
- сложный model router;
- удалённое выполнение worker'ов;
- GitHub webhook integration.

## 3. Пользовательский сценарий

Основной интерфейс MVP — CLI.

Пример:

```bash
taskforge run \
  --repo C:\Projects\Operations \
  "После удаления Segment остаются PropertyBinding. Исправь и добавь тест."
```

Или из текущего каталога:

```bash
cd C:\Projects\Operations
taskforge run "Исправь удаление связанных PropertyBinding"
```

Результат:

```text
TaskForge run tf-20261001-001

[planner]     completed
[explorer]    completed
[codex 1/2]   completed
[build]       success
[test]        failed
[diagnostic]  corrective run required
[codex 2/2]   completed
[test]        success
[review]      completed

Result: completed
Changed files: 3
Codex runs: 2/2
```

## 4. Структура solution

Предлагаемая структура:

```text
TaskForge.slnx

src/
  TaskForge.Core/
  TaskForge.Application/
  TaskForge.Infrastructure/
  TaskForge.Cli/

tests/
  TaskForge.Core.Tests/
  TaskForge.Application.Tests/
  TaskForge.Infrastructure.Tests/

docs/
  adr/
  rfc/
```

### TaskForge.Core

Не зависит от инфраструктуры.

Содержит:

- TaskId;
- TaskRequest;
- TaskPacket;
- AgentResult;
- WorkflowState;
- Budget;
- доменные enum/value objects.

### TaskForge.Application

Содержит orchestration:

- TaskOrchestrator;
- PlannerAgent;
- ExplorerAgent;
- DiagnosticAgent;
- ReviewAgent;
- workflow transitions;
- budget enforcement;
- prompt composition.

### TaskForge.Infrastructure

Содержит реализации внешних интеграций:

- OllamaClient;
- CodexCliClient;
- GitClient;
- DotnetClient;
- FileSearch;
- ProcessRunner;
- RunStore.

### TaskForge.Cli

Содержит:

- System.CommandLine/ручной CLI parsing;
- DI bootstrap;
- configuration;
- console rendering;
- exit codes.

MVP не должен вводить отдельный ASP.NET Core host без необходимости.

## 5. Основные модели

### TaskRequest

```csharp
public sealed record TaskRequest(
    string Goal,
    string RepositoryPath);
```

Позже сюда могут быть добавлены:

- branch;
- constraints;
- explicit acceptance criteria;
- max budget;
- preferred model.

### TaskPacket

TaskPacket является контрактом между дешёвой фазой исследования и дорогим implementation worker.

```csharp
public sealed record TaskPacket
{
    public required string Goal { get; init; }

    public IReadOnlyList<string> Constraints { get; init; } = [];

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    public IReadOnlyList<string> RelevantFiles { get; init; } = [];

    public IReadOnlyList<string> Observations { get; init; } = [];

    public IReadOnlyList<TestTarget> TestTargets { get; init; } = [];

    public string? Diagnostics { get; init; }
}
```

TaskPacket сериализуется в JSON и сохраняется в trace run.

## 6. Workflow state machine

Используется явная state machine.

```text
Created
   │
   ▼
Planning
   │
   ▼
Exploring
   │
   ▼
PacketReady
   │
   ▼
Implementing
   │
   ▼
Building
   │
   ├─────────────── failure ───────────────┐
   ▼                                       │
Testing                                    │
   │                                       │
   ├── success ──► Reviewing ──► Completed │
   │                                       │
   └── failure ──► Diagnosing ◄────────────┘
                        │
              ┌─────────┴─────────┐
              │                   │
        local fix possible   Codex required
              │                   │
              ▼                   ▼
          Implementing        Correcting
                                  │
                                  ▼
                               Building
```

Terminal states:

```csharp
public enum WorkflowState
{
    Created,
    Planning,
    Exploring,
    PacketReady,
    Implementing,
    Building,
    Testing,
    Diagnosing,
    Correcting,
    Reviewing,

    Completed,
    Failed,
    BudgetExceeded,
    Cancelled,
    NeedsUser
}
```

Переходы выполняются только оркестратором.

Агент не может самостоятельно менять state.

## 7. Контракт агента

```csharp
public interface IAgent<in TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(
        TInput input,
        CancellationToken cancellationToken);
}
```

Для MVP предпочтительнее типизированные контракты, а не универсальный:

```csharp
IAgent<AgentContext, AgentResult>
```

Например:

```csharp
public interface IPlannerAgent
{
    Task<PlanResult> PlanAsync(
        TaskRequest request,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IExplorerAgent
{
    Task<ExplorationResult> ExploreAsync(
        TaskRequest request,
        PlanResult plan,
        CancellationToken cancellationToken);
}
```

Это уменьшает количество runtime casts и делает workflow проще тестировать.

## 8. Planner

Planner работает через Ollama.

Вход:

- исходная задача;
- имя/тип репозитория;
- минимальная информация о solution.

Выход должен быть structured JSON:

```json
{
  "summary": "Fix cascading cleanup of bindings",
  "searchTerms": [
    "PropertyBinding",
    "DeleteSegment",
    "SegmentId"
  ],
  "likelyAreas": [
    "services",
    "entity configuration",
    "tests"
  ],
  "acceptanceCriteria": [
    "Deleting Segment does not leave related bindings",
    "Relevant tests pass"
  ]
}
```

Planner не читает весь репозиторий самостоятельно.

## 9. Explorer

Explorer использует два слоя:

### 9.1 Deterministic discovery

Сначала запускаются:

- поиск файлов;
- `rg`;
- `git grep`;
- поиск символов;
- список проектов;
- поиск тестов.

LLM получает только результаты этого поиска.

### 9.2 Local reasoning

Ollama анализирует найденные фрагменты и возвращает:

```csharp
public sealed record ExplorationResult
{
    public IReadOnlyList<string> RelevantFiles { get; init; } = [];

    public IReadOnlyList<string> Observations { get; init; } = [];

    public IReadOnlyList<TestTarget> TestTargets { get; init; } = [];

    public double Confidence { get; init; }
}
```

Explorer должен иметь ограничения:

```text
MaxCandidateFiles = 20
MaxRelevantFiles = 8
MaxFileBytesPerFile = 64 KB
MaxTotalContextBytes = 256 KB
```

Конкретные значения конфигурируемые.

## 10. Ollama integration

Для локальных агентов используется HTTP API Ollama.

По умолчанию:

```text
POST http://localhost:11434/api/chat
```

Используется:

```json
{
  "model": "...",
  "messages": [],
  "stream": false,
  "format": { }
}
```

Для Planner, Explorer, Diagnostic и Reviewer следует использовать structured outputs через JSON Schema.

Провайдер:

```csharp
public interface ILocalLlmClient
{
    Task<T> CompleteStructuredAsync<T>(
        LocalLlmRequest request,
        CancellationToken cancellationToken);
}
```

Первая реализация:

```csharp
OllamaClient : ILocalLlmClient
```

Настройки:

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3-coder",
    "Timeout": "00:02:00"
  }
}
```

Имя модели не является частью архитектурного контракта и должно быть конфигурируемым.

## 11. Codex integration

В MVP Codex вызывается как внешний процесс через Codex CLI.

Причины:

- не требуется собственная реализация agent loop;
- Codex уже умеет работать с локальным workspace;
- процесс легко ограничить по времени и числу запусков;
- stdout/stderr можно сохранить как trace;
- интеграцию позже можно заменить на API без изменения orchestration слоя.

Интерфейс:

```csharp
public interface ICodexClient
{
    Task<CodexRunResult> ExecuteAsync(
        CodexRunRequest request,
        CancellationToken cancellationToken);
}
```

Реализация:

```csharp
CodexCliClient : ICodexClient
```

Запуск выполняется через `ProcessStartInfo`, без промежуточного shell script.

Рабочий каталог устанавливается в repository workspace.

Для автоматизации используется `codex exec`.

Рекомендуемый режим трассировки:

```bash
codex exec --json --full-auto "<prompt>"
```

JSONL stdout сохраняется как artifact run и может использоваться для подсчёта tool calls и token usage.

`multi_agent` отключается CLI-флагом с максимальным приоритетом над пользовательской конфигурацией, поэтому Codex worker технически не может порождать subagents. `workspace-write` даёт worker право изменять рабочее дерево без выдачи полного доступа к машине. `--ephemeral` не сохраняет rollout-сессию после выполнения. Дополнительно каждый вызов включает `features.rollout_budget` через CLI overrides; в MVP используются отдельные лимиты 40000 weighted tokens для implementation и 20000 для correction. Таймаут процесса остаётся независимым внешним ограничителем.

## 12. Prompt Codex worker

Prompt собирается TaskForge и должен быть коротким и предсказуемым.

Каркас:

```text
You are the implementation worker for TaskForge.

Rules:
- Do not spawn subagents.
- Do not delegate this task to other agents.
- Work in the current repository only.
- Do not perform broad repository exploration.
- Prefer the supplied relevant files.
- Inspect additional files only when necessary.
- Do not commit, push, create branches, or create pull requests.
- Keep changes limited to the requested task.

Goal:
{goal}

Acceptance criteria:
{acceptanceCriteria}

Relevant files:
{relevantFiles}

Observations:
{observations}

Diagnostics:
{diagnostics}

After implementation:
1. summarize changed files;
2. report tests you ran;
3. report unresolved issues;
4. stop.
```

## 13. Budget enforcement

Budget проверяется до запуска Codex, а не после него.

```csharp
public sealed record AgentBudgetOptions
{
    public int MaxCodexRunsPerTask { get; init; } = 2;

    public int MaxCodexRetries { get; init; } = 1;

    public int MaxConcurrentCodexRuns { get; init; } = 1;
}
```

Для процесса используется отдельный gate:

```csharp
public interface ICodexRunGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        TaskId taskId,
        CancellationToken cancellationToken);
}
```

Первая реализация может использовать:

```csharp
SemaphoreSlim(1, 1)
```

### Правило

Первый run имеет тип:

```text
Implementation
```

Второй разрешён только как:

```text
Correction
```

Третий Codex run в рамках одной задачи запрещён.

## 14. Build/Test execution

Сборка и тесты не должны выполняться LLM, если команда уже известна.

Для MVP:

1. определить solution/project;
2. выполнить build;
3. выполнить test.

Абстракция:

```csharp
public interface IDotnetRunner
{
    Task<ProcessResult> BuildAsync(
        string workspace,
        CancellationToken cancellationToken);

    Task<ProcessResult> TestAsync(
        string workspace,
        IReadOnlyList<TestTarget> targets,
        CancellationToken cancellationToken);
}
```

Если Explorer определил конкретный тестовый проект, сначала запускаются релевантные тесты.

После успеха допускается общий `dotnet test`, если это не слишком дорого по времени.

## 15. Diagnostic loop

При падении build/test:

```text
ProcessResult
     │
     ▼
DiagnosticAgent (Ollama)
     │
     ├── EnvironmentFailure
     ├── LocalFix
     ├── CodexCorrectionRequired
     └── NeedsUser
```

Модель результата:

```csharp
public enum DiagnosticAction
{
    RetryWithoutChanges,
    ApplyLocalFix,
    RequestCodexCorrection,
    NeedsUser,
    Fail
}
```

Diagnostic Agent получает:

- последние строки stdout/stderr;
- failing test names;
- текущий git diff;
- исходный TaskPacket.

Полный build log не передаётся в LLM автоматически.

## 16. Local fixes

В MVP local fix должен быть ограничен безопасными изменениями.

Первый вариант:

- local Diagnostic Agent **не изменяет код**;
- он только решает, нужен ли corrective Codex run.

Таким образом MVP workflow проще:

```text
failure
   ↓
Ollama diagnosis
   ↓
Codex correction OR stop
```

Автоматическое изменение кода Ollama можно добавить отдельным RFC.

## 17. Review

После успешных тестов Reviewer получает:

- Goal;
- AcceptanceCriteria;
- TaskPacket;
- `git diff --stat`;
- полный diff в пределах лимита;
- результаты build/test.

Выход:

```csharp
public sealed record ReviewResult
{
    public required bool Acceptable { get; init; }

    public IReadOnlyList<string> Findings { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
```

Reviewer в MVP работает через Ollama.

Он не запускает corrective Codex run автоматически.

Если тесты успешны, но review считает изменение сомнительным, итоговый статус:

```text
NeedsUser
```

Это предотвращает расход дополнительной квоты на субъективный review loop.

## 18. Состояние run

Для MVP не нужна БД.

Каждый запуск создаёт каталог состояния вне изменяемых source-файлов:

```text
~/.taskforge/runs/
  tf-20261001-120501-a1b2/
    request.json
    plan.json
    exploration.json
    task-packet.json
    state.json
    codex-01.jsonl
    build-01.log
    test-01.log
    diagnostic.json
    codex-02.jsonl
    review.json
    result.json
```

На Windows:

```text
%LOCALAPPDATA%\TaskForge\runs\
```

Это позволяет:

- разбирать неудачные workflow;
- считать реальный расход;
- воспроизводить bugs;
- позже построить UI без изменения основного workflow.

## 19. Run metadata

```csharp
public sealed record RunMetadata
{
    public required TaskId Id { get; init; }

    public required string RepositoryPath { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public WorkflowState State { get; init; }

    public int CodexRuns { get; init; }

    public long? CodexInputTokens { get; init; }

    public long? CodexOutputTokens { get; init; }
}
```

Token usage заполняется, если присутствует в JSONL trace Codex.

Отсутствие token usage не должно ломать workflow.

## 20. Отмена

Весь workflow принимает один `CancellationToken`.

При Ctrl+C:

1. отменяется текущий HTTP request/process;
2. дочерний Codex/dotnet процесс завершается;
3. state сохраняется как `Cancelled`;
4. никакой автоматический retry не выполняется.

## 21. Безопасность

MVP запускает coding agent на локальном repository checkout, поэтому применяются следующие правила:

- Codex не получает право push;
- TaskForge не вызывает `git push`;
- TaskForge не вызывает `git reset --hard`;
- TaskForge не выполняет автоматический merge;
- секреты не включаются в TaskPacket;
- содержимое `.env`, secret stores и credentials исключается из автоматического context collection;
- process arguments передаются через `ArgumentList`, а не собираются shell-конкатенацией;
- repository path нормализуется и проверяется;
- timeout задаётся для каждого внешнего процесса.

## 22. Конфигурация

Пример `appsettings.json`:

```json
{
  "TaskForge": {
    "RunsDirectory": null,
    "MaxCandidateFiles": 20,
    "MaxRelevantFiles": 8,
    "MaxContextBytes": 262144
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3-coder",
    "Timeout": "00:02:00"
  },
  "Codex": {
    "Executable": "codex",
    "Timeout": "00:20:00",
    "MaxRunsPerTask": 2,
    "MaxConcurrentRuns": 1
  },
  "Dotnet": {
    "Executable": "dotnet",
    "BuildTimeout": "00:10:00",
    "TestTimeout": "00:20:00"
  }
}
```

Environment variables должны иметь приоритет над JSON configuration.

## 23. CLI commands MVP

Минимально:

```text
taskforge run <task>
taskforge runs
taskforge show <run-id>
```

### run

Запускает новый workflow.

### runs

Показывает последние локальные runs:

```text
ID                           STATE       CODEX
tf-20261001-120501-a1b2      Completed   1/2
tf-20261001-114203-c3d4      Failed      2/2
```

### show

Показывает summary конкретного run.

## 24. Exit codes

```text
0  Completed
1  Failed
2  NeedsUser
3  BudgetExceeded
4  Cancelled
5  ConfigurationError
```

## 25. Тестирование TaskForge

### Unit tests

Покрыть:

- state transitions;
- budget guard;
- TaskPacket composition;
- context size limiting;
- diagnostic routing;
- CLI exit code mapping.

### Integration tests

Использовать fake providers:

```text
FakeOllamaClient
FakeCodexClient
FakeDotnetRunner
```

Основной happy path должен тестироваться без реального LLM.

Пример:

```text
Created
→ Planning
→ Exploring
→ Implementing
→ Building
→ Testing
→ Reviewing
→ Completed
```

Отдельно:

```text
test failure
→ Diagnosing
→ Correcting
→ Testing
→ Completed
```

И:

```text
second correction requested
→ BudgetExceeded
```

### Optional live tests

Live Ollama/Codex tests должны быть opt-in и не запускаться обычным `dotnet test`.

Например category:

```text
Live
```

## 26. Порядок реализации

### Phase 1 — Skeleton

- solution/projects;
- configuration;
- CLI `run`;
- RunStore;
- ProcessRunner;
- workflow state model.

### Phase 2 — Local analysis

- OllamaClient;
- Planner;
- deterministic search;
- Explorer;
- structured TaskPacket.

### Phase 3 — Codex worker

- CodexCliClient;
- JSONL trace capture;
- budget guard;
- implementation prompt.

### Phase 4 — Verification loop

- DotnetRunner;
- DiagnosticAgent;
- single correction path;
- Reviewer.

### Phase 5 — usability

- `runs`;
- `show`;
- improved console output;
- README;
- bootstrap/install instructions.

## 27. Definition of Done

MVP считается готовым, когда TaskForge способен на тестовом .NET репозитории:

1. принять natural-language задачу через CLI;
2. получить план через локальную Ollama;
3. автоматически найти релевантные файлы;
4. сформировать и сохранить TaskPacket;
5. вызвать Codex ровно один раз для реализации;
6. запустить `dotnet build/test`;
7. при ошибке выполнить локальную диагностику;
8. при необходимости выполнить максимум один corrective Codex run;
9. выполнить локальный review;
10. сохранить полный trace;
11. завершиться с корректным exit code;
12. никогда не превысить configured Codex run budget.

## 28. Открытые вопросы после MVP

Следующие решения должны быть вынесены в отдельные ADR/RFC:

- git worktree isolation;
- выбор дефолтной локальной модели;
- возможность локального implementer;
- persistent queue;
- daemon/API architecture;
- GitHub integration;
- automatic PR creation;
- remote workers;
- per-task monetary/token budgets;
- model benchmarking/evals;
- semantic code search/indexing.

## 29. Внешние интерфейсы, на которые опирается MVP

Codex CLI поддерживает non-interactive `codex exec`; `--json` выдаёт JSONL trace, а `--full-auto` разрешает изменяющий файлы автоматизированный run.

Ollama предоставляет локальный HTTP API по адресу `http://localhost:11434/api`; endpoint `POST /api/chat` поддерживает non-streaming ответы и structured output через JSON Schema.

Эти детали изолируются инфраструктурными адаптерами и не должны проникать в Application/Core.

## 30. Итоговое решение RFC

Первую версию TaskForge реализовать как локальное .NET 10 CLI-приложение с явной state machine.

Workflow:

```text
CLI
 ↓
Planner (Ollama)
 ↓
Explorer (tools + Ollama)
 ↓
TaskPacket
 ↓
Codex implementation [1/2]
 ↓
dotnet build/test
 ↓
Diagnostic (Ollama)
 ↓
optional Codex correction [2/2]
 ↓
dotnet build/test
 ↓
Review (Ollama)
 ↓
Result
```

В MVP orchestration остаётся полностью детерминированным: LLM может возвращать решение или рекомендацию, но только TaskForge решает, какой следующий этап workflow разрешён и достаточно ли для него бюджета.
