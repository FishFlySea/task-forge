# RFC-0001: MVP implementation

- **Статус:** Superseded
- **Дата:** 2026-10-01
- **Superseded:** 2026-10-02

Этот RFC был первым подробным implementation proposal и смешивал архитектурные решения с изменяемыми деталями реализации.

После review документ разделён:

- [ADR-0001](../adr/0001-controlled-multi-agent-architecture.md) — стабильные архитектурные решения: TaskForge владеет orchestration, Codex является leaf worker, hard budget/enforcement, tiering;
- [ADR-0002](../adr/0002-workspace-isolation-and-sandbox.md) — worktree isolation, sandbox, network/secrets/write-scope policy;
- [docs/design/orchestrator.md](../design/orchestrator.md) — state machine, TaskPacket schema, worker contracts, routing, metrics и implementation gaps.

Git history этого файла сохраняет исходный RFC. Новые изменения workflow должны вноситься в design document, а изменения архитектурных инвариантов — отдельным ADR.
