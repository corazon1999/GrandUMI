# -*- coding: utf-8 -*-

import asyncio
import sys
import unittest
from pathlib import Path
from unittest import mock


BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot


class FakeActionClient:
    def __init__(self, failures=0):
        self.actions = []
        self.failures = failures

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, params))
        if self.failures:
            self.failures -= 1
            raise RuntimeError("模拟发送失败")
        return {"status": "ok", "retcode": 0, "data": {"message_id": 1}}


class NewMemberWelcomeTests(unittest.TestCase):
    def setUp(self):
        bot._handled_new_member_welcomes.clear()

    def tearDown(self):
        bot._handled_new_member_welcomes.clear()

    @staticmethod
    def cfg(
        assistant="primary",
        name="s-蛇",
        self_id="3215228879",
        enabled=True,
        groups=None,
        role="primary",
    ):
        return {
            "_assistant_id": assistant,
            "_assistant_name": name,
            "_assistant_role": role,
            "_expected_self_id": self_id,
            "new_member_welcome_enabled": enabled,
            "new_member_welcome_groups": [297542853] if groups is None else groups,
        }

    @staticmethod
    def event(
        self_id="3215228879",
        group_id=297542853,
        user_id=123456789,
        event_time=1787971200,
        message_id=None,
    ):
        event = {
            "post_type": "notice",
            "notice_type": "group_increase",
            "sub_type": "approve",
            "group_id": group_id,
            "user_id": user_id,
            "self_id": self_id,
            "time": event_time,
        }
        if message_id is not None:
            event["message_id"] = message_id
        return event

    def test蛇和鲨各自发送结构化欢迎消息(self):
        cases = (
            ("primary", "s-蛇", "3215228879", "primary"),
            ("s-shark", "s-鲨", "184689168", "admin_only"),
        )
        for assistant, name, self_id, role in cases:
            with self.subTest(assistant=assistant):
                client = FakeActionClient()
                handled = asyncio.run(
                    bot.handle_new_member_welcome(
                        client,
                        self.cfg(assistant, name, self_id, role=role),
                        self.event(self_id=self_id),
                    )
                )
                self.assertTrue(handled)
                self.assertEqual(1, len(client.actions))
                action, params = client.actions[0]
                self.assertEqual("send_group_msg", action)
                self.assertEqual(297542853, params["group_id"])
                self.assertEqual(
                    [
                        {"type": "at", "data": {"qq": "123456789"}},
                        {
                            "type": "text",
                            "data": {"text": f" 欢迎加入本群！我是 {name}，请多关照。"},
                        },
                    ],
                    params["message"],
                )

    def test主助理欢迎成功后仍继续新人验证(self):
        client = FakeActionClient()
        verification = mock.AsyncMock(return_value=True)
        with mock.patch.object(bot, "handle_member_verification_notice", verification):
            asyncio.run(bot.on_event(client, self.cfg(), self.event()))
        self.assertEqual(["send_group_msg"], [action for action, _ in client.actions])
        verification.assert_awaited_once_with(client, self.cfg(), self.event())

    def test主助理欢迎失败也继续新人验证(self):
        client = FakeActionClient(failures=1)
        verification = mock.AsyncMock(return_value=True)
        with mock.patch.object(bot, "handle_member_verification_notice", verification):
            asyncio.run(bot.on_event(client, self.cfg(), self.event()))
        self.assertEqual(["send_group_msg"], [action for action, _ in client.actions])
        verification.assert_awaited_once_with(client, self.cfg(), self.event())

    def test机器人自身入群非目标群和关闭配置均不发送(self):
        cases = (
            (self.cfg(), self.event(user_id="3215228879")),
            (self.cfg(), self.event(group_id=987654321)),
            (self.cfg(enabled=False), self.event()),
            (self.cfg(groups=[]), self.event()),
            (
                self.cfg("s-eagle", "s-鹰", "3430685803", role="admin_only"),
                self.event(self_id="3430685803"),
            ),
        )
        for cfg, event in cases:
            with self.subTest(cfg=cfg, event=event):
                client = FakeActionClient()
                asyncio.run(bot.handle_new_member_welcome(client, cfg, event))
                self.assertEqual([], client.actions)

    def test同一通知重放只欢迎一次(self):
        client = FakeActionClient()
        event = self.event(message_id=9988)
        asyncio.run(bot.handle_new_member_welcome(client, self.cfg(), event))
        asyncio.run(bot.handle_new_member_welcome(client, self.cfg(), dict(event)))
        self.assertEqual(1, len(client.actions))

    def test发送失败后同一通知可重试(self):
        client = FakeActionClient(failures=1)
        event = self.event()
        asyncio.run(bot.handle_new_member_welcome(client, self.cfg(), event))
        asyncio.run(bot.handle_new_member_welcome(client, self.cfg(), dict(event)))
        self.assertEqual(2, len(client.actions))

    def test管理员副助理事件分发先处理欢迎通知(self):
        client = FakeActionClient()
        asyncio.run(
            bot.on_event(
                client,
                self.cfg("s-shark", "s-鲨", "184689168", role="admin_only"),
                self.event(self_id="184689168"),
            )
        )
        self.assertEqual(["send_group_msg"], [action for action, _ in client.actions])

    def test连接配置支持逐助理覆盖和顶层缺省(self):
        raw = {
            "ws_url": "ws://napcat:3001",
            "new_member_welcome_enabled": False,
            "new_member_welcome_groups": [],
            "assistant_connections": [
                {
                    "id": "primary",
                    "name": "s-蛇",
                    "role": "primary",
                    "ws_url": "ws://napcat:3001",
                    "new_member_welcome_enabled": True,
                    "new_member_welcome_groups": [297542853],
                },
                {
                    "id": "s-eagle",
                    "name": "s-鹰",
                    "role": "admin_only",
                    "ws_url": "ws://napcat-eagle:3001",
                    "expected_self_id": "3430685803",
                    "new_member_welcome_enabled": False,
                    "new_member_welcome_groups": [],
                },
                {
                    "id": "s-shark",
                    "name": "s-鲨",
                    "role": "admin_only",
                    "ws_url": "ws://napcat-shark:3001",
                    "expected_self_id": "184689168",
                    "new_member_welcome_enabled": True,
                    "new_member_welcome_groups": [297542853],
                },
            ],
        }
        resolved = bot.resolve_assistant_connections(raw)
        self.assertTrue(resolved[0]["new_member_welcome_enabled"])
        self.assertEqual([297542853], resolved[0]["new_member_welcome_groups"])
        self.assertFalse(resolved[1]["new_member_welcome_enabled"])
        self.assertEqual([], resolved[1]["new_member_welcome_groups"])
        self.assertTrue(resolved[2]["new_member_welcome_enabled"])
        self.assertEqual([297542853], resolved[2]["new_member_welcome_groups"])


if __name__ == "__main__":
    unittest.main()
