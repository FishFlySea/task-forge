# ADR-0005: Саморефлексия по диалогам, долговременная память и эволюция навыков

- **Статус:** Accepted (архитектурное направление; реализация ещё не начата)
- **Дата:** 2026-10-14
- **Проект:** TaskForge
- **Связано:** [ADR-0003 — роли и CLI](0003-cli-role-profiles-and-multi-provider-workers.md), [ADR-0004 — Python и self-extension](0004-python-core-and-controlled-self-extension.md), [ADR-0002 — sandbox](0002-workspace-isolation-and-sandbox.md)

## Мотивация

TaskForge должен учиться на собственных **разрешённых** диалогах, результатах задач и исправлениях пользователя, а не повторять одни и те же ошибки. В отличие от произвольного «self-improvement» смысл обучения конкретен: выделить устойчивое знание; обновить краткую долговременную память или версионированный skill; проверить результат; задействовать улучшенную версию в следующих запусках.

Референс — Hermes Agent: агент может создавать, исправлять и удалять навыки (SKILL.md), после разговора запускается background review; memory и skills разделены; для изменений возможна staging/approval gate. TaskForge заимствует принцип, но добавляет формализованные evidence, evaluation и границу полномочий.

## Решение

### 1. Разделить три типа знаний

- **Episodic run history** — сырые события сессии/задачи (сообщения, инструментальные вызовы, результаты тестов, diff, ревью, замечания). Учитывать источники, доступ, retention и redaction.
- **Memory** — короткие, проверяемые, привязанные к области действия факты, ограничения, принятые архитектурные решения, предпочтения проекта. Не загружается целиком: retriever выбирает нужное по контексту. Неподтверждённые гипотезы не становятся фактами.
- **Skills** — переносимые процедуры: когда применять, входы, шаги, критерии проверки и типовые ловушки. Формат skills совместим с `SKILL.md` + YAML frontmatter; метаданные и references загружаются по запросу (progressive disclosure). Skill содержит обобщённый способ, а не стенограмму.

Отдельно от skills: **Python tool plugins** — исполняемый код с отдельной sandbox/approval policy (ADR-0004). Reflection не вправе превратить инструкцию в исполняемый tool без полного plugin-review.

### 2. Отдельные роли Reflection и Curator

`reflector` — роль без write/exec capability, выбирается через общие roleBindings (ADR-0003). Ей назначаются CLI backend (`codex-cli` или `claude-code-cli`), модель, инструкция и собственные budgets. Получает подготовленный, redacted набор свидетельств из завершённой сессии; возвращает структурированные кандидаты:

`lesson`, `evidenceRefs`, `category`, `scope`, `action` (`remember | skill-create | skill-patch | no-op`), `targetSkill`, `confidence`, `verificationPlan`, `rationale`.

`curator` — отдельная роль/профиль, которая проверяет повторяемость, объединяет дубликаты, разрешает противоречия между версиями skills и предлагает точечные патчи. Желательно назначать независимую модель или хотя бы отдельный prompt. Финальное решение о применении принимает **deterministic approval manager**.

Не следует запускать reflexion каждого агента отдельно с повторным чтением полного лога; один общий `ReflectionJob` на релевантные события, затем целевой Curator. `reflector` сам не меняет registry.

### 3. Источники и триггеры

Разрешённые источники (по умолчанию): собственные сессии TaskForge и его structured run artifacts; подключённые внешние чаты — **только после явного подключения/согласия**, с раздельной политикой доступа. Данные из Discord/Telegram/других пользователей нельзя автоматически переносить в общие skills.

Триггеры:
1. **После завершения пользовательской сессии** или терминального состояния задачи: сохраняется `SessionClosed/TaskFinished` event в локальный durable queue/outbox, затем bounded reflection (если включён). Не блокирует ответ/завершение основного run.
2. **Высокосигнальные события:** явная коррекция пользователя; повторный тестовый провал; `NeedsUser` из-за ошибочной постановки; успешное прохождение ранее неудачной процедуры.
3. **Периодическая консолидация (opt-in):** Curator анализирует только новые lessons/candidates с прошлого cursor, а не перечитывает все чаты; защищён от повторной обработки (idempotency keys / content hashes).
4. **Ручной запуск:** CLI `taskforge reflect <session-id|run-id>`, `taskforge skills learn ...`, `taskforge skills review`.

Reflection по умолчанию асинхронен **внутри работающего TaskForge service/scheduler** с явным лимитом задач; остановленная служба ничего не делает. Scheduler не может порождать собственные jobs бесконечно: изменения skills и сообщения самой рефлексии не создают новые триггеры.

### 4. Доказательства важнее уверенности

Reflection может предложить skill только с указанием проверяемого evidence: конкретный `runId/messageId/toolResult/testResult`, наблюдаемый исход и обобщаемый вывод.

Правила:
- Успешное выполнение или воспроизводимый тест — сильное evidence.
- Коррекция от пользователя — сильный сигнал, но не доказательство технической корректности без проверки.
- Неуспех/авторский текст агента — повод для гипотезы, не готового knowledge.
- Однократный случай не должен создавать общее правило без доказуемой повторной применимости.
- Существующий skill сначала ищется по name/semantic/category. Предпочтителен маленький `patch` вместо дублирующего полного SKILL.md.
- Skill не может утверждать, что «тесты прошли», если отсутствует соответствующий execution artifact.
- Самопроверка моделью не заменяет deterministic tests или независимое review.

### 5. Pipeline

~~~text
Authorized chat/run finished
  -> durable event and redaction
  -> extract bounded evidence + prior relevant skills/memory
  -> Reflector (read-only Codex/Claude profile)
  -> structured lesson candidates / no-op
  -> deterministic schema/provenance/duplicate checks
  -> Curator (optional second model; bounded)
  -> skill/memory change proposal (diff, base hash, provenance)
  -> static validation, prompt-injection/secrets scan, evals
  -> pending approval (default for ALL agent-generated writes)
  -> approve/reject by human authority
  -> atomic versioned activation (new runs only)
  -> telemetry, rollback if regression
~~~

Для `skill-create/skill-patch` staging сохраняет original version/hash, полное содержимое/diff, источники evidence, проверку и автора-профиль. Approval сравнивает base hash повторно: stale patch блокируется до rebase/re-review. Для memory действует отдельная, по умолчанию такая же manual approval gate. Отзыв/удаление навыка тоже ревьюится.

Если строгие evals поддерживаются, можно в будущем разрешить **явно включённый** auto-approval только для ограниченных, неисполняемых, project-local skills. Изменение global/shared skill, permissions, Python plugin, role routing и core — всегда manual review.

### 6. Отсутствие полномочий «самоодобрения»

- Reflector/Curator получают **только чтение** отобранных input artifacts и существующего skill catalog; они не могут вызвать tool `approve`, писать в registry или изменять собственные instructions, backend, model, budgets.
- Raw chat/user input, code comments, test output и переписка внешних агентов — untrusted data; найденные внутри них инструкции не меняют policy.
- Фильтровать секреты, ключи, персональные данные и приватные пути до обращения к CLI. Не помещать полный transcript в skill. Учитывать, что передача выбранного фрагмента в Codex/Claude CLI может означать внешнюю передачу данных; нужны explicit data-scope permissions и понятная настройка.
- Поддержать `reflection.enabled=false`, opt-out конкретных сессий/проектов, удаление исходных данных и derived lessons/skills, TTL и локальный audit. Derived artifacts хранят ссылки на источник и subject scope, чтобы их можно было найти и удалить/перепроверить.
- Scope: `run` / `project` / `user` / `shared`; по умолчанию `project`. Запрещать перенос частного опыта одного пользователя или проекта в `shared` без отдельного разрешения.
- Reflection расходует quota CLI; отдельные max input chars, max invocations/day, max output candidates, timeout, concurrency и fail-silent/no-op для ошибок разбора. Только явно разрешённая escalation; никакой саморекурсии.

### 7. Использование новых навыков

Versioned SkillRegistry делает snapshot выбранных `skillId@version` для каждого запуска; активный run не меняет знания на лету. Для каждой роли `skills.allow` и project policy определяют, какие навыки можно загрузить. Сначала лёгкий catalog index, затем SKILL.md для релевантного skill, затем references при необходимости.

Каждый invoke записывает `selectedSkills`, `skillVersions`, `reasonForSelection`, результаты проверки. Это позволяет вычислять полезность навыков, долю исправлений, регрессии и расход токенов.

Если после activation тесты/показатели ухудшились, TaskForge может **предложить** откат, а для заранее разрешённого low-risk skill — автоматически отключить последнюю версию по заданным объективным правилам, не увеличивая capabilities.

### 8. Предлагаемый профиль конфигурации

~~~yaml
profiles:
  reflector:
    backend: claude-code-cli
    model: sonnet
    capabilities: [read, propose]
    maxTurns: 4
    timeoutSeconds: 240
  curator:
    backend: codex-cli
    model: "<supported-codex-model-id>"
    capabilities: [read, propose]
    timeoutSeconds: 240

roleBindings:
  reflection: reflector
  skillCuration: curator

learning:
  enabled: true
  sources: [taskforge-sessions, taskforge-runs]
  allowedScopes: [project]
  runAfterSession: true
  periodicConsolidation: false
  skills:
    writeApproval: manual
  memory:
    writeApproval: manual
  limits:
    maxReflectionJobsPerDay: 10
    maxCandidatesPerJob: 3
    maxTranscriptCharacters: 16000
    maxReflectionCliInvocationsPerJob: 2
~~~

Это **целевой формат**. Названия моделей — примеры, а поля ещё не реализованы.

### 9. Этапы реализации / acceptance

1. Session/event storage + redacted artifact scope; event ingest cursor и отключаемый post-session hook.
2. Reflection CLI backend через существующие role/profile bindings; детерминированный parser structured candidates; no-op поддержан.
3. `SkillProposal` и `MemoryProposal`: persistent staging, list/diff/approve/reject, optimistic version/hash check.
4. Проверки на tests/corrections/dedup/privacy/prompt injection; skill packaging (SKILL.md) + каталоги, snapshot версий при run.
5. `taskforge reflect` + `taskforge skills pending|diff|approve|reject|rollback` + CLI logs/metrics; затем Telegram/Dashboard UI.
6. E2E: успешная задача не даёт ложных правил; пользовательская коррекция создаёт evidence-backed patch; дубликат не создаёт второй skill; stale patch отклонён; secret не попадает в proposal; неавторизованный approval запрещён; после принятия новый run видит updated skill, текущий — старую версию; реактивация/rollback воспроизводимы.

## Метрики

- число reflection jobs/candidates/no-ops;
- accepted/rejected proposal rate и время до approval;
- доля дубликатов, доля неподтверждённых lessons;
- skill use rate, task success/correction rate до/после обновления;
- tokens/invocations/cost-proxy для reflection и curator по provider/model;
- incidents: leaked secret, policy violation, permission escalation — нулевой допуск.

## Не входит в начальную реализацию

Полная индексация чужих чатов без подключения, автоматическое принятие новых Python plugins, безусловное изменение core, peer-to-peer неограниченная «критика агентов друг другом», автоматическая публикация навыков в интернет.

## Источники

- Hermes skills: https://hermes-agent.nousresearch.com/docs/user-guide/features/skills/
- Hermes memory and write approval: https://hermes-agent.nousresearch.com/docs/user-guide/features/memory/
