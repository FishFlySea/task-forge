# ADR-0003: Роли и профили для Codex CLI и Claude Code CLI

- **Статус:** Proposed
- **Дата:** 2026-10-14
- **Проект:** TaskForge
- **Расширяет:** [ADR-0001](0001-controlled-multi-agent-architecture.md)
- **Сохраняет ограничения безопасности:** [ADR-0002](0002-workspace-isolation-and-sandbox.md)
- **Вариант языка и расширяемости:** [ADR-0004](0004-python-core-and-controlled-self-extension.md) — Proposed

## Контекст

Первый MVP TaskForge жёстко связывает локальные Planner/Explorer/Diagnostic/Reviewer с Ollama, а разработчика — с Codex CLI. Это ограничивает выбор моделей, не позволяет назначить архитектором Claude Code, разработчиком Codex, а ревьюером другую модель или поменять эти назначения без изменения кода.

Новая цель: TaskForge управляет **ролями**, а исполнителем каждой роли может быть **установленное и уже авторизованное консольное приложение** Codex или Claude Code. Аналогичный принцип используется в Hermes: собственная оркестрация, настраиваемые профили исполнителей. Прямой OpenAI/Anthropic API и хранение API-ключей TaskForge для этих двух backend не требуются.

## Решение

### 1. Роль, профиль и CLI-backend — разные сущности

- **Workflow stage** — канонический этап (`planning`, `exploration`, `implementation`, `diagnosis`, `correction`, `review`). Переходами управляет только TaskForge.
- **Role** — назначение для этапа: `architect`, `explorer`, `developer`, `diagnostician`, `reviewer`. Можно определять дополнительные именованные роли и назначать их поддерживаемым этапам.
- **Agent profile** — конфигурация конкретного исполнителя: `backend`, `model`, optional `effort`, шаблон/версия инструкций, capabilities, policy, ограничения ресурсов.
- **CLI backend** — адаптер `codex-cli` или `claude-code-cli`, который запускает локальный executable как дочерний процесс и приводит события/результат к единому формату.
- **Binding** — явное соответствие stage/role -> именованный profile. Один backend может использоваться несколькими ролями с разными моделями, инструкциями и бюджетами.

Нельзя смешивать роль (`reviewer`) с провайдером (`claude-code-cli`) и моделью (`opus`). Routing выполняется по сохранённому binding, а не по self-confidence модели и не по просьбам агента.

### 2. Примеры допустимых назначений

Ниже **иллюстрация будущей конфигурации**, не утверждение о поддержке этих полей текущим кодом. Названия моделей здесь — настраиваемые строки; доступность модели проверяет установленный CLI / учётная запись.

~~~yaml
schemaVersion: 1

profiles:
  architect-claude:
    backend: claude-code-cli
    executable: claude
    model: opus
    effort: high
    instructions: prompts/architect.md
    capabilities: [read, propose]
    maxTurns: 8
    timeoutSeconds: 600

  explorer-codex:
    backend: codex-cli
    executable: codex
    model: "<supported-codex-model-id>"
    effort: low
    instructions: prompts/explorer.md
    capabilities: [read, propose]
    timeoutSeconds: 300

  developer-codex:
    backend: codex-cli
    executable: codex
    model: "<supported-codex-model-id>"
    effort: medium
    instructions: prompts/developer.md
    capabilities: [read, edit]
    timeoutSeconds: 1200

  reviewer-claude:
    backend: claude-code-cli
    executable: claude
    model: sonnet
    effort: high
    instructions: prompts/reviewer.md
    capabilities: [read, propose]
    maxTurns: 6
    timeoutSeconds: 600

roleBindings:
  planning: architect-claude
  exploration: explorer-codex
  implementation: developer-codex
  diagnosis: architect-claude
  correction: developer-codex
  review: reviewer-claude

limits:
  maxCodingRunsPerTask: 2
  maxConcurrentCodingRuns: 1
  maxAgentInvocationsPerTask: 8
  maxTotalTaskDurationSeconds: 3600
~~~

Разрешены любые поддерживаемые перестановки: например, developer=Claude Code, reviewer=Codex, architect=Codex. Назначение модели **отдельно для каждой роли/профиля** обязательно; глобальная модель — только явный fallback конфигурации, не неявное наследование CLI defaults. `effort` опционален и проверяется backend-специфично.

### 3. Execution contract

Контракты не должны выдавать coding side effects за обычный LLM chat.

~~~csharp
public interface IAgentCliBackend
{
    string BackendId { get; }
    Task<CliCapabilities> ProbeAsync(CancellationToken ct);
    Task<AgentInvocationResult> InvokeAsync(
        AgentInvocation invocation,
        ExecutionBoundary boundary,
        CancellationToken ct);
}
~~~

`AgentInvocation` содержит `runId`, stage/role/profile, явно выбранную модель, роль-инструкции, TaskPacket или ограниченный контекст, structured-output schema, policy и budget. `AgentInvocationResult` содержит нормализованные outcome, structured result / report, exit code, trace, usage, cost-if-reported, actual model-if-reported, session id-if-reported и failure category.

`ExecutionBoundary` задаёт не просто `cwd`, а capabilities (read-only либо edit), допустимый workspace, настройки изоляции, environment policy и таймаут.

Для read-only ролей агенту передаётся подготовленный TaskPacket/diff/logs; shell, edit и дополнительные агенты недоступны согласно **проверенной** политике запуска. Только implementation/correction могут получить write capability и только в disposable worktree. Контроль `writeScope` и authoritative build/test остаётся в TaskForge.

Этот интерфейс — граница CLI-рантайма, а не публичный generic `ILlmProvider`. Отдельный `IChatClient` может остаться для прямой Ollama как optional Tier-1 backend; его наличие не является условием для ролей на Codex/Claude.

### 4. Запуск установленных приложений

TaskForge использует `codex` и `claude` из PATH либо явные пути из конфигурации, без встроенных SDK и без собственной авторизации от имени пользователя. Первоначальные установка и login — ответственность оператора; `taskforge doctor` должен проверять binary, version, auth и необходимые capabilities **до** расходного запуска. Credentials не включаются в TaskPacket, prompts и logs.

- **Codex adapter:** `codex exec`, явные `--model`, `--json`, workspace/sandbox/approval/config overrides; structured JSON output при поддержке CLI. Для read-only запуска — `read-only` sandbox, для coding — `workspace-write`; не полагаться на user config по умолчанию.
- **Claude Code adapter:** `claude -p`, явные `--model`, `--output-format stream-json`, `--max-turns`, режим разрешений и ограничения `--tools` / MCP; structured JSON output при поддержке CLI. Для read-only стадии предпочтителен `--restricted` в версии, где он поддержан. `--permission-prompts none` помогает избежать зависания при unattended режиме (требуется проверка версии).
- Prompt передаётся через stdin или временный защищённый файл, если CLI это поддерживает; аргументы формируются типизированным builder без shell string interpolation. Отдельный процесс получает scoped environment, managed cwd, stdout/stderr streams, отмену и process-tree kill.
- TaskForge управляет собственным worktree; встроенные `--worktree` CLI в этом workflow не включаются, чтобы избежать двойного управления lifecycle.
- CLI session/resume является только оптимизацией внутри допустимого run; нельзя через resume обойти лимит вызовов, изменить профиль, расширить scope или продолжить устаревший TaskPacket.

Конкретные флаги и форматы событий проверяются versioned compatibility tests, не фиксируются как бессрочные архитектурные гарантии.

### 5. Права и безопасность

**Role policy — hard boundary, prompt — только инструкция поведения.**

- `architect`, `explorer`, `diagnostician`, `reviewer`: read-only, без разрешения менять исходники и запускать произвольные команды.
- `developer`: write только в TaskForge-owned disposable worktree; post-run scope validation до build/test; при нарушении — `NeedsUser`, без автоматического retry.
- Никакая роль не может менять role bindings, budgets, model routing, доступные tools или создавать неучтённых дочерних исполнителей.
- Встроенные subagents/agent teams обоих CLI по умолчанию запрещаются **проверяемыми capability/launch ограничениям**. Если конкретная версия/backend не позволяет гарантировать отсутствие неучтённого делегирования, профиль не допускается к unattended запуску под строгими лимитами; одного prompt «не запускай агентов» недостаточно.
- Для Codex/Claude пользовательские MCP, hooks, plugins, skills, local/project settings рассматриваются как дополнительная поверхность полномочий и расходов. Их загрузку нужно изолировать/ограничить или fail closed; `read-only`/worktree сами по себе не являются host sandbox.
- Сетевой доступ **самого CLI для auth/model calls** отличается от сетевого доступа выполняемого агентом shell/tools. Первый необходим, второй запрещён по умолчанию, кроме явных разрешений.
- Изолировать secrets и host env, а build/test исполнять в сопоставимой process/container boundary по ADR-0002. До полноценной реализации — только trusted repositories.
- `--dangerously-skip-permissions`, аналогичные bypass flags, и неограниченный host shell не применяются как «быстрый» способ unattended запуска.

### 6. Бюджеты и телеметрия

Прежний `MaxCodexRuns=2` обобщается до **`MaxCodingRunsPerTask=2`** для implementation + optional correction независимо от выбранного CLI: смена Codex на Claude не обнуляет счётчик. Каждый фактически созданный coding process списывает run slot. Для других stages задаются отдельные лимиты на число invocations/turns, длительность и (где поддерживается) provider budget; внутри одного CLI могут существовать multiple model turns.

- Timeout и process-tree cancellation обязательны для каждого backend.
- Vendor token/rollout controls, `--max-turns`, `--max-budget-usd` и реальные quota — **разные** ограничения. Не считать `--max-budget-usd` гарантированным финансовым hard cap и не обещать одинаковую семантику у Codex и Claude.
- Usage сохраняется отдельно по role/stage/profile/backend/model; raw vendor events сохраняются с redaction. `reportedTokens`, `reportedCost` и `quotaRemaining` могут быть `unknown`, не подменяются оценкой без явного признака estimate.
- У TaskForge есть общий worst-case лимит запусков и времени; ограничения конкретного CLI применяются только после capability probing.
- Provider/model fallback и retry допускаются **только** по заранее записанной policy с общим бюджетом. Никакого бесшумного перехода на более дорогую модель или другую авторизацию.
- В TaskPacket/run artifacts сохраняется snapshot roleBindings, профилей, версии CLI, фактических моделей, effective policies и hashes инструкций, чтобы `apply` не использовал незаметно изменившиеся настройки после `plan`.

### 7. Backward compatibility и границы поставки

Это **новое архитектурное решение, ещё не реализованное в MVP**. Старый маршрут Ollama Planning/Exploration/Diagnostic/Review + Codex Implementation/Correction остаётся default/compatibility mode до миграции. Старые TaskPacket v3 разрешено читать/инспектировать; `apply` по новому binding допускается только после явной миграции либо нового `plan`, а не молчаливого reinterpretation.

Нужны: versioned конфигурация профилей и bindings; раздельные Codex/Claude adapters; read-only execution policy; typed output normalization; generalized budgets; per-role usage; capability/auth doctor; интеграционные тесты с подменными CLI и smoke E2E с обеими установленными утилитами.

**Не входит в эту ADR:** самостоятельная многоуровневая делегация внутри CLI, UI для управления ролями, автоматическое решение какой provider дешевле, прямые OpenAI/Anthropic API, автоматический merge и произвольные агентские workflow-графы.

## Acceptance criteria для реализации

1. Из конфигурации можно назначить architect=Claude Code/Opus, developer=Codex/выбранная модель, reviewer=Claude Code/Sonnet; и поменять их местами без recompilation.
2. Для двух ролей одного CLI можно назначить разные модели, инструкции и лимиты; effective binding виден в `inspect`.
3. `doctor` диагностирует отсутствие executable, устаревшие flags, отсутствие login и недоступную модель до рабочего запуска, насколько позволяют CLI; недоступность модели не приводит к бесшумному fallback.
4. Планирование/ревью не изменяют репозиторий; код изменяется только в disposable worktree. Отдельные negative tests проверяют отказ при нарушении capability, scope и budget.
5. E2E mock CLI подтверждают нормализацию JSON/JSONL, failures, cancellation, timeout, process cleanup, billing unknown и role routing; live smoke checks проходят для `codex exec` и `claude -p`.
6. Коррекция через другой backend всё равно учитывается в одном `MaxCodingRunsPerTask`, и retry не обходят лимит.
7. Все файлы старого MVP продолжают корректно читаться без ложного утверждения, что новый режим уже доступен.

## Ссылки на CLI

- Codex CLI: https://developers.openai.com/codex/cli/reference
- Claude Code CLI: https://code.claude.com/docs/en/cli-reference
