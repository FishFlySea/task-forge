"""Provider-neutral, read-only native-tool CLI chat SPIKE.

Not a host sandbox: project/user settings, hooks and external MCP need independent
isolation before running this against untrusted repositories.
"""
from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import signal
import tempfile
from typing import Any, Callable
from uuid import uuid4

MAX_EVENTS_BYTES = 16_000_000
MAX_PROMPT_CHARS = 32_000
MAX_TURN_SECONDS = 600
MAX_REPLY_CHARS = 200_000


class ChatError(RuntimeError):
    pass


def utcnow() -> str:
    return datetime.now(timezone.utc).isoformat()


def _atomic_json(path: Path, content: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, temp = tempfile.mkstemp(prefix=".taskforge-", dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(content, f, ensure_ascii=False, indent=2)
            f.flush()
            os.fsync(f.fileno())
        os.replace(temp, path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


@dataclass
class ChatSession:
    id: str
    backend: str
    model: str
    repo: str
    provider_session_id: str | None = None
    turns: list[dict[str, Any]] = field(default_factory=list)
    created_at: str = field(default_factory=utcnow)

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> ChatSession:
        expected = {"id", "backend", "model", "repo"}
        if not expected <= value.keys():
            raise ChatError("Malformed session metadata")
        return cls(id=value["id"], backend=value["backend"], model=value["model"],
                   repo=value["repo"], provider_session_id=value.get("provider_session_id"),
                   turns=value.get("turns", []), created_at=value.get("created_at", utcnow()))

    def to_dict(self) -> dict[str, Any]:
        return vars(self).copy()


class SessionStore:
    """Session metadata only: no raw tool results, CLI trace, or credentials stored."""
    def __init__(self, root: Path):
        self.root = root

    def _path(self, session_id: str) -> Path:
        if not re.fullmatch(r"[a-f0-9]{32}", session_id):
            raise ChatError("Invalid TaskForge session id")
        return self.root / "chat" / f"{session_id}.json"

    def create(self, backend: str, model: str, repo: Path) -> ChatSession:
        if backend not in ("codex", "claude"):
            raise ChatError("Expected backend codex or claude")
        if not model or model.startswith("-") or any(c in model for c in "\r\n\x00"):
            raise ChatError("A valid explicit model ID/alias is required")
        path = repo.expanduser().resolve(strict=True)
        if not path.is_dir():
            raise ChatError("--repo must be a directory")
        session = ChatSession(id=uuid4().hex, backend=backend, model=model, repo=str(path))
        self.save(session)
        return session

    def save(self, session: ChatSession) -> None:
        _atomic_json(self._path(session.id), session.to_dict())

    def load(self, session_id: str) -> ChatSession:
        try:
            session = ChatSession.from_dict(json.loads(self._path(session_id).read_text(encoding="utf-8")))
        except (FileNotFoundError, ValueError, TypeError, KeyError) as exc:
            raise ChatError("Cannot read session") from exc
        if session.id != session_id or session.backend not in ("codex", "claude"):
            raise ChatError("Session ID/backend mismatch")
        if not Path(session.repo).is_dir():
            raise ChatError("Session repository no longer exists")
        if session.provider_session_id is not None and not re.fullmatch(r"[0-9a-zA-Z_-]{8,128}", session.provider_session_id):
            raise ChatError("Invalid vendor session ID")
        return session

    def list(self) -> list[ChatSession]:
        found = []
        for path in sorted((self.root / "chat").glob("*.json"), reverse=True):
            try:
                found.append(self.load(path.stem))
            except ChatError:
                pass
        return found


@dataclass
class ChatResult:
    answer: str
    provider_session_id: str | None
    tool_names: list[str]
    usage: dict[str, Any] | None = None


class EventParser:
    """Normalize Codex JSONL and Claude stream-json; show native-tool activity."""
    def __init__(self, backend: str, on_tool: Callable[[str], None]):
        self.backend = backend
        self.on_tool = on_tool
        self.session_id: str | None = None
        self.answer = ""
        self.usage: dict[str, Any] | None = None
        self.tools: list[str] = []
        self._seen_tools: set[str] = set()
        self._failed: str | None = None

    def _tool(self, tool_id: str, tool_name: str) -> None:
        if tool_id in self._seen_tools:
            return
        self._seen_tools.add(tool_id)
        self.tools.append(tool_name)
        self.on_tool(tool_name)

    def feed(self, event: dict[str, Any]) -> None:
        etype = event.get("type")
        if self.backend == "codex":
            if etype == "thread.started":
                self.session_id = event.get("thread_id")
            elif etype in ("item.started", "item.completed", "item.updated"):
                item = event.get("item") or {}
                kind = item.get("type")
                if kind in ("command_execution", "mcp_tool_call", "file_change", "web_search"):
                    tool_label = ("shell: " + str(item.get("command", ""))[:100]) if kind == "command_execution" else kind
                    self._tool(str(item.get("id", "")) or tool_label, tool_label)
                if kind == "agent_message" and etype == "item.completed":
                    self.answer = str(item.get("text") or "")
            elif etype == "turn.completed":
                self.usage = event.get("usage")
            elif etype in ("turn.failed", "error"):
                err = event.get("error") or event
                self._failed = str(err)[:250]
        elif self.backend == "claude":
            if etype == "system" and event.get("subtype") == "init":
                self.session_id = event.get("session_id")
            elif etype == "assistant":
                msg = event.get("message") or {}
                for block in msg.get("content") or []:
                    if isinstance(block, dict) and block.get("type") == "tool_use":
                        name = str(block.get("name") or "tool")
                        self._tool(str(block.get("id") or name), name)
            elif etype == "result":
                self.session_id = event.get("session_id") or self.session_id
                answer = event.get("result")
                if isinstance(answer, str):
                    self.answer = answer
                self.usage = event.get("usage")
                if event.get("is_error"):
                    self._failed = str(event.get("result") or "Claude returned error")[:250]

    def finish(self) -> ChatResult:
        if self._failed:
            raise ChatError(f"Agent failed: {self._failed}")
        if not self.answer:
            raise ChatError("No assistant response in CLI events")
        if len(self.answer) > MAX_REPLY_CHARS:
            raise ChatError("Assistant response too long")
        if self.session_id is not None and not re.fullmatch(r"[0-9a-zA-Z_-]{8,128}", self.session_id):
            raise ChatError("Unexpected vendor session ID")
        return ChatResult(self.answer, self.session_id, self.tools, self.usage)


def build_command(backend: str, model: str, vendor_session_id: str | None,
                  executable: str | None = None) -> list[str]:
    """Native read-only tools. Model/chosen provider are kept explicit for every turn."""
    exe = executable or backend
    if executable is None:
        exe = "codex" if backend == "codex" else "claude"
    if not model or model.startswith("-"):
        raise ChatError("Explicit model required")
    if vendor_session_id and not re.fullmatch(r"[0-9a-zA-Z_-]{8,128}", vendor_session_id):
        raise ChatError("Invalid provider session id")
    if backend == "codex":
        argv = [exe, "exec", "--json", "--model", model,
                "--sandbox", "read-only", "--ask-for-approval", "never",
                "-c", "features.multi_agent=false", "-c", "web_search=disabled",
                "-c", "sandbox_workspace_write.network_access=false"]
        if vendor_session_id:
            argv += ["resume", vendor_session_id, "-"]
        else:
            argv += ["-"]
        return argv
    if backend == "claude":
        argv = [exe, "-p", "--model", model, "--output-format", "stream-json",
                "--verbose", "--restricted", "--tools", "Read,Glob,Grep",
                "--disallowedTools", "mcp__*", "--permission-mode", "plan",
                "--max-turns", "6"]
        if vendor_session_id:
            argv += ["--resume", vendor_session_id]
        return argv
    raise ChatError(f"Unknown backend: {backend}")


async def doctor(backend: str, executable: str | None = None) -> str:
    exe = executable or ("codex" if backend == "codex" else "claude")
    if not shutil.which(exe) and not Path(exe).exists():
        raise ChatError(f"{exe} not installed or missing from PATH")
    p = await asyncio.create_subprocess_exec(exe, "--version", stdout=asyncio.subprocess.PIPE,
                                             stderr=asyncio.subprocess.PIPE)
    try:
        out, err = await asyncio.wait_for(p.communicate(), 10)
    except asyncio.TimeoutError:
        p.kill()
        await p.wait()
        raise ChatError(f"{exe} --version timed out")
    if p.returncode != 0:
        raise ChatError(f"{exe} --version failed: {err.decode(errors='replace')[:200]}")
    version = out.decode(errors="replace").strip()
    if backend == "claude":
        m = re.search(r"(\d+)\.(\d+)\.(\d+)", version)
        if not m or tuple(map(int, m.groups())) < (2, 1, 248):
            raise ChatError("Claude Code >=2.1.248 required for --restricted; upgrade CLI")
    return version


async def _terminate_group(process: asyncio.subprocess.Process) -> None:
    if process.returncode is not None:
        return
    if os.name == "posix":
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    else:
        process.kill()
    await process.wait()


async def run_turn(session: ChatSession, prompt: str, on_tool: Callable[[str], None],
                   timeout: int = 300, executable: str | None = None) -> ChatResult:
    if not prompt.strip() or len(prompt) > MAX_PROMPT_CHARS:
        raise ChatError("Prompt missing or too large")
    if not 1 <= timeout <= MAX_TURN_SECONDS:
        raise ChatError("Timeout out of range")
    cmd = build_command(session.backend, session.model, session.provider_session_id, executable)
    parser = EventParser(session.backend, on_tool)
    process = await asyncio.create_subprocess_exec(
        *cmd, cwd=session.repo, stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        start_new_session=(os.name == "posix"))
    async def capture_stderr() -> bytes:
        # Drain even after the reporting limit, or a verbose CLI may block on stderr.
        assert process.stderr is not None
        head = bytearray()
        while chunk := await process.stderr.read(8192):
            if len(head) < 16384:
                head.extend(chunk[:16384 - len(head)])
        return bytes(head)

    stderr_task = asyncio.create_task(capture_stderr())

    async def collect() -> ChatResult:
        assert process.stdin is not None and process.stdout is not None
        process.stdin.write(prompt.encode("utf-8"))
        await process.stdin.drain()
        process.stdin.close()
        # CLI JSONL lines may contain entire tool results (e.g. Claude Read),
        # which routinely exceed asyncio StreamReader's 64 KiB readline limit.
        # Read bounded chunks and split JSONL ourselves instead of readline().
        total_bytes = 0
        pending = bytearray()

        def handle_line(line: bytes) -> None:
            if not line.strip():
                return
            try:
                event = json.loads(line)
            except (ValueError, UnicodeDecodeError) as exc:
                raise ChatError("Invalid JSONL from CLI") from exc
            if not isinstance(event, dict):
                raise ChatError("CLI event must be an object")
            parser.feed(event)

        while chunk := await process.stdout.read(64 * 1024):
            total_bytes += len(chunk)
            if total_bytes > MAX_EVENTS_BYTES:
                raise ChatError(f"CLI event output exceeded the {MAX_EVENTS_BYTES}-byte turn limit")
            pending.extend(chunk)
            while (newline := pending.find(b"\n")) != -1:
                handle_line(bytes(pending[:newline]))
                del pending[:newline + 1]
        # A valid final JSONL event need not end with a newline.
        if pending:
            handle_line(bytes(pending))
        code = await process.wait()
        if code != 0:
            err = (await stderr_task).decode(errors="replace")
            raise ChatError(f"{session.backend} exited with {code}: {err[:400]}")
        return parser.finish()
    try:
        return await asyncio.wait_for(collect(), timeout)
    except asyncio.TimeoutError as exc:
        raise ChatError(f"Agent exceeded {timeout}s timeout") from exc
    finally:
        if process.returncode is None:
            await _terminate_group(process)
        if not stderr_task.done():
            stderr_task.cancel()
            try:
                await stderr_task
            except asyncio.CancelledError:
                pass


async def ask(session: ChatSession, store: SessionStore, prompt: str,
              on_tool: Callable[[str], None], timeout: int = 300,
              executable: str | None = None) -> ChatResult:
    result = await run_turn(session, prompt, on_tool, timeout, executable)
    # Persist the provider session ID only after a successful completed turn.
    if not result.provider_session_id:
        raise ChatError("Provider omitted session ID; refusing silent stateless chat")
    session.provider_session_id = result.provider_session_id
    session.turns.append({"at": utcnow(), "user": prompt, "assistant": result.answer,
                          "tools": result.tool_names, "usage": result.usage})
    store.save(session)
    return result