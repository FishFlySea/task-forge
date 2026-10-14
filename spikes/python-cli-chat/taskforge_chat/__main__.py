"""Simple CLI REPL. First priority: choose provider/model -> chat -> native tools."""
from __future__ import annotations

import argparse
import asyncio
from pathlib import Path
import sys

from .core import ChatError, SessionStore, ask, doctor


async def main() -> None:
    parser = argparse.ArgumentParser(prog="python -m taskforge_chat",
                                     description="TaskForge Python chat CLI proof of concept (read-only tools)")
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="Trusted repo to inspect")
    parser.add_argument("--backend", choices=("codex", "claude"), help="Installed coding CLI")
    parser.add_argument("--model", help="Exact installed CLI model ID/alias")
    parser.add_argument("--resume", help="TaskForge chat ID from /status or /sessions")
    parser.add_argument("--state-dir", type=Path, default=Path.home() / ".local/share/taskforge")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--executable", help="Override CLI binary path (testing/custom installations)")
    args = parser.parse_args()
    store = SessionStore(args.state_dir)
    if args.resume:
        session = store.load(args.resume)
        if args.backend and args.backend != session.backend or args.model and args.model != session.model:
            raise ChatError("Resume uses stored backend and model; /new to switch")
        if args.repo != Path.cwd() and args.repo.expanduser().resolve() != Path(session.repo):
            raise ChatError("Cannot resume from different repository")
    else:
        backend = args.backend
        if not backend:
            print("1. Codex CLI\n2. Claude Code CLI")
            select = input("Backend [1/2]: ").strip()
            if select not in ("1", "2"):
                raise ChatError("Choose 1 or 2")
            backend = "codex" if select == "1" else "claude"
        model = args.model or input("Model (exact id/alias): ").strip()
        session = store.create(backend, model, args.repo)
    version = await doctor(session.backend, args.executable)
    print(f"TaskForge chat {session.id}\n{session.backend} {session.model} ({version})\n"
          f"Repository: {session.repo}\nPermissions: read-only native tools\n"
          "Commands: /help, /status, /sessions, /new, /model <id>, /backend <codex|claude> <id>, /use <session-id>, /quit")
    while True:
        try:
            prompt = input("\nYou > ").strip()
        except (KeyboardInterrupt, EOFError):
            print("\nBye")
            return
        if not prompt:
            continue
        if prompt in ("/quit", "/exit"):
            return
        if prompt == "/help":
            print("/new creates a fresh chat; /status shows session ID; /sessions lists past chats.\n"
                  "/model <id> starts a new chat with the selected model.\n"
                  "/backend <codex|claude> <id> changes the provider and model in a new chat.\n"
                  "/use <TaskForge-session-id> opens a saved chat.\n"
                  "Changing a provider/model never silently reuses incompatible context.")
            continue
        if prompt == "/status":
            print(f"Session: {session.id}\nProvider: {session.backend} / {session.model}\n"
                  f"Provider session: {session.provider_session_id or '(new)'}\n"
                  f"Repo: {session.repo}\nCompleted turns: {len(session.turns)}")
            continue
        if prompt == "/sessions":
            for s in store.list():
                print(f"{s.id}  {s.backend}/{s.model}  {len(s.turns)} turns  {s.repo}")
            continue
        if prompt == "/new":
            session = store.create(session.backend, session.model, Path(session.repo))
            print(f"New session: {session.id}")
            continue
        if prompt.startswith("/model "):
            model = prompt.partition(" ")[2].strip()
            try:
                session = store.create(session.backend, model, Path(session.repo))
                print(f"New session with model {model}: {session.id}")
            except ChatError as exc:
                print(f"[error] {exc}")
            continue
        if prompt.startswith("/backend "):
            parts = prompt.split(maxsplit=2)
            if len(parts) != 3 or parts[1] not in ("codex", "claude"):
                print("Usage: /backend <codex|claude> <model-id>")
                continue
            try:
                print(f"Found {await doctor(parts[1], args.executable if parts[1] == session.backend else None)}")
                session = store.create(parts[1], parts[2], Path(session.repo))
                # Executable override is backend-specific; discard it on switches.
                args.executable = None
                print(f"New session {session.id}: {session.backend}/{session.model}")
            except ChatError as exc:
                print(f"[error] {exc}")
            continue
        if prompt.startswith("/use "):
            try:
                selected = store.load(prompt.partition(" ")[2].strip())
                print(f"Found {await doctor(selected.backend)}")
                session = selected
                args.executable = None
                print(f"Resumed {session.id}: {session.backend}/{session.model}")
            except ChatError as exc:
                print(f"[error] {exc}")
            continue
        if prompt.startswith("/"):
            print("Unknown command. See /help")
            continue
        try:
            answer = await ask(session, store, prompt, lambda t: print(f"  [tool] {t}", flush=True),
                               timeout=args.timeout, executable=args.executable)
        except ChatError as exc:
            print(f"[error] {exc}", file=sys.stderr)
            continue
        print("\nAssistant > " + answer.answer)
        if answer.usage:
            print("[usage] " + str(answer.usage))


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except ChatError as e:
        print(f"Error: {e}", file=sys.stderr)
        raise SystemExit(1) from e