# -*- coding: utf-8 -*-

import asyncio
import json
import os
import sqlite3
import sys
import tempfile
import unittest
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
        self.kick_error = None
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
        if action == "set_group_kick":
            if self.kick_error:
                raise self.kick_error
            self.members.discard(str(params["user_id"]))
            return {"status": "ok", "retcode": 0, "data": None}
        raise AssertionError(f"未预期的 OneBot 动作：{action}")


def verification_cfg(groups=(456,), timeout=1800, poll_interval=300):
    return {
        "new_member_verification_enabled": True,
        "new_member_verification_groups": list(groups),
        "new_member_verification_timeout_seconds": timeout,
        "new_member_verification_poll_interval_seconds": poll_interval,
        "allowed_groups": list(groups),
        "chat_agent_enabled": False,
        "admin_agent_enabled": False,
    }


def join_event(group_id=456, newcomer=10001, when=1000, self_id=99999):
    return {
        "post_type": "notice",
        "notice_type": "group_increase",
        "sub_type": "invite",
        "group_id": group_id,
        "user_id": newcomer,
        "operator_id": 20002,
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
    def create_prompted_session(now=1000, timeout=600, group_id="456"):
        row = storage.start_member_verification(
            group_id, "10001", "新人", now, now=now
        )
        prompt = storage.claim_member_verification_prompt(row["id"], now=now)
        assert storage.complete_member_verification_prompt(
            row["id"], prompt["claim_token"], timeout, sent_at=now
        )
        return storage.get_member_verification(row["id"])


class MemberVerificationStorageTests(MemberVerificationTestCase):
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

    def test及时回答可抢占超时检查_旧令牌不得踢人(self):
        row = self.create_prompted_session(now=100, timeout=600)
        timeout_job = storage.claim_due_member_verification_timeout(now=700)
        self.assertEqual("checking_timeout", timeout_job["state"])
        answer = storage.begin_member_inviter_check(
            "456", "10001", "20002", "onebot:3", 700, received_at=700
        )
        self.assertEqual("claimed", answer["status"])
        self.assertFalse(
            storage.authorize_member_verification_kick(
                row["id"], timeout_job["claim_token"], now=700
            )
        )
        self.assertTrue(
            storage.complete_member_inviter_check(
                row["id"], answer["verification"]["claim_token"], "20002", now=701
            )
        )

    def test成员接口失败保留答案且到期后仍先重试答案(self):
        row = self.create_prompted_session(now=100, timeout=600)
        answer = storage.begin_member_inviter_check(
            "456", "10001", "20002", "onebot:4", 650, received_at=650
        )
        token = answer["verification"]["claim_token"]
        self.assertTrue(
            storage.defer_member_inviter_check(row["id"], token, "接口失败", now=800)
        )
        self.assertIsNone(storage.claim_due_member_verification_timeout(now=900))
        retry = storage.claim_pending_member_inviter_check(now=900)
        self.assertEqual(row["id"], retry["id"])
        self.assertEqual("20002", retry["candidate_qq"])

    def test目标群筛选不会领取其他群任务(self):
        self.create_prompted_session(now=100, timeout=60, group_id="456")
        other = storage.start_member_verification("999", "10002", "", 100, now=100)
        self.assertIsNone(
            storage.claim_member_verification_prompt(
                other["id"], now=100, group_ids={"456"}
            )
        )
        self.assertIsNone(
            storage.claim_due_member_verification_timeout(
                now=200, group_ids={"9999"}
            )
        )
        self.assertIsNotNone(
            storage.claim_due_member_verification_timeout(
                now=200, group_ids={"456"}
            )
        )

    def test配置移除目标群会取消遗留会话防止重新启用补踢(self):
        kept = self.create_prompted_session(now=100, timeout=60, group_id="456")
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
            storage.claim_due_member_verification_timeout(
                now=1000, group_ids={"999"}
            )
        )

    def test新表迁移幂等并带活动会话唯一约束(self):
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
        self.assertIn("member_verifications", tables)
        self.assertIn("member_verification_responses", tables)
        self.assertIn("idx_member_verifications_active", indexes)


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
        self.assertEqual("send_group_msg", client.actions[0][0])
        prompt = client.actions[0][1]["message"]
        self.assertEqual("at", prompt[0]["type"])
        self.assertIn("30 分钟内", prompt[1]["data"]["text"])
        self.assertIn("必须真正 @本群机器人", prompt[1]["data"]["text"])

        asyncio.run(bot.on_event(client, cfg, reply_event()))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual(
            ["send_group_msg", "get_group_member_list", "send_group_msg"],
            [name for name, _ in client.actions],
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

    def test超时查询失败绝不踢_恢复后才踢且不拒绝后续申请(self):
        row = self.create_prompted_session(now=100, timeout=60)
        client = FakeOneBotClient({"10001", "99999"})
        client.member_error = RuntimeError("列表失败")
        job = storage.claim_due_member_verification_timeout(now=160)
        asyncio.run(bot.process_member_verification_timeout(client, job))
        self.assertEqual("pending", storage.get_member_verification(row["id"])["state"])
        self.assertNotIn("set_group_kick", [name for name, _ in client.actions])

        client.member_error = None
        with sqlite3.connect(storage.DB_PATH) as conn:
            conn.execute(
                "UPDATE member_verifications SET next_attempt_at = 0 WHERE id = ?",
                (row["id"],),
            )
        job = storage.claim_due_member_verification_timeout(now=165)
        asyncio.run(bot.process_member_verification_timeout(client, job))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("kicked", completed["state"])
        kick = [params for name, params in client.actions if name == "set_group_kick"]
        self.assertEqual(1, len(kick))
        self.assertIs(False, kick[0]["reject_add_request"])

    def test踢人动作失败不标记成功_新人已离群时不再踢(self):
        row = self.create_prompted_session(now=100, timeout=60)
        client = FakeOneBotClient({"10001", "99999"})
        client.kick_error = RuntimeError("权限不足")
        job = storage.claim_due_member_verification_timeout(now=160)
        asyncio.run(bot.process_member_verification_timeout(client, job))
        self.assertEqual("pending", storage.get_member_verification(row["id"])["state"])

        client.kick_error = None
        client.members.discard("10001")
        with sqlite3.connect(storage.DB_PATH) as conn:
            conn.execute(
                "UPDATE member_verifications SET next_attempt_at = 0 WHERE id = ?",
                (row["id"],),
            )
        job = storage.claim_due_member_verification_timeout(now=165)
        before = len([name for name, _ in client.actions if name == "set_group_kick"])
        asyncio.run(bot.process_member_verification_timeout(client, job))
        after = len([name for name, _ in client.actions if name == "set_group_kick"])
        self.assertEqual(before, after)
        self.assertEqual("left", storage.get_member_verification(row["id"])["state"])

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
