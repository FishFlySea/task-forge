# TaskForge Python CLI Chat Spike

This spike lives in `spikes/python-cli-chat/` on branch `feature/python-cli-chat-spike`.

**Goal:** Open a terminal chat, select an installed Codex CLI or Claude Code CLI
and model, ask it to inspect a trusted project using its **native tools**, see tool
calls and continue the same provider session across multiple prompts.

This is the next minimal deliverable before role orchestration and reflective skills.
Not a replacement for the existing .NET 10 production code.

## Linux quickstart

Requirements: Python 3.11+ (stdlib only), `git`, installed and **already authenticated**
`codex` and/or `claude`. Claude Code >= 2.1.248 is required for `--restricted`.

```bash
cd spikes/python-cli-chat
python3 -m unittest discover -s tests -v
python3 -m taskforge_chat --repo /home/codex/src/signal-platform --backend claude --model sonnet
# or, use an explicit Codex model ID supported by your Codex CLI:
python3 -m taskforge_chat --repo /home/codex/src/signal-platform --backend codex --model '<model-id>'
# or, run without --backend/--model for interactive selection:
python3 -m taskforge_chat --repo /home/codex/src/signal-platform
```

Once in chat:

```text
You > Find the .sln files in this repository and explain the project structure. Use your available tools.
  [tool] Read
Assistant > ...
You > Which projects contain unit tests?
Assistant > ...
You > /status
You > /quit
```

Resume later with the ID from `/status` or `/sessions`:

```bash
python3 -m taskforge_chat --resume <taskforge-session-id>
```

`/new` creates an independent session with the same provider/model/repository.
`/model <id>` opens a new session with another model; `/backend claude sonnet`
(or `codex <model-id>`) switches CLI and model without inheriting an incompatible
conversation. `/use <id>` opens a saved TaskForge chat. Old sessions remain intact. History is saved under
`~/.local/share/taskforge/chat/*.json` (mode 0600), **including user prompts and
assistant final replies**, but not raw tool output/trace. Each reply is produced
by a separate process that resumes the vendor's session ID.

## Large CLI events

Claude Code can emit one JSONL event containing a full `Read` tool result.
The event may be much larger than Python's default 64 KiB stream line limit.
The reader consumes bounded chunks and reconstructs JSONL events; the current
per-turn stdout cap is 16 MB. Exceeding the cap now reports a `ChatError`
without persisting a failed turn, rather than crashing the terminal session.

## Diagnosing Codex failures

Codex may report errors as JSONL events in stdout while leaving stderr empty.
TaskForge now displays the parsed `turn.failed` / `error` message even when
`codex exec` exits with a nonzero code. If it reports no diagnostic at all,
verify model availability, authentication and local CLI configuration directly.

Example using the **same** read-only policy (from a trusted repo):

```bash
printf 'Reply with OK\n' | codex exec --json --model 'gpt6-luna' \
  --sandbox read-only -c approval_policy=never \
  -c features.multi_agent=false -c web_search=disabled -
```

Inspect JSON events with `jq`, but do not post entire raw logs publicly: they
may contain private project context or tool results. A model name here is only
an example, not a guarantee that the authenticated account supports it.

## Scope, known limitations

- Native tools are deliberately **read-only**: Codex uses `read-only` sandbox;
  Claude uses `--restricted` and `--tools Read,Glob,Grep`. The displayed events
  are provider-native tool activity. This prototype does not implement new MCP
  tools, skills, local command dispatch, or write-mode worktrees.
- This is **not a security boundary for untrusted repositories**: Codex user
  config/MCP, CLI hooks, inherited provider settings and host capabilities have
  not been fully isolated. Use a trusted repository with no secrets accessible
  to unwanted provider tools; inspect CLI settings first.
- Agent model calls require network access and may send file excerpts to the
  provider even though sandboxed shell network is disabled. Don't test with
  private files you are not authorized to share.
- The models are selected by CLI model ID or alias, without forced fallback.
  The `doctor` check verifies the CLI version/executable, not credentials or
  subscription model access. A real query confirms model availability.
- Tests use **fake CLI subprocesses**. No authenticated Codex or Claude calls
  were run in this environment. Current vendor JSON event structure can change;
  command flags/permission policy must be verified on your Linux versions.
- No automatic Reflection Agent, prompt sanitization or secret redaction yet.
- Only one foreground chat turn at a time; no concurrency/daemon/web UI.