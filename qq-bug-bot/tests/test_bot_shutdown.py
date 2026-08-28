# -*- coding: utf-8 -*-

import asyncio
import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot


class _FailedConnection:
    async def __aenter__(self):
        raise OSError("测试连接失败")

    async def __aexit__(self, *_args):
        return False


class _BlockingWebSocket:
    def __init__(self):
        self.waiting = asyncio.Event()
        self.cancelled = False

    def __aiter__(self):
        return self

    async def __anext__(self):
        self.waiting.set()
        try:
            await asyncio.Future()
        except asyncio.CancelledError:
            self.cancelled = True
            raise


class _FiniteWebSocket:
    def __init__(self, events):
        self.events = [json.dumps(item, ensure_ascii=False) for item in events]

    def __aiter__(self):
        return self

    async def __anext__(self):
        if not self.events:
            raise StopAsyncIteration
        return self.events.pop(0)

    async def send(self, _payload):
        return None


class BotShutdownTests(unittest.IsolatedAsyncioTestCase):
    async def test_副助理账号核验前不会启动任何回执后台任务(self):
        cfg = {
            "_assistant_id": "s-eagle",
            "_assistant_name": "s-鹰",
            "_assistant_role": "admin_only",
            "_expected_self_id": "88888888",
            "admin_agent_enabled": True,
        }
        sync = SimpleNamespace(enabled=False)
        wrong = _FiniteWebSocket(
            [{"post_type": "meta_event", "self_id": 77777777}]
        )
        right = _FiniteWebSocket(
            [{"post_type": "meta_event", "self_id": 88888888}]
        )

        with mock.patch.object(bot, "notification_loop", new=mock.AsyncMock()) as notify:
            with self.assertRaisesRegex(RuntimeError, "expected_self_id"):
                await bot._run_connected_session(wrong, cfg, set(), sync)
            notify.assert_not_called()
            await bot._run_connected_session(right, cfg, set(), sync)
            notify.assert_called_once()

    async def test_停止事件会打断五秒重连等待(self):
        stop_event = asyncio.Event()
        attempted = asyncio.Event()

        def connect(*_args, **_kwargs):
            attempted.set()
            return _FailedConnection()

        config = {
            "ws_url": "ws://napcat:3001",
            "new_member_verification_enabled": False,
            "group_add_auto_approval_enabled": False,
            "qq_whitelist_sync_enabled": False,
        }
        with (
            mock.patch.object(bot, "load_config", return_value=config),
            mock.patch.object(bot.storage, "init_db"),
            mock.patch.object(
                bot.storage,
                "cancel_member_verifications_outside_groups",
                return_value=0,
            ),
            mock.patch.object(bot, "ws_connect", side_effect=connect),
        ):
            task = asyncio.create_task(bot.run(stop_event))
            await asyncio.wait_for(attempted.wait(), timeout=1)
            await asyncio.sleep(0)
            stop_event.set()
            await asyncio.wait_for(task, timeout=1)

    async def test_停止事件会取消正在等待消息的连接消费(self):
        ws = _BlockingWebSocket()
        stop_event = asyncio.Event()
        task = asyncio.create_task(
            bot._consume_until_stopped(
                ws,
                {},
                set(),
                SimpleNamespace(enabled=False),
                stop_event,
            )
        )
        await asyncio.wait_for(ws.waiting.wait(), timeout=1)
        stop_event.set()
        await asyncio.wait_for(task, timeout=1)
        self.assertTrue(ws.cancelled)


if __name__ == "__main__":
    unittest.main()
