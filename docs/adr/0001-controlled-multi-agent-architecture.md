# ADR-0001: Контролируемая оркестрация AI-workers

- **Статус:** Accepted
- **Дата:** 2026-10-01
- **Пересмотр:** 2026-10-02
- **Проект:** TaskForge

## Контекст

TaskForge должен выполнять задачи разработки с помощью локальных моделей и Codex, не превращая один пользовательский запрос в неконтролируемое дерево дорогих model runs.

Основной риск встроенной мультиагентности coding-agent состоит не только в цене одного вызова. Координатор может создавать дополнительные workers, повторно исследовать репозиторий, дублировать контекст и тем самым делать расход квоты и время выполнения плохо предсказуемыми.

При этом значительная часть workflow не требует coding-agent: поиск файлов, чтение git metadata, запуск build/test, первичная классификация, диагностика логов и review могут выполняться deterministic tools или локальной LLM.

## Решение

### 1. TaskForge владеет orchestration

Только TaskForge определяет:

- текущий state;
- следующий шаг workflow;
- разрешён ли очередной Codex run;
- какой workspace используется;
- какие build/test команды считаются authoritative;
- когда задача должна остановиться как `Completed`, `BudgetExceeded`, `NeedsUser`, `Failed` или `Cancelled`.

LLM может вернуть structured recommendation, но не меняет state самостоятельно и не может увеличить собственный бюджет.

### 2. Codex является leaf coding worker

Codex не используется как coordinator.

Запрет subagents обеспечивается в два слоя:

1. **hard enforcement на запуске** — multi-agent capability отключается launcher-конфигурацией/CLI override; worker запускается в ограниченном sandbox, с фиксированной approval policy, внешним timeout и process-tree kill;
2. **prompt contract** — worker дополнительно получает инструкцию не делегировать задачу и не запускать subagents.

Prompt не считается security или budget boundary.

Coding worker имеет отдельный side-effectful контракт. Он не является реализацией обычного request/response LLM provider.

Целевая граница:

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

Результат worker должен позволять оркестратору получить как минимум diff/changed files, отчёт и usage.

Локальные Planner/Explorer/Diagnostic/Reviewer используют chat abstraction. Для .NET предпочтительным общим контрактом является `Microsoft.Extensions.AI.IChatClient` либо тонкий adapter над ним.

### 3. Tier выбирается по типу операции, а не по self-confidence модели

Канонические уровни:

~~~text
Tier 0: deterministic tools
Tier 1: local LLM reasoning without source-code side effects
Tier 2: coding worker with repository side effects
~~~

Для MVP правила routing детерминированы:

- известная операция поиска/build/test/git -> Tier 0;
- planning, context selection, diagnosis, review -> Tier 1;
- изменение исходного кода -> Tier 2;
- второй Tier-2 run допустим только как correction после результата authoritative build/test и Diagnostic step.

`Confidence` локальной модели допускается хранить как telemetry, но он **не используется как routing gate**, пока не появятся evals и калибровка.

Local code implementation в MVP отсутствует. Если он будет добавлен, необходимо отдельно выбрать механизм side effects: bounded agent loop с tools либо генерация patch с deterministic apply/validation. Ollama сама по себе не считается coding worker.

Переход от дорогого шага к локальной диагностике не является «downgrade той же операции»: это новый шаг workflow с другим контрактом.

### 4. Бюджет имеет одну семантику

Используется один счётчик:

~~~text
MaxCodexRuns = 2
~~~

Каждый фактически запущенный Codex process считается run, включая run, завершившийся ошибкой или timeout. Отдельных `MaxRetries` / `MaxCodexRetries` в архитектурной модели нет.

Разрешённая последовательность:

~~~text
Implementation [1/2]
  -> authoritative build/test
  -> Diagnostic
  -> optional Correction [2/2]
  -> authoritative build/test
~~~

Третий run запрещён.

Дополнительно каждый run обязан иметь:

- `CodexRunTimeout`;
- per-run token/rollout budget, если backend предоставляет enforcement;
- глобальный concurrency gate, по умолчанию `MaxConcurrentCodexRuns = 1`;
- CancellationToken с убийством всего дерева процессов при отмене/timeout.

Usage из machine-readable Codex output собирается уже в MVP. Usage telemetry не заменяет hard run/token limits.

### 5. Workspace isolation является обязательной частью решения

Codex не должен изменять основной checkout.

Каждый apply выполняется в отдельном git worktree, привязанном к сохранённому `baseCommit`. Sandbox и правила запуска процессов определены в [ADR-0002](0002-workspace-isolation-and-sandbox.md).

До полной реализации ADR-0002 TaskForge допускается использовать только на доверенных локальных репозиториях.

### 6. Build/test принадлежат оркестратору

Codex может запускать targeted checks только если это разрешено worker policy. Их результат является advisory.

Source of truth — команды, запущенные TaskForge после worker завершения. Именно эти результаты определяют переход в Review, Diagnostic или terminal state.

### 7. TaskPacket является versioned bounded contract

Перед запуском worker TaskForge сохраняет validated TaskPacket.

Минимальный контракт содержит:

- `schemaVersion`;
- `taskId`;
- `baseCommit`;
- goal, constraints и acceptance criteria;
- relevant paths **и выбранные file spans/excerpts**;
- `writeScope`;
- command/test policy;
- remaining run budget и per-run token budget;
- packet token budget;
- diagnostics для correction run.

Structured output локальных моделей валидируется по JSON Schema.

Риск неполного context discovery считается отдельным архитектурным риском: Explorer может пропустить критичный файл. Поэтому worker имеет bounded право дочитать дополнительные файлы в worktree, а факт такого расширения контекста должен попадать в telemetry.

### 8. Воспроизводимость означает replayable inputs, а не детерминированный LLM output

Для каждого run сохраняются:

- исходная задача;
- `baseCommit`;
- TaskPacket;
- версии prompt templates;
- provider/model и параметры inference;
- seed, если backend его поддерживает;
- версия Codex CLI и эффективные launch options;
- build/test commands;
- worker trace и usage;
- diff/changed files;
- terminal state.

Это позволяет повторить тот же workflow input и расследовать расхождения, но не обещает byte-identical результат генеративной модели.

## Канонический workflow

Подробная state machine описана только в [docs/design/orchestrator.md](../design/orchestrator.md).

На уровне ADR достаточно следующего инварианта:

~~~text
Plan/Explore
    |
    v
TaskPacket
    |
    v
Codex Implementation [1/2]
    |
    v
orchestrator build/test
    | success                 | failure
    v                         v
 Review                  Diagnostic (local)
    |                         |
    v                         +--> optional Codex Correction [2/2]
 terminal                           |
                                    v
                              orchestrator build/test
                                    |
                                    v
                                  terminal
~~~

Неограниченных LLM loops нет.

## Метрики, по которым решение пересматривается

TaskForge должен собирать как минимум:

- Codex runs per task;
- долю задач, прошедших без correction run;
- input/output/weighted tokens per task;
- долю `BudgetExceeded`;
- долю `NeedsUser`;
- Codex timeout/failure rate;
- first-pass build/test success rate;
- частоту, с которой worker вынужден читать файлы вне подготовленного context set;
- долю correction, классифицированных как недостаточный/ошибочный TaskPacket.

Без этих данных нельзя обоснованно менять routing или бюджет.

## Рассмотренные альтернативы

### Codex как автономный multi-agent coordinator

Отклонено как основной режим: TaskForge теряет внешний контроль над числом дорогих workers и общим workflow budget.

### Один Codex на всю задачу

Отклонено: дорогой worker тратит контекст на deterministic discovery и verification, а workflow хуже наблюдаем.

### Только локальная LLM

Отклонено как общий coding backend. Local LLM остаётся Tier 1; local coding worker может появиться позже только с явным side-effect contract и evals.

### Microsoft Agent Framework

Это наиболее близкая готовая .NET-альтернатива собственному workflow runtime: framework поддерживает explicit workflows, typed executors, state/checkpointing и observability. Для текущего MVP TaskForge сохраняет небольшой собственный deterministic orchestrator, потому что ключевые budget/workspace invariants всё равно являются доменной логикой проекта.

Решение должно быть пересмотрено, если state machine станет существенно сложнее, появятся durable distributed workflows или human-in-the-loop checkpoints.

### Semantic Kernel

Может использоваться как provider/tooling layer, но для новой orchestration-архитектуры Microsoft Agent Framework рассматривается раньше: Microsoft позиционирует его как следующий шаг развития agent/workflow abstractions.

### LangGraph

Подходит для graph/state orchestration, но добавляет отдельный Python/TypeScript runtime в .NET-first проект. Не выбран для MVP.

### OpenHands / Aider

Рассмотрены как готовые coding harnesses и источник идей для repository context/editing. Они не выбраны orchestration core, поскольку TaskForge требуется собственный жёсткий бюджет и собственная state machine вокруг Codex.

## Последствия

Положительные:

- дорогие runs ограничены вне модели;
- recursive multi-agent spending запрещён технически, а не только prompt-инструкцией;
- build/test остаются deterministic control plane;
- можно отдельно измерять качество context preparation и coding worker;
- Codex можно заменить другим coding worker без изменения orchestration semantics.

Отрицательные:

- требуется поддерживать state machine, budget gate, workspace manager и run artifacts;
- sandbox/build isolation сложнее обычного запуска CLI;
- context preparation становится критической частью качества;
- часть возможностей готовых agent frameworks пока реализуется самостоятельно.

## Связанные документы

- [ADR-0002: Workspace isolation и sandbox](0002-workspace-isolation-and-sandbox.md)
- [Orchestrator design](../design/orchestrator.md)
- [RFC-0001](../rfc/0001-mvp-implementation.md) — historical/superseded implementation proposal.

## References

- Microsoft.Extensions.AI: https://learn.microsoft.com/dotnet/core/extensions/artificial-intelligence
- Microsoft Agent Framework: https://learn.microsoft.com/agent-framework/
- Agent Framework workflows: https://learn.microsoft.com/agent-framework/concepts/workflows/
- Aider repository map: https://aider.chat/docs/repomap.html
