# ADR-0004: Python-first ядро и контролируемое саморазвитие TaskForge

- **Статус:** Accepted as target direction (2026-10-14); migration gated by spike and parity
- **Дата:** 2026-10-14
- **Проект:** TaskForge
- **Связано:** [ADR-0001](0001-controlled-multi-agent-architecture.md), [ADR-0002](0002-workspace-isolation-and-sandbox.md), [ADR-0003](0003-cli-role-profiles-and-multi-provider-workers.md), [ADR-0005](0005-conversation-reflection-memory-and-skill-evolution.md)

## Контекст

Текущий MVP реализован на .NET 10. Новые требования: независимо назначаемые роли на локально установленные Codex CLI и Claude Code CLI, подключаемые инструменты/навыки, возможность агентам **создавать расширения самого TaskForge**. Язык реализации не должен ограничивать расширения или потреблять отдельный платный LLM API: оркестратор запускает авторизованные CLI.

Python упрощает быстрое создание инструментов и интеграций, описание contracts с typing/Protocol, загрузку trusted plugins через packaging entry points, поддержку собственных скриптов и автоматическое тестирование расширений. Но Python **не является предпосылкой** саморазвития: .NET тоже допускает плагины, внешние процессы и изменения исходников через Git. Переписывание работающего MVP — стоимость и риск; поэтому новое ядро сначала подтверждается ограниченным spike.

## Решение, предлагаемое к принятию

### 1. Python-first orchestration core

Предпочтительный целевой язык **Python**, если acceptance gates ниже показывают, что это даёт нужную гибкость без регрессии контроля.

Core содержит стабильную deterministic control plane: state machine, policy/budget gates, persistence, artifacts, workspace manager, process supervision, role/profile binding, tool/plugin registry. CLI adapters для `codex exec` и `claude -p` вызываются через `asyncio.create_subprocess_exec` с явным argv (без `shell=True`), изолированными environment и cwd, streaming JSONL, timeout и termination process tree.

**Язык проекта-цели независим от языка TaskForge.** Python orchestrator продолжает запускать `dotnet restore/build/test` из typed command policy для .NET проектов.

### 2. Три уровня расширяемости

1. **Skill:** версионированный декларативный manifest + Markdown-инструкции, схемы input/output, разрешённые tools/capabilities. Можно создать без изменения Python-кода; роль/профиль подключает skill по имени/версии.
2. **Tool plugin:** Python package с manifest и явным typed protocol, зарегистрированный в отдельном plugin registry. Доверенные пакеты обнаруживаются через Python packaging entry points (`importlib.metadata`). Исполнение новых/сгенерированных plugins — **в отдельном ограниченном subprocess или контейнере**, не через `importlib` в привилегированном процессе оркестратора.
3. **Core evolution:** изменение engine, policies, схем, CLI adapters и workflow само по себе **не** hot-loaded plugin. Агент создаёт branch/PR, запускает проверки, получает независимое review и человеческое approval перед merge/deploy.

Нельзя превращать произвольный Python-скрипт из каталога `skills/` в автоматически доверенный инструмент.

**Непрерывное обучение на опыте:** разбор сессий, reflection, memory/skill proposals, version pinning, corrections, approval и rollback — самостоятельный контур по [ADR-0005](0005-conversation-reflection-memory-and-skill-evolution.md). Он не имеет права обходить ограничения на создание исполняемых плагинов и изменение ядра.

### 3. Контракты и registry

~~~python
from typing import Protocol, Any

class ToolPlugin(Protocol):
    name: str
    version: str

    async def invoke(self, request: dict[str, Any]) -> dict[str, Any]:
        ...

class AgentBackend(Protocol):
    async def probe(self) -> dict[str, Any]:
        ...

    async def invoke(self, invocation: dict[str, Any]) -> dict[str, Any]:
        ...
~~~

Это **логические протоколы**; IPC-схемы для sandboxed plugins — JSON Schema (versioned request/result/events), а не передача живых Python объектов. Registry проверяет id/version, capabilities, разрешения, hash исходников/зависимостей, pinned environment и compatibility range. При ошибке plugin не должен ломать orchestrator.

Рекомендуемая структура:

~~~text
taskforge/
  core/           # workflow, state, policy, budgets
  backends/       # codex_cli.py, claude_cli.py
  roles/          # bindings, profiles, instructions
  registry/       # skills/tools discovery and activation
  runner/         # subprocess supervision and IPC
  storage/        # versioned durable artifacts/state
  commands/       # typed git/dotnet/build/test
skills/            # declarative skill manifests + prompts
plugins/           # externally versioned package manifests
tests/
~~~

### 4. Контролируемый цикл self-development

~~~text
Need a capability
  -> Architect proposes skill/plugin spec + acceptance tests
  -> Developer (Codex or Claude CLI) implements in isolated git worktree
  -> Deterministic tests + security/capability checks
  -> Independent Reviewer (profile can use another CLI/model)
  -> Stage plugin in quarantined registry
  -> Human approves activation (default)
  -> Activate pinned version for NEW runs
  -> Observe metrics / disable or roll back on failure
~~~

Для заранее разрешённых низкорисковых декларативных skills можно позже добавить opt-in auto-activation policy; этот режим не распространяется на Python executable plugins и core changes. Агент никогда не редактирует собственные policy gates, чтобы одобрить расширение. Запрос разрешения и его решение должны храниться в audit trail.

### 5. Изоляция и границы

- Создание файла не эквивалентно праву запустить его. Generated code — недоверенный.
- Plugin manifest не может сам расширять свои permissions. Network, filesystem, secrets, subprocess и external tool calls требуют explicit grants.
- Install/upgrade dependencies не выполняется внутри запущенного core процесса; использовать отдельные lockfile/venv, проверку разрешённых источников, sandbox и audit.
- Plugin code исполняется вне процесса TaskForge с timeout, ограничениями ресурсов, structured IPC; отказ — fail closed.
- Роли и skills — data для CLI, а не право на дополнительные background workers. Внешние Codex/Claude CLI остаются под единым лимитом invocations/coding runs.
- Запрещено бесконтрольное `exec/eval`, автоматическое применение изменений к core, скрытое обновление environment/settings и silent plugin activation.
- Работа с untrusted repositories не разрешена до полноценного sandbox для CLI, build/test и plugins (ADR-0002).

### 6. Не переписывать весь MVP одним шагом

До выполнения migration acceptance gates .NET 10 `main` остаётся рабочей реализацией/источником истины, хотя **Python-first принят как целевое направление**. Этапы перехода:

1. **Python spike:** запустить обе установленные CLI из Python, проверить non-interactive outputs, model selection, cancellation, auth probing, базовый role binding и isolated workspace; test doubles обязательны.
2. **Plugin + learning vertical slice:** Reflection Agent анализирует разрешённый завершённый чат с подтверждённой пользовательской коррекцией и создаёт staged предложение skill-create/patch; отдельный Curator/approval manager проверяет его по ADR-0005. Также агент создаёт один изолированный Python tool plugin; для него проверяются sandbox, ручное одобрение, активация и rollback end-to-end.
3. **Core parity:** перенести state machine, TaskPacket, budgets, deterministic build/test, git worktree, inspect, JSONL usage и artifacts. Сохранять чтение существующих TaskPacket v3, явно version/migrate новые.
4. **Acceptance:** сравнить Python с текущим .NET по реальному E2E, семантике ошибок, безопасности, времени/расходу CLI и восстановлению после сбоев.
5. Только после прохождения acceptance gates перевести Python в основную реализацию. До этого .NET не удалять, не переписывать `main` массовым PR.

### 7. Критерии принятия перехода на Python

- Для одинакового task input Python соблюдает те же run limits, write-scope, no-implicit-merge, artifact completeness и terminal states.
- Отдельный Claude/Codex profile может быть назначен любой разрешённой роли, модель задаётся явно и записывается в trace.
- Python plugin создаётся агентом и устанавливается **только после** проверок/одобрения, запускается изолированно и может быть отключён без restart основного процесса (новые invocations используют обновлённый registry snapshot).
- Run-level configuration immutable; уже выполняющаяся задача не меняет capabilities при регистрации нового plugin.
- При падении plugin, некорректном IPC, timeout, попытке повысить права или скрытой CLI-делегации TaskForge останавливает операцию без расширения лимитов.
- Существующий .NET MVP остаётся доступен как fallback до прохождения миграционных проверок.

## Рассмотренные альтернативы

- **Сохранить .NET и добавить систему plugins:** возможно и экономит rewrite; целесообразно, если Python spike не даст явной выгоды. Саморазвитие возможно через отдельные Python tools даже при .NET core.
- **Гибрид .NET core + Python plugin host:** минимальный риск миграции и подходящий промежуточный вариант; усложняет IPC и поддержку двух runtimes, но это управляемая цена.
- **Python core с произвольными in-process plugins:** отклонено для agent-generated code — ошибки и вредоносный/ошибочный код получают полномочия ядра.
- **Core, который автоматически мерджит собственные изменения:** отклонено на начальном этапе; нарушает trust/budget boundary.

## References

- Python packaging entry points: https://packaging.python.org/en/latest/specifications/entry-points/
- Codex CLI reference: https://developers.openai.com/codex/cli/reference
- Claude Code CLI reference: https://code.claude.com/docs/en/cli-reference
