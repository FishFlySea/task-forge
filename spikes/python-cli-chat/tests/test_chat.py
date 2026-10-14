import asyncio
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch

from taskforge_chat.core import (ChatError, EventParser, SessionStore, ask,
                                 build_command, doctor, run_turn)


FAKE_CLI = '''#!{python}
import json, os, sys, time
from pathlib import Path
args = sys.argv[1:]
if '--version' in args:
    print('2.1.300 (Claude Code)' if os.environ.get('TF_BACKEND') == 'claude' else 'codex-cli 1.1.0')
    sys.exit(0)
text = sys.stdin.read()
Path(os.environ['TF_FAKE_LOG']).open('a', encoding='utf8').write(json.dumps({{'args': args, 'prompt': text}})+'\\n')
if text == 'sleep':
    time.sleep(4)
    sys.exit(0)
backend = os.environ.get('TF_BACKEND', 'codex')
if backend == 'codex':
    print(json.dumps({{'type': 'thread.started', 'thread_id': '12345678-aaaa-bbbb-cccc-123456789012'}}), flush=True)
    print(json.dumps({{'type':'item.started','item':{{'id':'t1','type':'command_execution','command':'pwd','status':'in_progress'}}}}), flush=True)
    print(json.dumps({{'type':'item.completed','item':{{'id':'t1','type':'command_execution','command':'pwd','status':'completed'}}}}), flush=True)
    print(json.dumps({{'type':'item.completed','item':{{'type':'agent_message','text': 'I inspected the workspace.'}}}}), flush=True)
    print(json.dumps({{'type':'turn.completed','usage':{{'input_tokens':12,'output_tokens':4}}}}), flush=True)
else:
    print(json.dumps({{'type':'system','subtype':'init','session_id':'12345678-aaaa-bbbb-cccc-123456789012'}}), flush=True)
    if text == 'large':
        # One Claude tool result event over 64 KiB; previously readline() crashed.
        print(json.dumps({{'type':'user','message':{{'content':[{{'type':'tool_result','tool_use_id':'t2','content':'x' * 200_000}}]}}}}), flush=True)
    print(json.dumps({{'type':'assistant','message':{{'content':[{{'type':'tool_use','id':'t2','name':'Read','input':{{'file_path':'README.md'}}}}]}}}}), flush=True)
    print(json.dumps({{'type':'assistant','message':{{'content':[{{'type':'tool_use','id':'t2','name':'Read'}}]}}}}), flush=True)
    print(json.dumps({{'type':'result','session_id':'12345678-aaaa-bbbb-cccc-123456789012','result':'I read the README.','usage':{{'input_tokens':25}}}}), flush=True)
'''


class ChatTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.repo = self.root / 'repo'
        self.repo.mkdir()
        self.store = SessionStore(self.root / 'state')
        self.fake = self.root / 'fake-cli'
        self.fake.write_text(FAKE_CLI.format(python=sys.executable), encoding='utf-8')
        self.fake.chmod(0o700)
        self.log = self.root / 'log.jsonl'
        self.old_log = os.environ.get('TF_FAKE_LOG')
        self.old_backend = os.environ.get('TF_BACKEND')
        os.environ['TF_FAKE_LOG'] = str(self.log)

    def tearDown(self):
        if self.old_log is None:
            os.environ.pop('TF_FAKE_LOG', None)
        else:
            os.environ['TF_FAKE_LOG'] = self.old_log
        if self.old_backend is None:
            os.environ.pop('TF_BACKEND', None)
        else:
            os.environ['TF_BACKEND'] = self.old_backend

    async def test_new_codex_session_and_resume_with_tools(self):
        os.environ['TF_BACKEND'] = 'codex'
        sess = self.store.create('codex', 'model-A', self.repo)
        tools = []
        first = await ask(sess, self.store, 'inspect', tools.append, executable=str(self.fake))
        self.assertEqual(first.answer, 'I inspected the workspace.')
        self.assertEqual(tools, ['shell: pwd'])
        self.assertEqual(sess.provider_session_id, '12345678-aaaa-bbbb-cccc-123456789012')
        self.assertEqual(first.usage['input_tokens'], 12)
        second = await ask(sess, self.store, 'continue', tools.append, executable=str(self.fake))
        self.assertEqual(second.answer, first.answer)
        invocations = [json.loads(l) for l in self.log.read_text().splitlines()]
        self.assertEqual(len(invocations), 2)
        self.assertIn('resume', invocations[1]['args'])
        self.assertIn(sess.provider_session_id, invocations[1]['args'])
        self.assertIn('read-only', invocations[0]['args'])
        self.assertNotIn('--ephemeral', invocations[0]['args'])
        self.assertEqual(len(self.store.load(sess.id).turns), 2)
        self.assertEqual(stat.S_IMODE(self.store._path(sess.id).stat().st_mode), 0o600)

    async def test_claude_read_native_tools_and_resume(self):
        os.environ['TF_BACKEND'] = 'claude'
        sess = self.store.create('claude', 'sonnet', self.repo)
        tools = []
        self.assertIn('2.1.300', await doctor('claude', str(self.fake)))
        result = await ask(sess, self.store, 'read README', tools.append, executable=str(self.fake))
        self.assertEqual(result.answer, 'I read the README.')
        self.assertEqual(tools, ['Read'])
        await ask(sess, self.store, 'next', tools.append, executable=str(self.fake))
        invocations = [json.loads(l) for l in self.log.read_text().splitlines()]
        self.assertIn('--restricted', invocations[0]['args'])
        self.assertIn('--tools', invocations[0]['args'])
        self.assertIn('Read,Glob,Grep', invocations[0]['args'])
        self.assertIn('--resume', invocations[1]['args'])
        self.assertIn(sess.provider_session_id, invocations[1]['args'])

    async def test_claude_jsonl_single_event_larger_than_stream_default(self):
        os.environ['TF_BACKEND'] = 'claude'
        sess = self.store.create('claude', 'sonnet', self.repo)
        observed = []
        result = await ask(sess, self.store, 'large', observed.append, executable=str(self.fake))
        self.assertEqual(result.answer, 'I read the README.')
        self.assertEqual(observed, ['Read'])
        self.assertEqual(len(self.store.load(sess.id).turns), 1)

    async def test_large_jsonl_aggregate_fails_cleanly_without_persisting(self):
        os.environ['TF_BACKEND'] = 'claude'
        sess = self.store.create('claude', 'sonnet', self.repo)
        with patch('taskforge_chat.core.MAX_EVENTS_BYTES', 100_000):
            with self.assertRaisesRegex(ChatError, 'CLI event output exceeded'):
                await ask(sess, self.store, 'large', lambda _: None, executable=str(self.fake))
        self.assertEqual(self.store.load(sess.id).turns, [])
        self.assertIsNone(self.store.load(sess.id).provider_session_id)

    async def test_timeouts_do_not_persist_failed_turn(self):
        os.environ['TF_BACKEND'] = 'codex'
        sess = self.store.create('codex', 'm', self.repo)
        with self.assertRaisesRegex(ChatError, 'timeout'):
            await ask(sess, self.store, 'sleep', lambda _: None, timeout=1, executable=str(self.fake))
        self.assertIsNone(self.store.load(sess.id).provider_session_id)
        self.assertEqual(self.store.load(sess.id).turns, [])

    async def test_explicit_models_and_session_directory_validation(self):
        with self.assertRaises(ChatError):
            self.store.create('codex', '--bad', self.repo)
        with self.assertRaises(ChatError):
            self.store.load('../../passwd')
        s = self.store.create('codex', 'm', self.repo)
        self.assertEqual(self.store.list()[0].id, s.id)
        self.assertNotIn('resume', build_command('codex', 'm', None))
        self.assertIn('resume', build_command('codex', 'm', '12345678'))

    async def test_rejected_old_claude_cli(self):
        os.environ['TF_BACKEND'] = 'claude'
        self.fake.write_text('#!' + sys.executable + '\nimport sys\nprint("claude 2.1.100")\n')
        self.fake.chmod(0o700)
        with self.assertRaisesRegex(ChatError, '2.1.248'):
            await doctor('claude', str(self.fake))

    def test_codex_event_parser_deduplicates_tools(self):
        seen = []
        parser = EventParser('codex', seen.append)
        ev = {'type':'item.started','item':{'type':'command_execution','id':'x','command':'git status'}}
        parser.feed(ev)
        parser.feed(dict(ev, type='item.completed'))
        parser.feed({'type':'item.completed','item':{'type':'agent_message','text':'ok'}})
        parser.feed({'type':'thread.started','thread_id':'12345678'})
        result = parser.finish()
        self.assertEqual(seen, ['shell: git status'])
        self.assertEqual(result.answer, 'ok')

    def test_claude_result_error(self):
        parser = EventParser('claude', lambda _: None)
        parser.feed({'type':'result','is_error':True,'result':'quota exceeded'})
        with self.assertRaisesRegex(ChatError, 'quota exceeded'):
            parser.finish()


if __name__ == '__main__':
    unittest.main()