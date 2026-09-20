# -*- coding: utf-8 -*-

import asyncio
import contextlib
from concurrent.futures import ThreadPoolExecutor
import io
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import unittest
from unittest import mock


sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bot
import import_activation_codes
import storage


GROUP_1 = 1039789967
GROUP_2 = 1104489180
BOT_QQ = 3215228879
USER_QQ = 100000001


def fake_code(number: int) -> str:
    return f"CODEX-{number:05d}-ABCDE"


def claim_config(enabled=True, assistant="primary", expected_self_id=BOT_QQ):
    return {
        "_assistant_id": assistant,
        "_assistant_name": "s-蛇" if assistant == "primary" else "s-鲨",
        "_assistant_role": "primary" if assistant == "primary" else "admin_only",
        "_expected_self_id": str(expected_self_id),
        "activation_code_claim_enabled": enabled,
        "allowed_groups": [],
    }


def group_message(
    text="领码",
    message_id=101,
    group_id=GROUP_1,
    user_id=USER_QQ,
    self_id=BOT_QQ,
    segments=None,
):
    return {
        "post_type": "message",
        "message_type": "group",
        "sub_type": "normal",
        "group_id": group_id,
        "user_id": user_id,
        "self_id": self_id,
        "message_id": message_id,
        "raw_message": text,
        "sender": {"user_id": user_id, "role": "member", "nickname": "群友"},
        "message": segments
        if segments is not None
        else [{"type": "text", "data": {"text": text}}],
    }


class FakeOneBotClient:
    def __init__(self, outcomes=None, member_outcome="human"):
        self.outcomes = list(outcomes or ["success"])
        self.member_outcome = member_outcome
        self.actions = []
        self.next_message_id = 9000

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, params))
        await asyncio.sleep(0)
        if action == "get_group_member_info":
            if self.member_outcome == "timeout":
                raise TimeoutError("模拟群成员查询超时")
            data = {
                "group_id": params["group_id"],
                "user_id": params["user_id"],
                "role": "member",
                "is_robot": self.member_outcome == "robot",
            }
            if self.member_outcome == "missing_flag":
                data.pop("is_robot")
            elif self.member_outcome == "wrong_group":
                data["group_id"] += 1
            return {"status": "ok", "retcode": 0, "data": data}
        if action != "send_group_msg":
            raise AssertionError(f"不应调用动作：{action}")
        outcome = self.outcomes.pop(0) if self.outcomes else "success"
        if outcome == "leaky_rejected":
            raise bot.OneBotActionRejected(
                "模拟拒绝并回显参数：" + json.dumps(params, ensure_ascii=False)
            )
        if outcome == "rejected":
            raise bot.OneBotActionRejected("模拟拒绝，响应中可能回显敏感正文")
        if outcome == "timeout":
            raise TimeoutError("模拟响应超时")
        if outcome == "cancelled":
            raise asyncio.CancelledError()
        if outcome == "missing_id":
            return {"status": "ok", "retcode": 0, "data": {}}
        self.next_message_id += 1
        return {
            "status": "ok",
            "retcode": 0,
            "data": {"message_id": self.next_message_id},
        }

    @property
    def sent_actions(self):
        return [item for item in self.actions if item[0] == "send_group_msg"]

    @property
    def member_actions(self):
        return [item for item in self.actions if item[0] == "get_group_member_info"]


class ActivationCodeTestCase(unittest.TestCase):
    def setUp(self):
        temp_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT") or None
        self.temp_dir = tempfile.TemporaryDirectory(
            dir=temp_root, ignore_cleanup_errors=True
        )
        self.old_activation_db_path = storage.ACTIVATION_CODE_DB_PATH
        storage.ACTIVATION_CODE_DB_PATH = os.path.join(
            self.temp_dir.name, "activation_codes.db"
        )
        storage.init_activation_code_db()

    def tearDown(self):
        storage.ACTIVATION_CODE_DB_PATH = self.old_activation_db_path
        self.temp_dir.cleanup()

    def seed(self, count=3):
        codes = [fake_code(index) for index in range(1, count + 1)]
        digest = storage.activation_code_digest(codes)
        result = storage.import_activation_codes(codes, digest, now=100)
        self.assertEqual(count, result["input_count"])
        return codes

    @staticmethod
    def event_key(event):
        return bot.activation_code_claim_message_identity(event)[0]


class ActivationCodeRoutingTests(ActivationCodeTestCase):
    def test只在两个固定群由主助理处理纯文本命令(self):
        self.seed(4)
        accepted = FakeOneBotClient()
        asyncio.run(
            bot.handle_activation_code_claim(
                accepted,
                claim_config(),
                group_message(text="\u3000领码\u3000", message_id=1),
            )
        )
        asyncio.run(
            bot.handle_activation_code_claim(
                accepted,
                claim_config(),
                group_message(message_id=2, group_id=GROUP_2),
            )
        )
        self.assertEqual(2, len(accepted.sent_actions))
        self.assertEqual(
            {GROUP_1, GROUP_2},
            {item[1]["group_id"] for item in accepted.sent_actions},
        )

        cases = (
            (claim_config(enabled=False), group_message(message_id=2)),
            (claim_config(), group_message(message_id=3, group_id=999999999)),
            (
                claim_config(assistant="s-shark", expected_self_id=184689168),
                group_message(message_id=4, self_id=184689168),
            ),
            (claim_config(), group_message(message_id=5, user_id=BOT_QQ)),
            (claim_config(), group_message(message_id=6, user_id=3430685803)),
            (claim_config(), group_message(message_id=None)),
        )
        for cfg, event in cases:
            with self.subTest(cfg=cfg, event=event):
                client = FakeOneBotClient()
                asyncio.run(bot.handle_activation_code_claim(client, cfg, event))
                self.assertEqual([], client.actions)

        flagged_bot = group_message(message_id=8)
        flagged_bot["sender"]["is_robot"] = True
        client = FakeOneBotClient()
        asyncio.run(
            bot.handle_activation_code_claim(client, claim_config(), flagged_bot)
        )
        self.assertEqual([], client.actions)

        private = group_message(message_id=7)
        private["message_type"] = "private"
        private.pop("group_id")
        client = FakeOneBotClient()
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), private))
        self.assertEqual([], client.actions)

    def test实时成员核验拒绝机器人与不可信响应且不占用库存(self):
        self.seed(2)
        for index, outcome in enumerate(
            ("robot", "timeout", "missing_flag", "wrong_group"), start=1
        ):
            with self.subTest(outcome=outcome):
                event = group_message(message_id=f"member-check-{index}")
                client = FakeOneBotClient(member_outcome=outcome)
                asyncio.run(
                    bot.handle_activation_code_claim(client, claim_config(), event)
                )
                self.assertEqual(1, len(client.member_actions))
                self.assertEqual([], client.sent_actions)
        summary = storage.get_activation_code_inventory_summary()
        self.assertEqual(0, summary["allocated_count"])
        self.assertEqual(2, summary["available_count"])

    def test子串引用转发附件字符串CQ与其他顶层段均不触发(self):
        self.seed(2)
        invalid_events = (
            group_message("请领码", message_id=10),
            group_message("领码谢谢", message_id=11),
            group_message(
                "领码",
                message_id=12,
                segments=[
                    {"type": "reply", "data": {"id": "1"}},
                    {"type": "text", "data": {"text": "领码"}},
                ],
            ),
            group_message(
                "领码",
                message_id=13,
                segments=[
                    {"type": "forward", "data": {"content": "领码"}},
                    {"type": "text", "data": {"text": "领码"}},
                ],
            ),
            group_message(
                "领码",
                message_id=14,
                segments=[
                    {"type": "image", "data": {"text": "领码"}},
                    {"type": "text", "data": {"text": "领码"}},
                ],
            ),
            group_message(
                "领码",
                message_id=15,
                segments=[
                    {"type": "at", "data": {"qq": str(BOT_QQ)}},
                    {"type": "text", "data": {"text": "领码"}},
                ],
            ),
        )
        cq = group_message("领码", message_id=16)
        cq["raw_message"] = "[CQ:reply,id=1]领码"
        invalid_events += (cq,)
        string_message = group_message("领码", message_id=17)
        string_message["message"] = "领码"
        invalid_events += (string_message,)

        for event in invalid_events:
            with self.subTest(message_id=event["message_id"]):
                client = FakeOneBotClient()
                asyncio.run(
                    bot.handle_activation_code_claim(client, claim_config(), event)
                )
                self.assertEqual([], client.actions)
        self.assertEqual(0, storage.get_activation_code_inventory_summary()["allocated_count"])

    def test启用配置强制绑定固定主助理身份(self):
        config = {
            "activation_code_claim_enabled": True,
            "abuse_moderation_enabled": False,
            "abuse_moderation_groups": [],
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
        self.assertEqual(
            str(BOT_QQ),
            bot.resolve_assistant_connections(config)[0]["_expected_self_id"],
        )


class ActivationCodeConcurrencyTests(ActivationCodeTestCase):
    def reserve(self, message_id, group_id=GROUP_1, requester=USER_QQ, process="p1"):
        event_key = f"onebot-activation:{BOT_QQ}:{group_id}:{message_id}"
        return storage.reserve_activation_code_claim(
            event_key=event_key,
            self_id=str(BOT_QQ),
            group_id=str(group_id),
            requester_qq=str(requester),
            source_message_id=str(message_id),
            sender_process_id=process,
            now=100,
        )

    def test同一消息并发重放只分配一个码(self):
        self.seed(8)
        with ThreadPoolExecutor(max_workers=12) as pool:
            results = list(pool.map(lambda _: self.reserve("same-1"), range(24)))
        self.assertEqual(1, sum(bool(item["acquired"]) for item in results))
        self.assertEqual(1, storage.get_activation_code_inventory_summary()["allocated_count"])

    def test同群与跨群并发请求得到不同码(self):
        self.seed(6)
        requests = (
            ("g1-a", GROUP_1, USER_QQ),
            ("g1-b", GROUP_1, USER_QQ + 1),
            ("g2-a", GROUP_2, USER_QQ),
            ("g2-b", GROUP_2, USER_QQ + 1),
        )
        with ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(lambda args: self.reserve(*args), requests))
        self.assertTrue(all(item["acquired"] for item in results))
        self.assertEqual(4, len({item["code"] for item in results}))
        self.assertEqual(4, storage.get_activation_code_inventory_summary()["allocated_count"])

    def test进程重启把未决发送隔离且绝不回库(self):
        self.seed(2)
        reserved = self.reserve("crash-1", process="old-process")
        self.assertTrue(reserved["acquired"])
        self.assertEqual(1, storage.recover_activation_code_claims("new-process", now=200))
        row = storage.get_activation_code_claim(reserved["event_key"])
        self.assertEqual("unknown", row["state"])
        replay = self.reserve("crash-1", process="new-process")
        self.assertFalse(replay["acquired"])
        summary = storage.get_activation_code_inventory_summary()
        self.assertEqual(1, summary["allocated_count"])
        self.assertEqual(1, summary["available_count"])


class ActivationCodeDeliveryTests(ActivationCodeTestCase):
    def test发送成功记录领取人来源消息与OneBot返回消息号(self):
        codes = self.seed(2)
        event = group_message(message_id="ok-1")
        client = FakeOneBotClient()
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
        row = storage.get_activation_code_claim(self.event_key(event))
        self.assertEqual("confirmed", row["state"])
        self.assertEqual(str(USER_QQ), row["requester_qq"])
        self.assertEqual("ok-1", row["source_message_id"])
        self.assertEqual("9001", row["onebot_message_id"])
        message = client.sent_actions[0][1]["message"]
        self.assertEqual("at", message[0]["type"])
        self.assertEqual(str(USER_QQ), message[0]["data"]["qq"])
        self.assertEqual(1, sum(code in message[1]["data"]["text"] for code in codes))

    def test同一用户新消息可以再次领取且旧消息重放不重复发送(self):
        self.seed(3)
        client = FakeOneBotClient(outcomes=["success", "success"])
        first = group_message(message_id="again-1")
        second = group_message(message_id="again-2")
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), first))
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), first))
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), second))
        self.assertEqual(2, len(client.sent_actions))
        first_text = client.sent_actions[0][1]["message"][1]["data"]["text"]
        second_text = client.sent_actions[1][1]["message"][1]["data"]["text"]
        self.assertNotEqual(first_text, second_text)
        self.assertEqual(2, storage.get_activation_code_inventory_summary()["allocated_count"])

    def test明确拒绝永久隔离本次码但新消息可以领取下一个(self):
        self.seed(3)
        client = FakeOneBotClient(outcomes=["rejected", "success"])
        first = group_message(message_id="reject-1")
        second = group_message(message_id="reject-2")
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), first))
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), first))
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), second))
        self.assertEqual("rejected", storage.get_activation_code_claim(self.event_key(first))["state"])
        self.assertEqual("confirmed", storage.get_activation_code_claim(self.event_key(second))["state"])
        self.assertEqual(2, len(client.sent_actions))
        self.assertEqual(2, storage.get_activation_code_inventory_summary()["allocated_count"])

    def test超时缺消息号与取消均记未知并且不重发(self):
        self.seed(4)
        for index, outcome in enumerate(("timeout", "missing_id"), start=1):
            with self.subTest(outcome=outcome):
                event = group_message(message_id=f"unknown-{index}")
                client = FakeOneBotClient(outcomes=[outcome])
                asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
                self.assertEqual(
                    "unknown",
                    storage.get_activation_code_claim(self.event_key(event))["state"],
                )
                asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
                self.assertEqual(1, len(client.sent_actions))

        cancelled = group_message(message_id="unknown-cancelled")
        client = FakeOneBotClient(outcomes=["cancelled"])
        with self.assertRaises(asyncio.CancelledError):
            asyncio.run(
                bot.handle_activation_code_claim(client, claim_config(), cancelled)
            )
        self.assertEqual(
            "unknown",
            storage.get_activation_code_claim(self.event_key(cancelled))["state"],
        )

    def test发送成功后落库失败仍由预占屏障阻止重发(self):
        self.seed(2)
        event = group_message(message_id="persist-fail")
        client = FakeOneBotClient()
        with mock.patch.object(
            storage,
            "finish_activation_code_claim",
            side_effect=sqlite3.OperationalError("模拟落库失败"),
        ):
            asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
        self.assertEqual("reserved", storage.get_activation_code_claim(self.event_key(event))["state"])
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
        self.assertEqual(1, len(client.sent_actions))
        storage.recover_activation_code_claims("replacement-process", now=500)
        self.assertEqual("unknown", storage.get_activation_code_claim(self.event_key(event))["state"])

    def test库存耗尽只发送清晰提示且消息重放不刷屏(self):
        event = group_message(message_id="empty-1")
        client = FakeOneBotClient()
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
        asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
        self.assertEqual(1, len(client.sent_actions))
        text = client.sent_actions[0][1]["message"][1]["data"]["text"]
        self.assertIn("库存暂时已领完", text)
        self.assertNotRegex(text, r"\d")
        self.assertEqual(
            "exhausted_confirmed",
            storage.get_activation_code_claim(self.event_key(event))["state"],
        )

    def test真实码样式内容不会进入日志错误或审计错误字段(self):
        marker = "LEAKX-99999-LEAKY"
        digest = storage.activation_code_digest([marker])
        storage.import_activation_codes([marker], digest)
        event = group_message(message_id="no-log")
        client = FakeOneBotClient(outcomes=["leaky_rejected"])
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            asyncio.run(bot.handle_activation_code_claim(client, claim_config(), event))
        row = storage.get_activation_code_claim(self.event_key(event))
        self.assertNotIn(marker, output.getvalue())
        self.assertNotIn(marker, str(row.get("last_error") or ""))


class ActivationCodeImportTests(ActivationCodeTestCase):
    def test导入解析校验数量摘要并保持幂等和已领取状态(self):
        codes = [fake_code(1), fake_code(2)]
        payload = ("\r\n".join(codes) + "\r\n").encode("utf-8")
        parsed, digest = import_activation_codes.parse_activation_code_bytes(payload)
        self.assertEqual(codes, parsed)
        first = storage.import_activation_codes(parsed, digest, now=100)
        self.assertEqual(2, first["inserted_count"])
        reservation = storage.reserve_activation_code_claim(
            event_key=f"onebot-activation:{BOT_QQ}:{GROUP_1}:import-claim",
            self_id=str(BOT_QQ),
            group_id=str(GROUP_1),
            requester_qq=str(USER_QQ),
            source_message_id="import-claim",
            sender_process_id="import-test",
            now=101,
        )
        self.assertTrue(reservation["acquired"])
        second = storage.import_activation_codes(parsed, digest, now=102)
        self.assertEqual(0, second["inserted_count"])
        self.assertEqual(2, second["existing_count"])
        summary = storage.get_activation_code_inventory_summary()
        self.assertEqual(1, summary["allocated_count"])
        self.assertEqual(1, summary["available_count"])
        self.assertEqual(digest, summary["inventory_sha256"])

    def test批内重复格式错误摘要错误均原子拒绝且不回显码(self):
        marker = "LEAKX-99999-LEAKY"
        invalid_payloads = (
            f"{marker}\n{marker}\n".encode(),
            f" {marker}\n".encode(),
            "中文不是激活码\n".encode("utf-8"),
        )
        for payload in invalid_payloads:
            with self.subTest(payload_length=len(payload)):
                with self.assertRaises(import_activation_codes.ActivationCodeInputError) as caught:
                    import_activation_codes.parse_activation_code_bytes(payload)
                self.assertNotIn(marker, str(caught.exception))
        self.assertEqual(0, storage.get_activation_code_inventory_summary()["total_count"])

        code = fake_code(1)
        with self.assertRaisesRegex(ValueError, "摘要不匹配"):
            storage.import_activation_codes([code], "0" * 64)
        self.assertEqual(0, storage.get_activation_code_inventory_summary()["total_count"])

    def test导入命令生成前后备份且标准输出不含任何码(self):
        codes = [fake_code(1), fake_code(2)]
        payload = ("\n".join(codes) + "\n").encode()
        digest = storage.activation_code_digest(codes)
        backup_dir = os.path.join(self.temp_dir.name, "backups")

        class FakeStdin:
            def __init__(self, value):
                self.buffer = io.BytesIO(value)

        stdout = io.StringIO()
        stderr = io.StringIO()
        with (
            mock.patch.object(sys, "stdin", FakeStdin(payload)),
            contextlib.redirect_stdout(stdout),
            contextlib.redirect_stderr(stderr),
        ):
            result = import_activation_codes.main(
                [
                    "--stdin",
                    "--expected-count",
                    "2",
                    "--expected-sha256",
                    digest,
                    "--backup-dir",
                    backup_dir,
                ]
            )
        self.assertEqual(0, result)
        self.assertEqual("", stderr.getvalue())
        output = stdout.getvalue()
        self.assertTrue(all(code not in output for code in codes))
        data = json.loads(output)
        self.assertEqual(2, data["total_count"])
        self.assertEqual(digest, data["sha256"])
        backups = list(Path(backup_dir).glob("*.db"))
        self.assertEqual(2, len(backups))
        for backup in backups:
            with sqlite3.connect(backup) as conn:
                self.assertEqual("ok", conn.execute("PRAGMA quick_check").fetchone()[0])

    def test旧反馈库初始化与独立激活码库互不覆盖(self):
        old_feedback_path = storage.DB_PATH
        legacy_path = os.path.join(self.temp_dir.name, "legacy-feedback.db")
        storage.DB_PATH = legacy_path
        try:
            with sqlite3.connect(legacy_path) as conn:
                conn.execute(
                    """
                    CREATE TABLE feedback(
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        qq TEXT NOT NULL,
                        nickname TEXT,
                        group_id TEXT NOT NULL,
                        content TEXT NOT NULL,
                        issue_no INTEGER,
                        created_at TEXT NOT NULL
                    )
                    """
                )
                conn.execute(
                    "INSERT INTO feedback(qq, group_id, content, created_at) VALUES('1','2','旧记录','2026-01-01')"
                )
                conn.commit()
            storage.init_db()
            storage.init_activation_code_db()
            with sqlite3.connect(legacy_path) as conn:
                self.assertEqual(1, conn.execute("SELECT COUNT(*) FROM feedback").fetchone()[0])
                self.assertNotIn(
                    "activation_code_inventory",
                    {row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")},
                )
            with sqlite3.connect(storage.ACTIVATION_CODE_DB_PATH) as conn:
                self.assertIn(
                    "activation_code_inventory",
                    {row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")},
                )
        finally:
            storage.DB_PATH = old_feedback_path


if __name__ == "__main__":
    unittest.main()
