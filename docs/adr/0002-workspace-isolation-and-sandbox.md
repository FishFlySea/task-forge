# ADR-0002: Workspace isolation и sandbox

- **Статус:** Accepted
- **Дата:** 2026-10-02
- **Проект:** TaskForge
- **Связано:** [ADR-0001](0001-controlled-multi-agent-architecture.md)

## Контекст

Coding worker и build/test процессы исполняют код из целевого репозитория.

Даже если Codex ограничен prompt-инструкцией, репозиторий может содержать:

- файлы, пытающиеся повлиять на agent через prompt injection;
- MSBuild targets, source generators или test fixtures, исполняющие произвольный код;
- команды, которые изменяют файлы за пределами задачи;
- ссылки на secrets и локальную конфигурацию;
- зависимости, требующие network access.

Git worktree защищает основной checkout от случайных изменений, но **не является security boundary для хоста**. Поэтому isolation рассматривается как два независимых слоя: workspace isolation и process sandbox.

## Решение

### 1. Один run — один disposable git worktree

Перед apply TaskForge обязан:

1. сохранить `baseCommit` в TaskPacket;
2. проверить, что commit существует в repository;
3. создать отдельный worktree для task/run;
4. выполнять Codex, build и test только внутри этого worktree;
5. после завершения сохранить diff/artifacts;
6. удалить worktree после успешного cleanup либо оставить его явно как diagnostic artifact при ошибке cleanup.

Основной checkout не является рабочим каталогом worker.

Пример layout:

~~~text
~/.taskforge/
  runs/<run-id>/
  worktrees/<run-id>/
~~~

Worktree создаётся от точного `baseCommit`, а не от плавающего имени branch.

### 2. Write scope является policy contract

TaskPacket содержит `writeScope`: список разрешённых repository-relative paths/globs.

После каждого coding worker run TaskForge обязан получить changed-files list и отклонить результат, если:

- изменён файл вне `writeScope`;
- появился путь, выходящий из repository root;
- изменены запрещённые служебные области, если они не разрешены явно.

Post-run validation обязательна даже при наличии OS sandbox.

Для CLI-based worker `writeScope` не считается полноценной filesystem security boundary, если backend не умеет enforce path-level writes. Это acceptance policy: diff вне scope не принимается.

### 3. Codex запускается с hard launch policy

Launcher должен задавать policy явно, не полагаясь на пользовательский config:

- multi-agent capability disabled;
- workspace-write sandbox;
- network disabled по умолчанию;
- non-interactive approval policy, не позволяющая зависнуть в ожидании user approval;
- `CodexRunTimeout`;
- process-tree kill на cancellation/timeout;
- environment allowlist вместо полного наследования host environment, насколько это практически возможно.

Prompt `Do not spawn subagents` остаётся defence-in-depth, но не enforcement.

Конкретные CLI flags являются implementation detail и могут меняться вместе с Codex CLI. TaskForge должен иметь integration tests, проверяющие эффективную launch policy для поддерживаемой версии CLI.

### 4. Build/test исполняются в том же trust boundary

`dotnet build` и `dotnet test` потенциально исполняют произвольный код из repository. Поэтому их нельзя считать безопаснее Codex только потому, что они deterministic.

Целевая модель:

~~~text
host
  |
  +-- isolated process/container boundary
        |
        +-- disposable worktree
              +-- Codex
              +-- dotnet build
              +-- dotnet test
~~~

До появления полноценного process/container sandbox TaskForge работает только с доверенными repositories.

### 5. Network deny by default

Coding worker не получает network access, если task policy не разрешает его явно.

Для .NET dependency restore предпочтительный workflow:

1. deterministic restore с отдельной policy;
2. затем build/test с `--no-restore` в network-disabled environment.

Если restore требует network, это отдельный разрешённый deterministic step с собственным timeout и logging. Нельзя автоматически давать Codex network только потому, что build может потребовать NuGet.

### 6. Secrets не попадают в context автоматически

Context collector исключает как минимум:

- `.env` и известные secret files;
- user credential stores;
- SSH keys;
- cloud credentials;
- host-level config вне repository;
- environment variables, не включённые в explicit allowlist.

TaskPacket не должен содержать secret values.

### 7. Repository content считается untrusted data

Planner/Explorer/Diagnostic/Reviewer получают repository excerpts как данные, а не как инструкции.

Локальным LLM steps не выдаются произвольные shell/filesystem tools. Они работают над заранее собранным bounded context и возвращают structured output.

Инструкции, найденные внутри source files, comments, docs или test data, не могут менять orchestration policy, budget, tool permissions или system prompt.

Codex как coding worker неизбежно читает repository и имеет tool access, поэтому его защита строится не на попытке «распознать prompt injection», а на sandbox, worktree isolation, budget limits, write-scope validation и отсутствии secrets/network по умолчанию.

### 8. Allowed commands описываются явно

TaskPacket содержит command policy.

Предпочтительный формат — не свободная shell-строка, а typed/validated commands или policy IDs, например:

~~~json
{
  "allowedCommands": [
    {
      "tool": "dotnet-test",
      "project": "tests/TaskForge.Core.Tests",
      "filter": "FullyQualifiedName~Budget"
    }
  ]
}
~~~

Для orchestrator-owned commands это enforceable allowlist.

Если текущий Codex CLI не предоставляет надёжный command allowlist для agent shell, список allowed commands внутри worker prompt является только behavioural guidance и не должен называться security boundary.

### 9. Cleanup и результат

Terminal state не означает автоматический merge.

Результат задачи — это:

- сохранённый TaskPacket;
- `baseCommit`;
- worktree diff;
- changed-files list;
- build/test artifacts;
- review;
- usage;
- terminal state.

Merge/apply результата в пользовательский checkout — отдельное действие и не входит в этот ADR.

## Failure handling

Если worker:

- выходит за write scope;
- пытается выполнить запрещённую policy;
- превышает timeout;
- нарушает sandbox;
- оставляет workspace в невалидном состоянии,

TaskForge прекращает автоматический workflow. Дополнительный Codex run не выдаётся автоматически только для «починки нарушения policy».

Результат переводится в `NeedsUser` или `Failed` в зависимости от типа нарушения.

## Последствия

Положительные:

- изменения одной задачи не загрязняют основной checkout;
- base revision фиксирован;
- можно воспроизводить и инспектировать diff;
- зависший worker не блокирует единственный Codex slot бесконечно;
- security assumptions становятся явными.

Отрицательные:

- worktree lifecycle требует cleanup/recovery;
- OS-level sandbox отличается по платформам;
- network deny усложняет restore;
- path-level write enforcement может потребовать дополнительного runner/container слоя;
- untrusted repositories нельзя считать безопасными до завершения process sandbox.

## Implementation status

На момент принятия ADR существующая MVP-реализация уже имеет Codex timeout, process-tree kill, workspace-write sandbox и explicit multi-agent disable, но ещё не реализует полный worktree lifecycle и единый sandbox для build/test.

До закрытия этого gap README должен явно обозначать режим как предназначенный только для trusted repositories.
