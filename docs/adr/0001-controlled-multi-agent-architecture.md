# ADR-0001: Контролируемая мультиагентная архитектура TaskForge

- **Статус:** Accepted
- **Дата:** 2026-10-01
- **Проект:** TaskForge
- **Область:** AI-assisted development / agent orchestration
- **Основной стек:** .NET 10, Ollama, Codex, Git, dotnet CLI

## 1. Контекст

TaskForge должен выполнять задачи разработки с помощью нескольких специализированных агентов, при этом расход дорогих модельных вызовов должен оставаться предсказуемым и ограниченным.

Использование Codex как автономного координатора, способного порождать дочерних агентов, приводит к нескольким проблемам:

- каждый дочерний агент получает отдельный контекст и выполняет собственные модельные вызовы;
- разные агенты повторно исследуют одни и те же части репозитория;
- значительная часть дорогого контекста тратится на поиск файлов, чтение проекта, анализ логов и тестов;
- количество вызовов Codex сложнее ограничить снаружи;
- рекурсивная или широкая параллельная мультиагентность может быстро исчерпать доступную квоту.

Большинство подготовительных операций не требуют использования наиболее дорогой модели.

## 2. Проблема

Необходимо построить мультиагентную систему разработки, которая:

1. поддерживает несколько специализированных ролей;
2. не позволяет дорогим агентам неконтролируемо создавать других дорогих агентов;
3. минимизирует количество вызовов Codex;
4. использует локальную LLM для дешёвых операций;
5. передаёт Codex минимальный достаточный контекст;
6. позволяет задавать жёсткие лимиты на число вызовов и повторов;
7. сохраняет Codex для сложной реализации и исправлений;
8. может работать как локальный сервис;
9. не зависит от конкретного UI или клиента Codex.

## 3. Решение

Мультиагентность реализуется на уровне собственного оркестратора TaskForge.

Codex не является главным координатором системы. Он используется как дорогой специализированный worker для реализации или сложного исправления.

Целевая схема:

```text
                         User
                           │
                           ▼
                    TaskForge
                  Agent Orchestrator
                           │
           ┌───────────────┼────────────────┐
           │               │                │
           ▼               ▼                ▼
        Planner         Explorer        Tool Runner
        Ollama          Ollama          deterministic
           │               │                │
           └───────────────┼────────────────┘
                           │
                           ▼
                      TaskPacket
                           │
                           ▼
                     Codex Worker
                           │
                           ▼
                        git diff
                           │
                           ▼
                     build / tests
                           │
                           ▼
                  Local diagnostics
                           │
               ┌───────────┴───────────┐
               │                       │
             success                  failure
               │                       │
               ▼                       ▼
            review              Codex Fix
                                if required
```

Ключевой принцип:

> Codex используется для сложной части задачи, а не для управления всем процессом разработки.

## 4. Принципы архитектуры

### 4.1. Запрет рекурсивной мультиагентности Codex

Codex-worker не должен запускать собственных дочерних агентов.

Каждый его вызов получает инструкцию, аналогичную:

```text
Do not spawn subagents.

You are an implementation worker.

Repository exploration and task decomposition have already been performed
by the orchestrator.

Work primarily with the supplied task packet.

Inspect additional files only when required for implementation.

Do not perform broad repository exploration.

After implementation:
1. run relevant tests if allowed;
2. report changed files;
3. report unresolved issues;
4. stop.
```

Создание и координация агентов выполняются только TaskForge.

### 4.2. Один уровень оркестрации

Не допускается дерево:

```text
Orchestrator
└── Agent
    └── Agent
        └── Agent
```

Используется плоская модель:

```text
Orchestrator
├── Planner
├── Explorer
├── Implementer
├── Diagnostic
├── Tester
└── Reviewer
```

Каждый агент является конечным исполнителем.

### 4.3. Эскалация от дешёвого к дорогому

Приоритет выполнения:

```text
Deterministic tools
        ↓
Local LLM
        ↓
Codex
```

Codex вызывается только при необходимости.

## 5. Уровни выполнения

### Tier 0 — deterministic tools

Без LLM выполняются:

- `rg`;
- `git grep`;
- `git diff`;
- `git log`;
- `dotnet build`;
- `dotnet test`;
- Roslyn/AST-анализ;
- чтение project/solution-файлов;
- поиск зависимостей и файлов.

Стоимость LLM для этого уровня равна нулю.

### Tier 1 — Local LLM

Через Ollama выполняются:

- классификация задачи;
- декомпозиция;
- определение релевантных файлов;
- анализ stack trace;
- анализ результатов тестов;
- первичная диагностика;
- простое ревью;
- формирование TaskPacket;
- оценка необходимости эскалации.

### Tier 2 — Codex

Codex используется для:

- нетривиальной реализации;
- сложного рефакторинга;
- изменения нескольких взаимосвязанных модулей;
- сложной concurrency-логики;
- сложных запросов и инфраструктурных изменений;
- исправления после неудачной локальной попытки.

## 6. Роли агентов

### Planner

Определяет:

- цель;
- ограничения;
- критерии готовности;
- этапы;
- необходимость Codex.

По умолчанию работает через Ollama.

### Explorer

Исследует кодовую базу и определяет:

- релевантные проекты;
- классы;
- методы;
- тесты;
- конфигурацию;
- зависимости;
- похожие реализации.

Использует deterministic tools и Ollama.

### Implementer

Выполняет изменение кода.

Для сложных задач используется Codex. Для простых изменений допускается локальная модель.

### Tester

Запускает сборку и тесты обычными процессами:

```text
dotnet build
dotnet test
```

LLM для самого запуска тестов не используется.

### Diagnostic Agent

Анализирует ошибки сборки и тестов.

Сначала используется Ollama. Codex вызывается только при необходимости.

### Reviewer

Проверяет итоговый diff:

- соответствие задаче;
- потенциальные регрессии;
- очевидные ошибки;
- избыточные изменения;
- отсутствие тестов;
- нарушения архитектуры.

По умолчанию используется Ollama; Codex-review должен быть отдельным явно разрешённым режимом.

## 7. TaskPacket

Codex не должен самостоятельно исследовать весь репозиторий без необходимости.

Оркестратор формирует компактный TaskPacket.

Пример:

```json
{
  "goal": "Fix PropertyBinding cleanup after Segment deletion",
  "constraints": [
    "Do not change public API",
    "Preserve existing database schema if possible"
  ],
  "relevantFiles": [
    "src/Operations/Services/PropertyBindingService.cs",
    "src/Operations/Data/PropertyBindingConfiguration.cs",
    "tests/Operations.Tests/PropertyBindingTests.cs"
  ],
  "diagnostics": {
    "test": "PropertyBindingTests.DeleteSegment",
    "error": "FK violation"
  },
  "observations": [
    "DeleteSegmentAsync deletes Segment",
    "PropertyBinding FK uses Restrict"
  ],
  "acceptanceCriteria": [
    "Segment can be deleted",
    "Related bindings are cleaned up",
    "Existing tests pass"
  ]
}
```

Главный принцип работы с контекстом:

> Context should be discovered cheaply and consumed expensively.

Поиск и подготовка контекста выполняются дешёвыми средствами, а Codex получает уже отобранный набор данных.

## 8. Контроль бюджета

Оркестратор обязан контролировать использование Codex.

Начальные значения:

```text
MaxConcurrentCodexRuns = 1
MaxCodexRunsPerTask = 2
MaxCodexRetries = 1
```

Позже могут быть добавлены:

```text
MaxInputTokens
MaxOutputTokens
MaxTaskCost
MaxTaskDuration
```

Пример конфигурационной модели:

```csharp
public sealed record AgentBudget
{
    public int MaxCodexRuns { get; init; } = 2;
    public int MaxRetries { get; init; } = 1;
    public int MaxConcurrentCodexRuns { get; init; } = 1;
}
```

При достижении лимита задача должна остановиться и вернуть текущий результат пользователю.

## 9. Эскалация

Результат агента должен явно сообщать, может ли workflow продолжаться на текущем уровне:

```csharp
public enum AgentResultStatus
{
    Completed,
    NeedsMoreContext,
    NeedsHigherTier,
    Failed
}
```

Пример результата:

```csharp
public sealed record AgentResult(
    AgentResultStatus Status,
    string Summary,
    IReadOnlyList<string> RelevantFiles,
    double Confidence);
```

Логика:

```text
Tool
 ↓
Ollama
 ↓
Codex
```

Обратная эскалация и неограниченные retry-loop не используются.

## 10. Предлагаемая структура .NET

```text
TaskForge
│
├── Application
│   ├── Tasks
│   ├── Agents
│   ├── Workflows
│   └── Budget
│
├── Agents
│   ├── PlannerAgent
│   ├── ExplorerAgent
│   ├── ImplementerAgent
│   ├── DiagnosticAgent
│   └── ReviewAgent
│
├── Providers
│   ├── OllamaProvider
│   └── CodexProvider
│
├── Tools
│   ├── GitTool
│   ├── SearchTool
│   ├── DotnetTool
│   ├── FileTool
│   └── TestTool
│
└── Infrastructure
    ├── Workspace
    ├── ProcessRunner
    └── TaskQueue
```

Базовые интерфейсы:

```csharp
public interface IAgent
{
    Task<AgentResult> ExecuteAsync(
        AgentContext context,
        CancellationToken cancellationToken);
}
```

```csharp
public interface ILlmProvider
{
    Task<LlmResponse> ExecuteAsync(
        LlmRequest request,
        CancellationToken cancellationToken);
}
```

Провайдеры:

```text
OllamaProvider
CodexProvider
```

Роль агента не должна напрямую зависеть от конкретной модели.

## 11. Workspace isolation

Каждая задача должна в перспективе выполняться в отдельном Git workspace.

Предпочтительный механизм:

```text
git worktree
```

Например:

```text
repo/
worktrees/
    task-001/
    task-002/
```

Это позволяет:

- параллельно выполнять независимые задачи;
- не повреждать основной checkout;
- безопасно отменять неудачные изменения;
- ограничивать область файлов, доступную конкретному worker.

## 12. Workflow задачи

Стандартный workflow:

```text
Task received
     │
     ▼
Planner
     │
     ▼
Repository search
     │
     ▼
Explorer
     │
     ▼
TaskPacket
     │
     ▼
Decide execution tier
     │
     ├── local implementation
     │
     └── Codex implementation
              │
              ▼
          git diff
              │
              ▼
         dotnet build
              │
              ▼
          dotnet test
              │
       ┌──────┴──────┐
       │             │
     success       failure
       │             │
       │        Local diagnosis
       │             │
       │        ┌────┴────┐
       │        │         │
       │    local fix   Codex fix
       │
       ▼
     review
       │
       ▼
     DONE
```

## 13. Retry policy

После неудачного исполнения:

```text
Codex
 ↓
tests failed
 ↓
local diagnostic
```

Только Diagnostic Agent может инициировать второй Codex-run.

Максимальный стандартный сценарий:

```text
Codex implement
      ↓
tests
      ↓
local diagnosis
      ↓
Codex fix
```

После этого автоматические Codex-вызовы прекращаются.

## 14. Параллельность

Локальные операции могут выполняться параллельно.

Например:

```text
           ┌── Explorer A
Planner ───┼── Explorer B
           └── Dependency Analyzer
```

Codex по умолчанию выполняется последовательно:

```text
MaxConcurrentCodexRuns = 1
```

Параллельность используется преимущественно там, где она не расходует дорогую квоту.

## 15. Наблюдаемость

Для каждого запуска необходимо сохранять:

```text
TaskId
Agent
Provider
Model
StartTime
Duration
InputTokens
OutputTokens
CodexRunNumber
ToolCalls
Result
```

Это позволит определить реальную стоимость отдельных стадий и постепенно улучшать routing.

## 16. Состояние workflow

Состояние задачи не должно существовать только внутри LLM-контекста.

TaskForge хранит:

```text
Task
Plan
RelevantFiles
TaskPacket
AgentResults
Diff
TestResults
BudgetUsage
```

LLM рассматривается как stateless worker.

## 17. Рассмотренные альтернативы

### Встроенная мультиагентность Codex

Плюсы:

- минимум собственной инфраструктуры;
- модель сама распределяет работу.

Минусы:

- труднее контролировать число дорогих вызовов;
- повторное исследование контекста;
- сложнее обеспечить общий бюджет задачи;
- слабее наблюдаемость.

**Решение:** не использовать как основную архитектуру.

### Один Codex без оркестратора

Плюсы:

- простота.

Минусы:

- дорогая модель выполняет дешёвую работу;
- большой входной контекст;
- ограниченная автоматизация.

**Решение:** не использовать.

### Только локальные модели

Плюсы:

- низкая стоимость;
- полный контроль.

Минусы:

- недостаточная надёжность для части сложных задач.

**Решение:** использовать как Tier 1, но не как единственный backend.

## 18. Последствия

### Положительные

- предсказуемый расход Codex;
- отсутствие рекурсивного создания дорогих агентов;
- большая часть работы выполняется локально;
- меньший контекст Codex;
- возможность параллельных локальных исследований;
- независимость ролей от конкретного LLM provider;
- наблюдаемость;
- возможность жёстких лимитов;
- воспроизводимый workflow.

### Отрицательные

- требуется собственный orchestrator;
- необходимо поддерживать prompts и routing;
- требуется хранение состояния workflow;
- появляется логика эскалации;
- потребуется поддерживать интеграции с несколькими LLM backend.

## 19. Ограничения первой версии

В MVP не поддерживаются:

- рекурсивные агенты;
- динамическое создание типов агентов;
- более одного одновременного Codex worker;
- автоматический merge в main;
- самостоятельный push;
- автоматическое создание PR;
- неограниченные retry-loop.

## 20. MVP

Первый workflow:

```text
Task
 ↓
Planner
 ↓
Explorer
 ↓
TaskPacket
 ↓
Codex Implementer
 ↓
dotnet test
 ↓
Local Review
```

Минимальные компоненты:

```text
AgentOrchestrator
OllamaProvider
CodexProvider
GitTool
SearchTool
DotnetTool
PlannerAgent
ExplorerAgent
ImplementerAgent
ReviewAgent
```

Начальная конфигурация:

```json
{
  "Agents": {
    "MaxConcurrentCodexRuns": 1,
    "MaxCodexRunsPerTask": 2,
    "MaxRetries": 1
  }
}
```

## 21. Следующие этапы

После MVP могут быть добавлены:

- git worktree isolation;
- persistent task state;
- task queue;
- parallel explorers;
- web UI;
- GitHub integration;
- PR creation/review;
- cost accounting;
- automatic model routing.

## 22. Итог

TaskForge использует собственный .NET orchestrator.

Мультиагентность реализуется внутри TaskForge, а не через рекурсивных Codex agents.

Основная модель исполнения:

```text
Tools → Ollama → Codex
```

Для одной задачи по умолчанию допускается:

```text
1 основной Codex run
+
1 corrective Codex run
```

Поиск, анализ, подготовка контекста, тестирование и первичная диагностика по возможности выполняются локально.
