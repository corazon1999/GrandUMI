# -*- coding: utf-8 -*-

import asyncio
from concurrent.futures import ThreadPoolExecutor
import os
import sqlite3
import sys
import tempfile
import time
import unittest
from unittest import mock


sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import abuse_moderation
import bot
import storage


GROUP_ID = 297542853
OFFENDER_QQ = 100000001
TARGET_QQ = 200000002
BOT_QQ = 3215228879


def moderation_cfg(enabled=True, groups=(GROUP_ID,), assistant="primary"):
    return {
        "_assistant_id": assistant,
        "_assistant_name": "s-蛇" if assistant == "primary" else "s-鹰",
        "_assistant_role": "primary" if assistant == "primary" else "admin_only",
        "_expected_self_id": str(BOT_QQ if assistant == "primary" else 3430685803),
        "abuse_moderation_enabled": enabled,
        "abuse_moderation_groups": list(groups),
        "abuse_moderation_exempt_qqs": [651846226],
        "allowed_groups": [GROUP_ID],
    }


def group_message(
    text="你就是个傻逼",
    message_id=101,
    offender=OFFENDER_QQ,
    role="member",
    self_id=BOT_QQ,
    segments=None,
):
    return {
        "post_type": "message",
        "message_type": "group",
        "group_id": GROUP_ID,
        "user_id": offender,
        "self_id": self_id,
        "message_id": message_id,
        "time": 1000,
        "sender": {"user_id": offender, "role": role, "nickname": "群友"},
        "message": segments
        if segments is not None
        else [{"type": "text", "data": {"text": text}}],
    }


class FakeOneBotClient:
    def __init__(self, role="member", mute_until=0, ban_outcomes=None):
        self.role = role
        self.mute_until = mute_until
        self.ban_outcomes = list(ban_outcomes or ["success"])
        self.actions = []

    @property
    def ban_calls(self):
        return sum(name == "set_group_ban" for name, _ in self.actions)

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, dict(params)))
        await asyncio.sleep(0)
        if action == "get_group_member_info":
            return {
                "status": "ok",
                "retcode": 0,
                "data": {
                    "group_id": params["group_id"],
                    "user_id": params["user_id"],
                    "role": self.role,
                    "shut_up_timestamp": self.mute_until,
                },
            }
        if action != "set_group_ban":
            raise AssertionError(f"不应调用动作：{action}")
        outcome = self.ban_outcomes.pop(0) if self.ban_outcomes else "success"
        if outcome == "timeout":
            raise TimeoutError("模拟动作响应超时")
        if outcome == "rejected":
            raise bot.OneBotActionRejected("模拟 OneBot 明确拒绝")
        if outcome == "cancelled":
            raise asyncio.CancelledError()
        if outcome == "error":
            raise OSError("模拟连接中断")
        return {"status": "ok", "retcode": 0, "data": None}


class BrokenMemberClient(FakeOneBotClient):
    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, dict(params)))
        if action == "get_group_member_info":
            raise TimeoutError("模拟成员查询超时")
        return await super().call_action(action, params, timeout)


class AbuseModerationTestCase(unittest.TestCase):
    def setUp(self):
        temp_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT") or None
        self.temp_dir = tempfile.TemporaryDirectory(
            dir=temp_root, ignore_cleanup_errors=True
        )
        self.old_db_path = storage.DB_PATH
        storage.DB_PATH = os.path.join(self.temp_dir.name, "moderation.db")
        storage.init_db()

    def tearDown(self):
        storage.DB_PATH = self.old_db_path
        self.temp_dir.cleanup()


class AbuseClassifierTests(unittest.TestCase):
    def test明确人身攻击家属诅咒死亡威胁与真实艾特均命中(self):
        samples = (
            (group_message("你就是个傻逼"), "direct_personal_insult"),
            (group_message("傻逼吧你"), "direct_personal_insult"),
            (group_message("操 你 妈"), "severe_family_abuse"),
            (group_message("你给我去死吧"), "direct_death_wish"),
            (
                group_message(
                    segments=[
                        {"type": "at", "data": {"qq": str(TARGET_QQ)}},
                        {"type": "text", "data": {"text": " 废物"}},
                    ]
                ),
                "at_personal_insult",
            ),
        )
        for event, expected in samples:
            with self.subTest(expected=expected):
                decision = abuse_moderation.classify_group_message(event)
                self.assertIsNotNone(decision)
                self.assertEqual(expected, decision.rule_id)
                self.assertRegex(decision.content_sha256, r"^[0-9a-f]{64}$")

    def test普通负面表达自嘲词汇讨论台词引语和劝阻均不处罚(self):
        safe_messages = (
            "这个功能做得真垃圾",
            "我真是个傻逼",
            "测试一下傻逼这个敏感词能否被过滤",
            "角色台词：你就是个傻逼",
            "他说“你就是个傻逼”",
            "你不是傻逼",
            "别骂别人傻逼",
            "这局打得太差了",
            "你妈没了工作，最近挺难受的",
            "你去死循环里检查一下",
            "你是废物利用方面的专家",
            "cnm模型是一种技术缩写",
            "你是沙比利这名角色的粉丝",
        )
        for text in safe_messages:
            with self.subTest(text=text):
                self.assertIsNone(
                    abuse_moderation.classify_group_message(group_message(text))
                )

    def test字符串CQ引用转发和图片内部文字均不被采信(self):
        events = (
            {
                **group_message(),
                "message": "[CQ:at,qq=200000002] 你就是个傻逼",
                "raw_message": "你就是个傻逼",
            },
            group_message(
                text="",
                segments=[
                    {
                        "type": "reply",
                        "data": {"text": "你就是个傻逼", "id": "88"},
                    },
                    {"type": "text", "data": {"text": "我不同意"}},
                ],
            ),
            group_message(
                text="",
                segments=[
                    {"type": "forward", "data": {"content": "操你妈"}},
                    {"type": "image", "data": {"text": "你就是个傻逼"}},
                ],
            ),
        )
        for event in events:
            self.assertIsNone(abuse_moderation.classify_group_message(event))


class AbuseModerationStorageTests(AbuseModerationTestCase):
    @staticmethod
    def reserve(event_key, offender=OFFENDER_QQ, message_id="101", now=100, mute=0):
        return storage.reserve_abuse_moderation_action(
            event_key=event_key,
            group_id=str(GROUP_ID),
            offender_qq=str(offender),
            source_message_id=message_id,
            rule_id="direct_personal_insult",
            content_sha256="a" * 64,
            member_role="member",
            observed_mute_until=mute,
            now=now,
        )

    def test并发预占同一消息只有一个调用资格(self):
        def reserve(_):
            return self.reserve("onebot-group:297542853:101")

        with ThreadPoolExecutor(max_workers=12) as pool:
            results = list(pool.map(reserve, range(24)))
        self.assertEqual(1, sum(bool(item["acquired"]) for item in results))
        self.assertEqual({"reserved"}, {item["state"] for item in results})

    def test重放永久去重且同成员确认或未知窗口阻止延长(self):
        first = self.reserve("onebot-group:297542853:101")
        duplicate = self.reserve("onebot-group:297542853:101")
        self.assertTrue(first["acquired"])
        self.assertFalse(duplicate["acquired"])
        self.assertEqual("duplicate_event", duplicate["reason"])
        self.assertTrue(
            storage.finish_abuse_moderation_action(
                first["event_key"], first["action_token"], "unknown", now=101
            )
        )

        queued = self.reserve(
            "onebot-group:297542853:102", message_id="102", now=102
        )
        self.assertFalse(queued["acquired"])
        self.assertEqual("suppressed", queued["state"])
        self.assertEqual("active_unknown", queued["reason"])

        after_barrier = self.reserve(
            "onebot-group:297542853:103", message_id="103", now=86501
        )
        self.assertTrue(after_barrier["acquired"])

    def test明确拒绝解除成员屏障但同消息仍不重试(self):
        first = self.reserve("onebot-group:297542853:201", message_id="201")
        self.assertTrue(
            storage.finish_abuse_moderation_action(
                first["event_key"], first["action_token"], "rejected", now=101
            )
        )
        replay = self.reserve("onebot-group:297542853:201", message_id="201", now=102)
        later = self.reserve("onebot-group:297542853:202", message_id="202", now=102)
        self.assertFalse(replay["acquired"])
        self.assertEqual("rejected", replay["state"])
        self.assertTrue(later["acquired"])

    def test实时观察到已禁言时记录终态且以后不处罚该旧消息(self):
        row = self.reserve(
            "onebot-group:297542853:301",
            message_id="301",
            now=100,
            mute=500,
        )
        self.assertFalse(row["acquired"])
        self.assertEqual("already_muted", row["state"])
        replay = self.reserve(
            "onebot-group:297542853:301",
            message_id="301",
            now=600,
        )
        self.assertFalse(replay["acquired"])
        self.assertEqual("duplicate_event", replay["reason"])

    def test审计表不保存辱骂原文(self):
        self.reserve("onebot-group:297542853:401", message_id="401")
        with sqlite3.connect(storage.DB_PATH) as conn:
            columns = {
                row[1] for row in conn.execute("PRAGMA table_info(abuse_moderation_actions)")
            }
            row = conn.execute(
                "SELECT content_sha256, rule_id FROM abuse_moderation_actions"
            ).fetchone()
        self.assertNotIn("content", columns)
        self.assertNotIn("raw_message", columns)
        self.assertEqual(("a" * 64, "direct_personal_insult"), row)


class AbuseModerationBotTests(AbuseModerationTestCase):
    def test命中后实时核验并只调用一次固定一天禁言(self):
        client = FakeOneBotClient()
        event = group_message()
        asyncio.run(bot.on_event(client, moderation_cfg(), event))

        self.assertEqual(
            ["get_group_member_info", "set_group_ban"],
            [name for name, _ in client.actions],
        )
        self.assertEqual(
            {
                "group_id": GROUP_ID,
                "user_id": OFFENDER_QQ,
                "duration": 86400,
            },
            client.actions[-1][1],
        )
        key, _ = bot.abuse_moderation_message_identity(event)
        row = storage.get_abuse_moderation_action(key)
        self.assertEqual("confirmed", row["state"])
        self.assertEqual("direct_personal_insult", row["rule_id"])

        action_count = len(client.actions)
        asyncio.run(bot.on_event(client, moderation_cfg(), event))
        self.assertEqual(action_count, len(client.actions))

    def test同消息并发和同成员后续排队消息均不会重复延长(self):
        client = FakeOneBotClient(ban_outcomes=["success"])
        first = group_message(message_id=501)

        async def concurrent_replay():
            await asyncio.gather(
                *(bot.handle_abuse_moderation(client, moderation_cfg(), first) for _ in range(8))
            )

        asyncio.run(concurrent_replay())
        self.assertEqual(1, client.ban_calls)

        second = group_message(text="操你妈", message_id=502)
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), second))
        self.assertEqual(1, client.ban_calls)
        second_key, _ = bot.abuse_moderation_message_identity(second)
        self.assertEqual(
            "suppressed", storage.get_abuse_moderation_action(second_key)["state"]
        )

    def test动作超时记未知且重放和后续消息都不再调用(self):
        client = FakeOneBotClient(ban_outcomes=["timeout", "success"])
        first = group_message(message_id=601)
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), first))
        first_key, _ = bot.abuse_moderation_message_identity(first)
        self.assertEqual("unknown", storage.get_abuse_moderation_action(first_key)["state"])

        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), first))
        second = group_message(text="你给我去死", message_id=602)
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), second))
        self.assertEqual(1, client.ban_calls)
        second_key, _ = bot.abuse_moderation_message_identity(second)
        self.assertEqual(
            "suppressed", storage.get_abuse_moderation_action(second_key)["state"]
        )

    def test明确拒绝不重试原消息但新消息可以再次尝试(self):
        client = FakeOneBotClient(ban_outcomes=["rejected", "success"])
        first = group_message(message_id=701)
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), first))
        first_key, _ = bot.abuse_moderation_message_identity(first)
        self.assertEqual("rejected", storage.get_abuse_moderation_action(first_key)["state"])
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), first))

        second = group_message(text="你就是个废物", message_id=702)
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), second))
        self.assertEqual(2, client.ban_calls)
        second_key, _ = bot.abuse_moderation_message_identity(second)
        self.assertEqual("confirmed", storage.get_abuse_moderation_action(second_key)["state"])

    def test取消和成功后落库失败都保留不重试屏障(self):
        cancelled_client = FakeOneBotClient(ban_outcomes=["cancelled"])
        cancelled = group_message(message_id=801)
        with self.assertRaises(asyncio.CancelledError):
            asyncio.run(
                bot.handle_abuse_moderation(
                    cancelled_client, moderation_cfg(), cancelled
                )
            )
        cancelled_key, _ = bot.abuse_moderation_message_identity(cancelled)
        self.assertEqual(
            "unknown", storage.get_abuse_moderation_action(cancelled_key)["state"]
        )

        # 换一个成员，避免上一个未知动作的一天成员级屏障影响本场景。
        other = group_message(message_id=802, offender=100000003)
        success_client = FakeOneBotClient(ban_outcomes=["success"])
        with mock.patch.object(
            storage,
            "finish_abuse_moderation_action",
            side_effect=sqlite3.OperationalError("模拟成功响应后的落库失败"),
        ):
            asyncio.run(
                bot.handle_abuse_moderation(success_client, moderation_cfg(), other)
            )
        other_key, _ = bot.abuse_moderation_message_identity(other)
        self.assertEqual("reserved", storage.get_abuse_moderation_action(other_key)["state"])
        asyncio.run(bot.handle_abuse_moderation(success_client, moderation_cfg(), other))
        self.assertEqual(1, success_client.ban_calls)

    def test固定豁免事件角色与实时管理员身份均不处罚(self):
        cases = (
            (group_message(offender=651846226), FakeOneBotClient(), 0),
            (group_message(offender=3430685803), FakeOneBotClient(), 0),
            (group_message(role="admin"), FakeOneBotClient(), 0),
            (group_message(message_id=904), FakeOneBotClient(role="owner"), 1),
        )
        for event, client, expected_queries in cases:
            with self.subTest(offender=event["user_id"], role=event["sender"]["role"]):
                asyncio.run(
                    bot.handle_abuse_moderation(client, moderation_cfg(), event)
                )
                self.assertEqual(0, client.ban_calls)
                self.assertEqual(expected_queries, len(client.actions))

    def test非目标群副助理错误账号缺消息号与成员查询失败均安全关闭(self):
        cases = [
            (moderation_cfg(enabled=False), group_message(), FakeOneBotClient()),
            (moderation_cfg(groups=(123456789,)), group_message(), FakeOneBotClient()),
            (moderation_cfg(assistant="s-eagle"), group_message(), FakeOneBotClient()),
            (moderation_cfg(), group_message(self_id=3430685803), FakeOneBotClient()),
            (moderation_cfg(), group_message(message_id=None), FakeOneBotClient()),
            (moderation_cfg(), group_message(message_id=1005), BrokenMemberClient()),
        ]
        for cfg, event, client in cases:
            with self.subTest(cfg=cfg, message_id=event.get("message_id")):
                asyncio.run(bot.handle_abuse_moderation(client, cfg, event))
                self.assertEqual(0, client.ban_calls)

    def test实时已禁言只记观察终态不重置截止时间(self):
        mute_until = int(time.time()) + 3600
        client = FakeOneBotClient(mute_until=mute_until)
        event = group_message(message_id=1101)
        asyncio.run(bot.handle_abuse_moderation(client, moderation_cfg(), event))
        self.assertEqual(0, client.ban_calls)
        key, _ = bot.abuse_moderation_message_identity(event)
        row = storage.get_abuse_moderation_action(key)
        self.assertEqual("already_muted", row["state"])
        self.assertEqual(mute_until, row["suppression_until"])

    def test启用治理时连接配置强制绑定官方主助理账号(self):
        config = {
            "abuse_moderation_enabled": True,
            "abuse_moderation_groups": [GROUP_ID],
            "abuse_moderation_exempt_qqs": [],
            "assistant_connections": [
                {
                    "id": "primary",
                    "name": "s-蛇",
                    "role": "primary",
                    "enabled": True,
                    "ws_url": "ws://127.0.0.1:3001",
                    "expected_self_id": "999999999",
                }
            ],
        }
        with self.assertRaisesRegex(ValueError, "3215228879"):
            bot.resolve_assistant_connections(config)
        config["assistant_connections"][0]["expected_self_id"] = str(BOT_QQ)
        resolved = bot.resolve_assistant_connections(config)
        self.assertEqual(str(BOT_QQ), resolved[0]["_expected_self_id"])

        legacy = {
            "ws_url": "ws://127.0.0.1:3001",
            "access_token": "",
            "abuse_moderation_enabled": True,
            "abuse_moderation_groups": [GROUP_ID],
            "abuse_moderation_exempt_qqs": [],
        }
        legacy_resolved = bot.resolve_assistant_connections(legacy)
        self.assertEqual(str(BOT_QQ), legacy_resolved[0]["_expected_self_id"])


if __name__ == "__main__":
    unittest.main()
