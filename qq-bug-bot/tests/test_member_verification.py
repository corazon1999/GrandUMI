# -*- coding: utf-8 -*-

import asyncio
import json
import os
import sqlite3
import sys
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot
import storage


class FakeOneBotClient:
    def __init__(self, members=None):
        self.members = {str(value) for value in (members or [])}
        self.actions = []
        self.member_error = None
        self.send_error = None

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, params))
        if action == "send_group_msg":
            if self.send_error:
                raise self.send_error
            return {"status": "ok", "retcode": 0, "data": {"message_id": 1}}
        if action == "get_group_member_list":
            if self.member_error:
                raise self.member_error
            group_id = params["group_id"]
            return {
                "status": "ok",
                "retcode": 0,
                "data": [
                    {"group_id": group_id, "user_id": int(qq)}
                    for qq in sorted(self.members)
                ],
            }
        raise AssertionError(f"未预期的 OneBot 动作：{action}")


def verification_cfg(groups=(456,), reminder=1800, poll_interval=300):
    return {
        "new_member_verification_enabled": True,
        "new_member_verification_groups": list(groups),
        "new_member_verification_timeout_seconds": reminder,
        "new_member_verification_poll_interval_seconds": poll_interval,
        "allowed_groups": list(groups),
        "chat_agent_enabled": False,
        "admin_agent_enabled": False,
    }


def join_event(
    group_id=456,
    newcomer=10001,
    when=1000,
    self_id=99999,
    sub_type="approve",
    operator=20002,
):
    return {
        "post_type": "notice",
        "notice_type": "group_increase",
        "sub_type": sub_type,
        "group_id": group_id,
        "user_id": newcomer,
        "operator_id": operator,
        "self_id": self_id,
        "time": when,
        "_grandumi_received_at": when,
    }


def reply_event(
    candidate="20002",
    group_id=456,
    newcomer=10001,
    when=1100,
    message_id=88,
    self_id=99999,
):
    return {
        "post_type": "message",
        "message_type": "group",
        "group_id": group_id,
        "user_id": newcomer,
        "self_id": self_id,
        "time": when,
        "message_id": message_id,
        "_grandumi_received_at": when,
        "message": [
            {"type": "at", "data": {"qq": str(self_id)}},
            {"type": "text", "data": {"text": f" 邀请人是 {candidate}"}},
        ],
    }


class MemberVerificationTestCase(unittest.TestCase):
    def setUp(self):
        temp_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT") or None
        self.temp = tempfile.TemporaryDirectory(
            dir=temp_root, ignore_cleanup_errors=True
        )
        self.old_path = storage.DB_PATH
        storage.DB_PATH = os.path.join(self.temp.name, "feedback.db")
        storage.init_db()

    def tearDown(self):
        storage.DB_PATH = self.old_path
        self.temp.cleanup()

    @staticmethod
    def create_prompted_session(now=1000, reminder=600, group_id="456"):
        row = storage.start_member_verification(
            group_id, "10001", "新人", now, now=now
        )
        prompt = storage.claim_member_verification_prompt(row["id"], now=now)
        assert storage.complete_member_verification_prompt(
            row["id"], prompt["claim_token"], reminder, sent_at=now
        )
        return storage.get_member_verification(row["id"])


class MemberVerificationStorageTests(MemberVerificationTestCase):
    def test完成登记后可按群成员和消息键识别回放(self):
        row = self.create_prompted_session()
        answer = storage.begin_member_inviter_check(
            "456",
            "10001",
            "20002",
            "onebot:completed-reply",
            1100,
            received_at=1100,
        )
        self.assertEqual("claimed", answer["status"])
        self.assertTrue(
            storage.complete_member_inviter_check(
                row["id"],
                answer["verification"]["claim_token"],
                "20002",
                now=1101,
            )
        )
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        self.assertTrue(
            storage.has_member_verification_response(
                "456", "10001", "onebot:completed-reply"
            )
        )
        self.assertFalse(
            storage.has_member_verification_response(
                "456", "10001", "onebot:different-reply"
            )
        )
        self.assertFalse(
            storage.has_member_verification_response(
                "789", "10001", "onebot:completed-reply"
            )
        )
        self.assertFalse(
            storage.has_member_verification_response(
                "456", "10002", "onebot:completed-reply"
            )
        )

    def test审批预授权只有明确成功后才能由真实群消息恢复(self):
        prepared = storage.prepare_member_verification_approval(
            "456", "10001", "20002", 100, 600, now=100
        )
        self.assertEqual("approval_pending", prepared["state"])
        self.assertEqual("20002", prepared["inviter_qq"])
        self.assertIsNone(storage.claim_member_verification_prompt(now=100))
        self.assertIsNone(
            storage.activate_member_verification_from_reply(
                "456", "10001", 600, event_time=101, now=101
            )
        )

        self.assertTrue(
            storage.record_member_verification_approval_failure(
                prepared["id"], "审批动作失败", now=102
            )
        )
        self.assertIsNone(
            storage.activate_member_verification_from_reply(
                "456", "10001", 600, event_time=103, now=103
            )
        )
        self.assertTrue(
            storage.complete_member_verification_approval(prepared["id"], now=104)
        )
        activated = storage.activate_member_verification_from_reply(
            "456", "10001", 600, event_time=105, now=105
        )
        self.assertEqual("pending", activated["state"])
        self.assertEqual("20002", activated["inviter_qq"])
        self.assertEqual(105, activated["join_event_time"])
        self.assertEqual(705, activated["deadline_at"])

        expired = storage.prepare_member_verification_approval(
            "456", "10002", "20002", 200, 60, now=200
        )
        self.assertTrue(
            storage.complete_member_verification_approval(expired["id"], now=201)
        )
        self.assertIsNone(
            storage.activate_member_verification_from_reply(
                "456", "10002", 600, event_time=261, now=261
            )
        )
        self.assertEqual(
            "cancelled", storage.get_member_verification(expired["id"])["state"]
        )

    def test审批授权并发幂等且并发入群只完成一次(self):
        def prepare():
            return storage.prepare_member_verification_approval(
                "456", "10001", "20002", 100, 600, now=100
            )

        with ThreadPoolExecutor(max_workers=8) as pool:
            prepared = list(pool.map(lambda _: prepare(), range(8)))
        self.assertEqual(1, sum(bool(row["created"]) for row in prepared))
        self.assertEqual(1, len({row["id"] for row in prepared}))

        row = prepared[0]
        self.assertTrue(
            storage.complete_member_verification_approval(row["id"], now=101)
        )

        def join(_):
            return storage.start_member_verification(
                "456", "10001", "新人", 102, now=102
            )

        with ThreadPoolExecutor(max_workers=8) as pool:
            joined = list(pool.map(join, range(8)))
        self.assertEqual(1, sum(bool(item["created"]) for item in joined))
        self.assertEqual(1, len({item["id"] for item in joined}))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        self.assertIsNone(storage.claim_member_verification_prompt(row["id"], now=103))
        self.assertEqual([], storage.get_member_verification_responses(row["id"]))

    def test审批响应状态未落稳时真实入群仍可前向恢复(self):
        prepared = storage.prepare_member_verification_approval(
            "456", "10001", "20002", 100, 600, now=100
        )
        self.assertTrue(
            storage.record_member_verification_approval_failure(
                prepared["id"], "审批响应中断", now=101
            )
        )

        joined = storage.start_member_verification(
            "456", "10001", "新人", 102, now=102
        )
        self.assertTrue(joined["created"])
        self.assertEqual("approved_join_verified", joined["reason"])
        self.assertEqual("verified", joined["state"])
        self.assertEqual("20002", joined["inviter_qq"])
        self.assertFalse(
            storage.complete_member_verification_approval(prepared["id"], now=103)
        )

        replay = storage.start_member_verification(
            "456", "10001", "新人", 102, now=104
        )
        self.assertFalse(replay["created"])
        self.assertEqual(prepared["id"], replay["id"])
        self.assertEqual("verified", replay["state"])

    def test并发回答仍只能领取一次(self):
        row = self.create_prompted_session()

        def answer(index):
            return storage.begin_member_inviter_check(
                "456",
                "10001",
                "20002",
                f"onebot:{index}",
                110 + index,
                received_at=110 + index,
            )

        with ThreadPoolExecutor(max_workers=8) as pool:
            answers = list(pool.map(answer, range(8)))
        claimed = [result for result in answers if result["status"] == "claimed"]
        self.assertEqual(1, len(claimed))
        self.assertTrue(
            all(result["status"] in ("claimed", "busy") for result in answers)
        )
        self.assertEqual(1, len(storage.get_member_verification_responses(row["id"])))

    def test重复通知不重置倒计时_离群重进创建新会话(self):
        first = storage.start_member_verification("456", "10001", "新人", 100, now=100)
        prompt = storage.claim_member_verification_prompt(first["id"], now=100)
        storage.complete_member_verification_prompt(
            first["id"], prompt["claim_token"], 600, sent_at=110
        )

        duplicate = storage.start_member_verification(
            "456", "10001", "新人", 100, now=300
        )
        self.assertFalse(duplicate["created"])
        self.assertEqual(first["id"], duplicate["id"])
        self.assertEqual(710, storage.get_member_verification(first["id"])["deadline_at"])

        self.assertTrue(storage.mark_member_verification_left("456", "10001", now=400))
        replay = storage.start_member_verification(
            "456", "10001", "新人", 100, now=401
        )
        self.assertEqual("replayed_notice", replay["reason"])
        second = storage.start_member_verification(
            "456", "10001", "新人", 500, now=500
        )
        self.assertTrue(second["created"])
        self.assertNotEqual(first["id"], second["id"])

    def test提示确认成功后才开始计时_失败租约可恢复(self):
        row = storage.start_member_verification("456", "10001", "", 100, now=100)
        claimed = storage.claim_member_verification_prompt(row["id"], now=100)
        self.assertIsNone(storage.get_member_verification(row["id"])["deadline_at"])
        self.assertTrue(
            storage.release_member_verification_claim(
                row["id"], claimed["claim_token"], "prompt", "发送失败", now=101
            )
        )
        retried = storage.claim_member_verification_prompt(row["id"], now=106)
        self.assertTrue(
            storage.complete_member_verification_prompt(
                row["id"], retried["claim_token"], 600, sent_at=106
            )
        )
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("pending", completed["state"])
        self.assertEqual(706, completed["deadline_at"])
        self.assertEqual(2, completed["prompt_attempts"])

    def test无效邀请人可继续回答_重复消息不重复核查(self):
        row = self.create_prompted_session()
        first = storage.begin_member_inviter_check(
            "456", "10001", "30003", "onebot:1", 1100, received_at=1100
        )
        self.assertEqual("claimed", first["status"])
        rejected = storage.reject_member_inviter_check(
            row["id"], first["verification"]["claim_token"], "不在群", now=1101
        )
        self.assertTrue(rejected["can_retry"])
        self.assertEqual("pending", rejected["state"])

        duplicate = storage.begin_member_inviter_check(
            "456", "10001", "30003", "onebot:1", 1100, received_at=1102
        )
        self.assertEqual("duplicate", duplicate["status"])
        second = storage.begin_member_inviter_check(
            "456", "10001", "20002", "onebot:2", 1103, received_at=1103
        )
        self.assertTrue(
            storage.complete_member_inviter_check(
                row["id"], second["verification"]["claim_token"], "20002", now=1104
            )
        )
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual(
            ["not_member", "verified"],
            [
                item["result"]
                for item in storage.get_member_verification_responses(row["id"])
            ],
        )

    def test到期回答可抢占提醒租约_旧令牌不能完成提醒(self):
        row = self.create_prompted_session(now=100, reminder=600)
        reminder_job = storage.claim_due_member_verification_reminder(now=700)
        self.assertEqual("reminding", reminder_job["state"])
        answer = storage.begin_member_inviter_check(
            "456", "10001", "20002", "onebot:3", 700, received_at=700
        )
        self.assertEqual("claimed", answer["status"])
        self.assertFalse(
            storage.complete_member_verification_reminder(
                row["id"], reminder_job["claim_token"], now=700
            )
        )
        self.assertTrue(
            storage.complete_member_inviter_check(
                row["id"], answer["verification"]["claim_token"], "20002", now=701
            )
        )

    def test成员接口失败保留答案且追问到点后仍先重试答案(self):
        row = self.create_prompted_session(now=100, reminder=600)
        answer = storage.begin_member_inviter_check(
            "456", "10001", "20002", "onebot:4", 650, received_at=650
        )
        token = answer["verification"]["claim_token"]
        self.assertTrue(
            storage.defer_member_inviter_check(row["id"], token, "接口失败", now=800)
        )
        self.assertIsNone(storage.claim_due_member_verification_reminder(now=900))
        retry = storage.claim_pending_member_inviter_check(now=900)
        self.assertEqual(row["id"], retry["id"])
        self.assertEqual("20002", retry["candidate_qq"])

    def test目标群筛选不会领取其他群任务(self):
        self.create_prompted_session(now=100, reminder=60, group_id="456")
        other = storage.start_member_verification("999", "10002", "", 100, now=100)
        self.assertIsNone(
            storage.claim_member_verification_prompt(
                other["id"], now=100, group_ids={"456"}
            )
        )
        self.assertIsNone(
            storage.claim_due_member_verification_reminder(
                now=200, group_ids={"9999"}
            )
        )
        self.assertIsNotNone(
            storage.claim_due_member_verification_reminder(
                now=200, group_ids={"456"}
            )
        )

    def test配置移除目标群会取消遗留会话防止重新启用后追问(self):
        kept = self.create_prompted_session(now=100, reminder=60, group_id="456")
        removed = storage.start_member_verification("999", "10002", "", 100, now=100)
        prompt = storage.claim_member_verification_prompt(removed["id"], now=100)
        storage.complete_member_verification_prompt(
            removed["id"], prompt["claim_token"], 60, sent_at=100
        )
        self.assertEqual(
            1,
            storage.cancel_member_verifications_outside_groups({"456"}, now=200),
        )
        self.assertEqual("pending", storage.get_member_verification(kept["id"])["state"])
        self.assertEqual(
            "cancelled", storage.get_member_verification(removed["id"])["state"]
        )
        self.assertIsNone(
            storage.claim_due_member_verification_reminder(
                now=1000, group_ids={"999"}
            )
        )

    def test并发后台只领取一次提醒且成功后持久防重(self):
        row = self.create_prompted_session(now=100, reminder=60)

        with ThreadPoolExecutor(max_workers=8) as pool:
            jobs = list(
                pool.map(
                    lambda _: storage.claim_due_member_verification_reminder(now=160),
                    range(8),
                )
            )
        claimed = [job for job in jobs if job]
        self.assertEqual(1, len(claimed))
        self.assertEqual(1, claimed[0]["reminder_attempts"])
        self.assertTrue(
            storage.complete_member_verification_reminder(
                row["id"], claimed[0]["claim_token"], now=161
            )
        )
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("pending", completed["state"])
        self.assertIsNone(completed["deadline_at"])
        self.assertEqual(161, completed["reminder_sent_at"])
        self.assertIsNone(storage.claim_due_member_verification_reminder(now=9999))

    def test崩溃恢复和发送失败均有界退避且停止提醒后仍可回答(self):
        crashed = self.create_prompted_session(now=100, reminder=60)
        for attempt, now_value in enumerate((160, 190, 220, 250, 280), start=1):
            job = storage.claim_due_member_verification_reminder(
                now=now_value, lease_seconds=30
            )
            self.assertIsNotNone(job)
            self.assertEqual(attempt, job["reminder_attempts"])
        self.assertIsNone(
            storage.claim_due_member_verification_reminder(now=310, lease_seconds=30)
        )
        recovered = storage.get_member_verification(crashed["id"])
        self.assertEqual("pending", recovered["state"])
        self.assertIsNone(recovered["deadline_at"])
        self.assertIsNone(recovered["reminder_sent_at"])

        answer = storage.begin_member_inviter_check(
            "456", "10001", "20002", "onebot:after-crashes", 311, received_at=311
        )
        self.assertEqual("claimed", answer["status"])
        self.assertTrue(
            storage.complete_member_inviter_check(
                crashed["id"], answer["verification"]["claim_token"], "20002", now=312
            )
        )

        failed = storage.start_member_verification(
            "456", "10002", "另一位新人", 100, now=100
        )
        prompt = storage.claim_member_verification_prompt(failed["id"], now=100)
        self.assertTrue(
            storage.complete_member_verification_prompt(
                failed["id"], prompt["claim_token"], 60, sent_at=100
            )
        )
        expected_next_attempts = (165, 175, 195, 235, None)
        for attempt, (now_value, expected_next) in enumerate(
            zip((160, 165, 175, 195, 235), expected_next_attempts), start=1
        ):
            job = storage.claim_due_member_verification_reminder(now=now_value)
            self.assertEqual(attempt, job["reminder_attempts"])
            self.assertTrue(
                storage.release_member_verification_reminder(
                    failed["id"], job["claim_token"], "模拟发送失败", now=now_value
                )
            )
            current = storage.get_member_verification(failed["id"])
            self.assertEqual(expected_next, current["next_attempt_at"])
        exhausted = storage.get_member_verification(failed["id"])
        self.assertEqual("pending", exhausted["state"])
        self.assertIsNone(exhausted["deadline_at"])
        self.assertEqual(5, exhausted["reminder_attempts"])
        self.assertEqual(
            "claimed",
            storage.begin_member_inviter_check(
                "456", "10002", "20002", "onebot:after-failures", 236, received_at=236
            )["status"],
        )

    def test旧超时和踢人租约启动时前向迁移并带活动唯一约束(self):
        checking = storage.start_member_verification(
            "456", "10001", "新人甲", 100, now=100
        )
        kicking = storage.start_member_verification(
            "456", "10002", "新人乙", 100, now=100
        )
        with sqlite3.connect(storage.DB_PATH) as conn:
            for row, state, kind in (
                (checking, "checking_timeout", "timeout"),
                (kicking, "kicking", "kick"),
            ):
                conn.execute(
                    """
                    UPDATE member_verifications
                       SET state = ?, prompt_sent_at = 100, deadline_at = 160,
                           claim_token = 'legacy-token', claim_kind = ?,
                           claimed_at = 150, next_attempt_at = 170,
                           kick_requested_at = 155
                     WHERE id = ?
                    """,
                    (state, kind, row["id"]),
                )

        storage.init_db()
        storage.init_db()
        with sqlite3.connect(storage.DB_PATH) as conn:
            tables = {
                row[0]
                for row in conn.execute(
                    "SELECT name FROM sqlite_master WHERE type = 'table'"
                )
            }
            indexes = {
                row[0]
                for row in conn.execute(
                    "SELECT name FROM sqlite_master WHERE type = 'index'"
                )
            }
            index_sql = conn.execute(
                "SELECT sql FROM sqlite_master WHERE name = ?",
                ("idx_member_verifications_active",),
            ).fetchone()[0]
            columns = {
                row[1]
                for row in conn.execute("PRAGMA table_info(member_verifications)")
            }
        self.assertIn("member_verifications", tables)
        self.assertIn("member_verification_responses", tables)
        self.assertIn("idx_member_verifications_active", indexes)
        self.assertIn("reminder_attempts", columns)
        self.assertIn("reminder_sent_at", columns)
        self.assertIn("reminding", index_sql)
        self.assertNotIn("checking_timeout", index_sql)
        self.assertNotIn("kicking", index_sql)
        for original in (checking, kicking):
            migrated = storage.get_member_verification(original["id"])
            self.assertEqual("pending", migrated["state"])
            self.assertIsNone(migrated["deadline_at"])
            self.assertIsNone(migrated["claim_token"])
            self.assertIsNone(migrated["claim_kind"])
            self.assertIsNone(migrated["claimed_at"])
            self.assertIsNone(migrated["next_attempt_at"])
            self.assertIsNone(migrated["kick_requested_at"])


class MemberVerificationBotTests(MemberVerificationTestCase):
    def test配置样例包含安全默认值(self):
        for name in ("config.example.json", "config.server.example.json"):
            data = json.loads((BOT_DIR / name).read_text(encoding="utf-8"))
            self.assertIs(False, data["new_member_verification_enabled"])
            self.assertEqual([], data["new_member_verification_groups"])
            self.assertEqual(1800, data["new_member_verification_timeout_seconds"])
            self.assertEqual(
                300, data["new_member_verification_poll_interval_seconds"]
            )

    def test五分钟后台轮询不会被旧上限截断(self):
        self.assertEqual(300, bot._member_verification_poll_interval({}))
        self.assertEqual(
            300,
            bot._member_verification_poll_interval(
                {"new_member_verification_poll_interval_seconds": 300}
            ),
        )
        self.assertEqual(
            3600,
            bot._member_verification_poll_interval(
                {"new_member_verification_poll_interval_seconds": 7200}
            ),
        )

    def test目标群入群提示与有效回答完成验证(self):
        client = FakeOneBotClient({"10001", "20002", "99999"})
        cfg = verification_cfg()
        self.assertEqual(300, bot._member_verification_poll_interval(cfg))
        asyncio.run(bot.on_event(client, cfg, join_event()))
        row = storage.get_active_member_verification("456", "10001")
        self.assertEqual("pending", row["state"])
        self.assertIsNone(row["inviter_qq"])
        self.assertEqual("send_group_msg", client.actions[0][0])
        prompt = client.actions[0][1]["message"]
        self.assertEqual("at", prompt[0]["type"])
        self.assertIn("请真正 @“释迦的助理”", prompt[1]["data"]["text"])
        self.assertIn("邀请人QQ：123456789", prompt[1]["data"]["text"])
        self.assertNotIn("逾期", prompt[1]["data"]["text"])
        self.assertNotIn("移出群", prompt[1]["data"]["text"])

        asyncio.run(bot.on_event(client, cfg, reply_event()))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual(
            ["send_group_msg", "get_group_member_list", "send_group_msg"],
            [name for name, _ in client.actions],
        )

    def test待验证新人自然语言登记先收到严格格式指引再安全核验(self):
        client = FakeOneBotClient({"10001", "20002", "99999"})
        cfg = verification_cfg()
        asyncio.run(bot.on_event(client, cfg, join_event()))
        row = storage.get_active_member_verification("456", "10001")

        inquiry = reply_event(message_id=89)
        inquiry["message"] = [
            {"type": "at", "data": {"qq": "99999"}},
            {
                "type": "text",
                "data": {"text": " 我想登记邀请人QQ是20002，应该怎么填？"},
            },
        ]
        asyncio.run(bot.on_event(client, cfg, inquiry))

        unchanged = storage.get_member_verification(row["id"])
        self.assertEqual("pending", unchanged["state"])
        self.assertEqual([], storage.get_member_verification_responses(row["id"]))
        self.assertEqual(
            ["send_group_msg", "send_group_msg"],
            [name for name, _ in client.actions],
        )
        guidance = client.actions[-1][1]["message"][1]["data"]["text"]
        self.assertIn("你本人有待登记", guidance)
        self.assertIn("只发送“邀请人QQ：123456789”", guidance)
        self.assertIn("不要填写自己的 QQ", guidance)

        strict = reply_event(message_id=90, when=1101)
        strict["message"] = [
            {"type": "at", "data": {"qq": "99999"}},
            {"type": "text", "data": {"text": " 邀请人QQ：20002"}},
        ]
        asyncio.run(bot.on_event(client, cfg, strict))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual(
            ["get_group_member_list", "send_group_msg"],
            [name for name, _ in client.actions[-2:]],
        )

    def test明确邀请入群通知自动记录操作者且重复通知不重复确认(self):
        client = FakeOneBotClient({"10001", "20002", "99999"})
        cfg = verification_cfg()
        event = join_event(sub_type="invite", operator=20002)

        asyncio.run(bot.on_event(client, cfg, event))

        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        with sqlite3.connect(storage.DB_PATH) as conn:
            conn.row_factory = sqlite3.Row
            completed = conn.execute(
                """
                SELECT * FROM member_verifications
                 WHERE group_id = '456' AND newcomer_qq = '10001'
                 ORDER BY id DESC LIMIT 1
                """
            ).fetchone()
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual(["send_group_msg"], [name for name, _ in client.actions])
        self.assertIn(
            "本次群成员邀请自动记录邀请人 QQ：20002",
            client.actions[0][1]["message"][1]["data"]["text"],
        )

        action_count = len(client.actions)
        asyncio.run(bot.on_event(client, cfg, event))
        self.assertEqual(action_count, len(client.actions))
        with sqlite3.connect(storage.DB_PATH) as conn:
            count = conn.execute(
                """
                SELECT COUNT(*) FROM member_verifications
                 WHERE group_id = '456' AND newcomer_qq = '10001'
                """
            ).fetchone()[0]
        self.assertEqual(1, count)

    def test只有可靠invite操作者才自动记录_approve与无效字段沿用原流程(self):
        base = join_event()
        self.assertIsNone(bot.extract_group_increase_inviter_qq(base))
        for sub_type, operator in (
            ("invite", 0),
            ("invite", 10001),
            ("invite", 99999),
            ("approve", 20002),
        ):
            with self.subTest(sub_type=sub_type, operator=operator):
                event = join_event(sub_type=sub_type, operator=operator)
                self.assertIsNone(bot.extract_group_increase_inviter_qq(event))

        client = FakeOneBotClient({"10001", "20002", "99999"})
        asyncio.run(
            bot.on_event(
                client,
                verification_cfg(),
                join_event(sub_type="invite", operator=0),
            )
        )
        row = storage.get_active_member_verification("456", "10001")
        self.assertEqual("pending", row["state"])
        self.assertIsNone(row["inviter_qq"])
        self.assertIn(
            "请真正 @“释迦的助理”",
            client.actions[0][1]["message"][1]["data"]["text"],
        )

    def test非目标群不创建会话_空目标列表绝不代表全部群(self):
        client = FakeOneBotClient()
        cfg = verification_cfg(groups=(123,))
        self.assertFalse(
            asyncio.run(bot.handle_member_verification_notice(client, cfg, join_event()))
        )
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        cfg["new_member_verification_groups"] = []
        self.assertEqual(set(), bot.member_verification_groups(cfg))

    def test正文伪造at_引用和转发里的QQ均不能通过(self):
        fake = reply_event()
        fake["message"] = [
            {"type": "text", "data": {"text": "[CQ:at,qq=99999] 20002"}},
            {
                "type": "reply",
                "data": {"id": "1", "text": "邀请人 30003"},
            },
            {
                "type": "forward",
                "data": {"content": [{"type": "text", "data": {"text": "40004"}}]},
            },
        ]
        self.assertFalse(bot.is_real_at_self(fake))
        # 文本 CQ 码不是可信 at，而且其中的机器人 QQ 会造成多 QQ 拒绝。
        self.assertIsNone(bot.extract_inviter_qq(fake)[0])
        fake["message"] = "[CQ:at,qq=99999] 20002"
        self.assertFalse(bot.is_real_at_self(fake))
        self.assertIsNone(bot.extract_inviter_qq(fake)[0])

    def test无效邀请人继续追问且本人和机器人不能冒充(self):
        client = FakeOneBotClient({"10001", "99999"})
        cfg = verification_cfg()
        asyncio.run(bot.on_event(client, cfg, join_event()))
        asyncio.run(bot.on_event(client, cfg, reply_event(candidate="30003")))
        row = storage.get_active_member_verification("456", "10001")
        self.assertEqual("pending", row["state"])
        self.assertEqual(1, row["invalid_attempts"])
        self.assertIn("当前不在本群", client.actions[-1][1]["message"][1]["data"]["text"])

        action_count = len(client.actions)
        asyncio.run(
            bot.on_event(
                client,
                cfg,
                reply_event(candidate="10001", message_id=89, when=1101),
            )
        )
        self.assertEqual(action_count + 1, len(client.actions))
        self.assertNotIn(
            "get_group_member_list",
            [name for name, _ in client.actions[action_count:]],
        )

    def test成员查询失败不通过且保留答案供重启恢复(self):
        client = FakeOneBotClient({"10001", "20002", "99999"})
        cfg = verification_cfg()
        asyncio.run(bot.on_event(client, cfg, join_event()))
        client.member_error = RuntimeError("NapCat 暂时不可用")
        asyncio.run(bot.on_event(client, cfg, reply_event()))
        row = storage.get_active_member_verification("456", "10001")
        self.assertEqual("checking_inviter", row["state"])
        self.assertEqual("20002", row["candidate_qq"])
        self.assertIsNone(row["claim_token"])

        client.member_error = None
        with sqlite3.connect(storage.DB_PATH) as conn:
            conn.execute(
                "UPDATE member_verifications SET next_attempt_at = 0 WHERE id = ?",
                (row["id"],),
            )
        asyncio.run(bot.run_member_verification_job_once(client, cfg))
        self.assertEqual(
            "verified", storage.get_member_verification(row["id"])["state"]
        )

    def test到期只追问一次_成功持久防重且之后仍能登记(self):
        row = self.create_prompted_session(now=100, reminder=60)
        client = FakeOneBotClient({"10001", "20002", "99999"})
        job = storage.claim_due_member_verification_reminder(now=160)
        asyncio.run(bot.process_member_verification_reminder(client, job))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("pending", completed["state"])
        self.assertIsNone(completed["deadline_at"])
        self.assertIsNotNone(completed["reminder_sent_at"])
        self.assertEqual(["send_group_msg"], [name for name, _ in client.actions])
        reminder_text = client.actions[0][1]["message"][1]["data"]["text"]
        self.assertIn("还没有收到邀请人 QQ", reminder_text)
        self.assertNotIn("逾期", reminder_text)
        self.assertNotIn("移出群", reminder_text)
        self.assertIsNone(storage.claim_due_member_verification_reminder(now=9999))

        action_count = len(client.actions)
        asyncio.run(
            bot.on_event(
                client,
                verification_cfg(reminder=60),
                reply_event(when=10000, message_id=90),
            )
        )
        self.assertEqual("verified", storage.get_member_verification(row["id"])["state"])
        self.assertEqual(
            ["get_group_member_list", "send_group_msg"],
            [name for name, _ in client.actions[action_count:]],
        )

    def test追问发送失败可恢复且全程不查询成员(self):
        row = self.create_prompted_session(now=100, reminder=60)
        client = FakeOneBotClient()
        client.send_error = RuntimeError("发送接口暂时失败")
        job = storage.claim_due_member_verification_reminder(now=160)
        asyncio.run(bot.process_member_verification_reminder(client, job))
        failed = storage.get_member_verification(row["id"])
        self.assertEqual("pending", failed["state"])
        self.assertEqual(160, failed["deadline_at"])
        self.assertEqual(1, failed["reminder_attempts"])
        self.assertIsNotNone(failed["next_attempt_at"])
        self.assertEqual(["send_group_msg"], [name for name, _ in client.actions])

        client.send_error = None
        with sqlite3.connect(storage.DB_PATH) as conn:
            conn.execute(
                "UPDATE member_verifications SET next_attempt_at = 0 WHERE id = ?",
                (row["id"],),
            )
        job = storage.claim_due_member_verification_reminder(now=165)
        asyncio.run(bot.process_member_verification_reminder(client, job))
        recovered = storage.get_member_verification(row["id"])
        self.assertEqual("pending", recovered["state"])
        self.assertIsNone(recovered["deadline_at"])
        self.assertEqual(2, recovered["reminder_attempts"])
        self.assertIsNotNone(recovered["reminder_sent_at"])
        self.assertEqual(
            ["send_group_msg", "send_group_msg"],
            [name for name, _ in client.actions],
        )

    def test运行源码不包含OneBot自动踢人动作(self):
        for name in ("bot.py", "storage.py"):
            source = (BOT_DIR / name).read_text(encoding="utf-8")
            self.assertNotIn("set_group_kick", source)

    def test离群通知取消活动验证且重复回答不生效(self):
        client = FakeOneBotClient({"10001", "20002", "99999"})
        cfg = verification_cfg()
        asyncio.run(bot.on_event(client, cfg, join_event()))
        leave = join_event()
        leave["notice_type"] = "group_decrease"
        leave["sub_type"] = "leave"
        leave["time"] = 1100
        leave["_grandumi_received_at"] = 1100
        asyncio.run(bot.on_event(client, cfg, leave))
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        self.assertFalse(
            asyncio.run(
                bot.handle_member_verification_reply(
                    client, cfg, reply_event(when=1101)
                )
            )
        )

    def test成员列表混入其他群数据视为API失败(self):
        class WrongGroupClient(FakeOneBotClient):
            async def call_action(self, action, params, timeout=20):
                if action == "get_group_member_list":
                    return {
                        "status": "ok",
                        "retcode": 0,
                        "data": [{"group_id": 999, "user_id": 20002}],
                    }
                return await super().call_action(action, params, timeout)

        with self.assertRaisesRegex(RuntimeError, "其他群"):
            asyncio.run(bot.get_authoritative_group_members(WrongGroupClient(), 456))


if __name__ == "__main__":
    unittest.main()
