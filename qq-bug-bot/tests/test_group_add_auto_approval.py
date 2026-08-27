# -*- coding: utf-8 -*-

import asyncio
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot
import storage


def approval_cfg(groups=(456,)):
    return {
        "group_add_auto_approval_enabled": True,
        "group_add_auto_approval_groups": list(groups),
    }


def add_request(
    comment="问题：请填写邀请人qq号\n答案：20002",
    group_id=456,
    applicant=10001,
    flag="request-1",
    sub_type="add",
    self_id=99999,
):
    return {
        "post_type": "request",
        "request_type": "group",
        "sub_type": sub_type,
        "group_id": group_id,
        "user_id": applicant,
        "self_id": self_id,
        "comment": comment,
        "flag": flag,
    }


class FakeOneBotClient:
    def __init__(self, members=()):
        self.members = {str(value) for value in members}
        self.actions = []
        self.sent = []
        self.member_error = None
        self.set_error_count = 0
        self.set_error = RuntimeError("审批动作失败")

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, params))
        if action == "get_group_member_list":
            if self.member_error:
                raise self.member_error
            return {
                "status": "ok",
                "retcode": 0,
                "data": [
                    {"group_id": params["group_id"], "user_id": int(qq)}
                    for qq in sorted(self.members)
                ],
            }
        if action == "set_group_add_request":
            if self.set_error_count:
                self.set_error_count -= 1
                raise self.set_error
            return {"status": "ok", "retcode": 0, "data": None}
        if action == "send_group_msg":
            return {"status": "ok", "retcode": 0, "data": {"message_id": 1}}
        raise AssertionError(f"未预期的 OneBot 动作：{action}")

    async def send(self, payload):
        self.sent.append(json.loads(payload))


class GroupAddAutoApprovalTests(unittest.TestCase):
    def setUp(self):
        bot._handled_group_add_requests.clear()
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

    def test配置样例默认安全关闭且空列表不代表全部群(self):
        for name in ("config.example.json", "config.server.example.json"):
            data = json.loads((BOT_DIR / name).read_text(encoding="utf-8"))
            self.assertIs(False, data["group_add_auto_approval_enabled"])
            self.assertEqual([], data["group_add_auto_approval_groups"])
        self.assertEqual(set(), bot.group_add_auto_approval_groups(approval_cfg(())))

    def test纯QQ与常见答案包装可解析_多个号码和无关文字拒绝(self):
        valid = (
            "20002",
            "QQ号：20002",
            "邀请人QQ是 20002",
            "问题：请填写邀请人qq号\n答案：20002",
            "问题：请填写邀请人qq号\n回答: 邀请人是：20002",
        )
        for comment in valid:
            with self.subTest(comment=comment):
                self.assertEqual(
                    ("20002", ""),
                    bot.extract_group_request_inviter_qq(add_request(comment=comment)),
                )
        invalid = ("", "随便写 20002 就行", "20002 30003", "答案：20002 30003")
        for comment in invalid:
            with self.subTest(comment=comment):
                candidate, error = bot.extract_group_request_inviter_qq(
                    add_request(comment=comment)
                )
                self.assertIsNone(candidate)
                self.assertTrue(error)

    def test邀请人在群则用事件flag同意且强制实时成员查询(self):
        client = FakeOneBotClient({"20002", "99999"})
        handled = asyncio.run(bot.on_event(client, approval_cfg(), add_request()))
        self.assertIsNone(handled)
        self.assertEqual(
            ["get_group_member_list", "set_group_add_request"],
            [name for name, _ in client.actions],
        )
        query = client.actions[0][1]
        self.assertEqual({"group_id": 456, "no_cache": True}, query)
        action = client.actions[1][1]
        self.assertEqual("request-1", action["flag"])
        self.assertEqual("add", action["sub_type"])
        self.assertIs(True, action["approve"])
        self.assertNotIn("reason", action)

    def test邀请人不在群直接拒绝并返回清晰原因(self):
        client = FakeOneBotClient({"99999"})
        asyncio.run(bot.on_event(client, approval_cfg(), add_request()))
        action = client.actions[-1][1]
        self.assertIs(False, action["approve"])
        self.assertIn("20002", action["reason"])
        self.assertIn("不在本群", action["reason"])

    def test无效答案_自我邀请和机器人自身均直接拒绝(self):
        cases = (
            ("答案：乱填", "回答格式"),
            ("答案：10001", "申请人自己"),
            ("答案：99999", "机器人"),
        )
        for index, (comment, reason) in enumerate(cases):
            with self.subTest(comment=comment):
                client = FakeOneBotClient({"99999"})
                event = add_request(comment=comment, flag=f"invalid-{index}")
                asyncio.run(bot.on_event(client, approval_cfg(), event))
                self.assertEqual(
                    ["get_group_member_list", "set_group_add_request"],
                    [a for a, _ in client.actions],
                )
                self.assertIs(False, client.actions[1][1]["approve"])
                self.assertIn(reason, client.actions[1][1]["reason"])

    def test成员查询失败保持待审批_相同事件恢复后可重试(self):
        client = FakeOneBotClient({"20002", "99999"})
        client.member_error = RuntimeError("成员接口暂时不可用")
        event = add_request()
        asyncio.run(bot.on_event(client, approval_cfg(), event))
        self.assertNotIn("set_group_add_request", [name for name, _ in client.actions])
        self.assertNotIn("456:request-1", bot._handled_group_add_requests)

        client.member_error = None
        asyncio.run(bot.on_event(client, approval_cfg(), event))
        self.assertEqual(
            1,
            sum(name == "set_group_add_request" for name, _ in client.actions),
        )

    def test缺少或无效关键字段时保持待审批(self):
        missing_flag = add_request()
        missing_flag.pop("flag")
        missing_applicant = add_request()
        missing_applicant.pop("user_id")
        cases = (
            missing_flag,
            add_request(flag=""),
            add_request(flag="   "),
            add_request(flag=12345),
            missing_applicant,
            add_request(applicant="无效QQ"),
            add_request(self_id=None),
        )
        for event in cases:
            with self.subTest(
                flag=event.get("flag"),
                applicant=event.get("user_id"),
                self_id=event.get("self_id"),
            ):
                client = FakeOneBotClient({"20002"})
                self.assertTrue(
                    asyncio.run(
                        bot.handle_group_add_auto_approval(
                            client, approval_cfg(), event
                        )
                    )
                )
                self.assertEqual([], client.actions)

    def test非目标群_空目标列表和invite子类型均完全忽略(self):
        cases = (
            (approval_cfg((123,)), add_request()),
            (approval_cfg(()), add_request()),
            (approval_cfg(), add_request(sub_type="invite")),
        )
        for cfg, event in cases:
            with self.subTest(cfg=cfg, sub_type=event["sub_type"]):
                client = FakeOneBotClient({"20002"})
                self.assertFalse(
                    asyncio.run(bot.handle_group_add_auto_approval(client, cfg, event))
                )
                self.assertEqual([], client.actions)

    def test成功事件幂等_动作失败不去重并允许再次处理(self):
        client = FakeOneBotClient({"20002", "99999"})
        event = add_request()
        asyncio.run(bot.on_event(client, approval_cfg(), event))
        asyncio.run(bot.on_event(client, approval_cfg(), event))
        self.assertEqual(
            1, sum(a == "get_group_member_list" for a, _ in client.actions)
        )
        self.assertEqual(
            1, sum(a == "set_group_add_request" for a, _ in client.actions)
        )

        bot._handled_group_add_requests.clear()
        retry_client = FakeOneBotClient({"20002", "99999"})
        retry_client.set_error_count = 1
        asyncio.run(bot.on_event(retry_client, approval_cfg(), event))
        self.assertNotIn("456:request-1", bot._handled_group_add_requests)
        asyncio.run(bot.on_event(retry_client, approval_cfg(), event))
        self.assertEqual(
            2,
            sum(a == "set_group_add_request" for a, _ in retry_client.actions),
        )

    def test群成员邀请好友时空白或乱填comment仍用原add事件直接同意(self):
        cases = (
            (None, " member-invite-0 "),
            ("", "member-invite-1"),
            ("完全无效的答案", "member-invite-2"),
        )
        for comment, flag in cases:
            with self.subTest(comment=comment, flag=flag):
                client = FakeOneBotClient({"10001", "99999"})
                event = add_request(comment=comment, flag=flag)

                asyncio.run(bot.on_event(client, approval_cfg(), event))

                self.assertEqual(
                    ["get_group_member_list", "set_group_add_request"],
                    [name for name, _ in client.actions],
                )
                self.assertEqual(
                    {"flag": flag, "sub_type": "add", "approve": True},
                    client.actions[1][1],
                )

    def test自动审批成功后持久授权_入群登记必须与审核邀请人一致(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
                "new_member_verification_timeout_seconds": 1800,
            }
        )
        client = FakeOneBotClient({"20002", "30003", "99999"})
        request = add_request()
        request["time"] = 1000
        request["_grandumi_received_at"] = 1000
        asyncio.run(bot.on_event(client, cfg, request))
        prejoin = storage.get_active_member_verification("456", "10001")
        self.assertEqual("awaiting_join", prejoin["state"])
        self.assertEqual("20002", prejoin["inviter_qq"])

        notice = {
            "post_type": "notice",
            "notice_type": "group_increase",
            "sub_type": "approve",
            "group_id": 456,
            "user_id": 10001,
            "self_id": 99999,
            "time": 1010,
            "_grandumi_received_at": 1010,
        }
        client.members.add("10001")
        self.assertTrue(
            asyncio.run(bot.handle_member_verification_notice(client, cfg, notice))
        )
        active = storage.get_active_member_verification("456", "10001")
        self.assertEqual("pending", active["state"])
        self.assertEqual("20002", active["inviter_qq"])
        prompt = client.actions[-1][1]["message"][1]["data"]["text"]
        self.assertIn("已审核邀请人 QQ：20002", prompt)
        action_count = len(client.actions)
        deadline_at = active["deadline_at"]
        asyncio.run(bot.on_event(client, cfg, notice))
        self.assertEqual(action_count, len(client.actions))
        replayed = storage.get_active_member_verification("456", "10001")
        self.assertEqual(active["id"], replayed["id"])
        self.assertEqual(deadline_at, replayed["deadline_at"])

        mismatch = {
            "post_type": "message",
            "message_type": "group",
            "group_id": 456,
            "user_id": 10001,
            "self_id": 99999,
            "time": 1020,
            "message_id": 10,
            "_grandumi_received_at": 1020,
            "message": [
                {"type": "at", "data": {"qq": "99999"}},
                {"type": "text", "data": {"text": " 邀请人QQ:30003"}},
            ],
        }
        asyncio.run(bot.on_event(client, cfg, mismatch))
        unchanged = storage.get_active_member_verification("456", "10001")
        self.assertEqual("pending", unchanged["state"])
        self.assertEqual("20002", unchanged["inviter_qq"])
        self.assertEqual([], storage.get_member_verification_responses(active["id"]))
        self.assertIn(
            "不能通过群消息改写",
            client.actions[-1][1]["message"][1]["data"]["text"],
        )

        correct = dict(mismatch)
        correct["time"] = 1021
        correct["message_id"] = 11
        correct["_grandumi_received_at"] = 1021
        correct["message"] = [
            {"type": "at", "data": {"qq": "99999"}},
            {"type": "text", "data": {"text": " 邀请人QQ:20002"}},
        ]
        asyncio.run(bot.on_event(client, cfg, correct))
        completed = storage.get_member_verification(active["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual({"456"}, bot.member_verification_groups(cfg))

        action_count = len(client.actions)
        raw_sent = len(client.sent)
        asyncio.run(bot.on_event(client, cfg, correct))
        self.assertEqual(action_count, len(client.actions))
        self.assertEqual(raw_sent, len(client.sent))

    def test审批待确认和旧存量无会话的严格登记返回专用提示且不授权(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
                "new_member_verification_timeout_seconds": 1800,
            }
        )
        client = FakeOneBotClient({"20002", "99999"})
        client.set_error_count = 1
        event = add_request()
        event["time"] = 1000
        event["_grandumi_received_at"] = 1000
        asyncio.run(bot.on_event(client, cfg, event))
        pending = storage.get_active_member_verification("456", "10001")
        self.assertEqual("approval_pending", pending["state"])

        attempt = {
            "post_type": "message",
            "message_type": "group",
            "group_id": 456,
            "user_id": 10001,
            "self_id": 99999,
            "time": 1001,
            "message_id": 12,
            "_grandumi_received_at": 1001,
            "message": [
                {"type": "at", "data": {"qq": "99999"}},
                {"type": "text", "data": {"text": " 邀请人QQ:20002"}},
            ],
        }
        asyncio.run(bot.on_event(client, cfg, attempt))
        self.assertEqual(pending, storage.get_member_verification(pending["id"]))
        self.assertEqual([], storage.get_member_verification_responses(pending["id"]))
        self.assertEqual([], client.sent)
        self.assertEqual(
            bot.at_message(
                "10001", "加群审批结果尚未确认，请稍后再试或联系释迦大人。"
            ),
            client.actions[-1][1]["message"],
        )

        # 模拟旧版本已入群、但从未建立过持久验证会话的存量成员。
        impostor = dict(attempt)
        impostor["user_id"] = 30003
        impostor["message_id"] = 13
        asyncio.run(bot.on_event(client, cfg, impostor))
        self.assertIsNone(storage.get_active_member_verification("456", "30003"))
        self.assertEqual([], client.sent)
        self.assertEqual(
            bot.at_message(
                "30003", "当前没有待登记的邀请人验证，请联系释迦大人核对。"
            ),
            client.actions[-1][1]["message"],
        )

    def test普通含QQ聊天和纯文本艾特不误命中登记专用提示(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
                "new_member_verification_timeout_seconds": 1800,
            }
        )
        client = FakeOneBotClient({"99999"})
        ordinary = {
            "post_type": "message",
            "message_type": "group",
            "group_id": 456,
            "user_id": 30003,
            "self_id": 99999,
            "time": 1001,
            "message_id": 14,
            "_grandumi_received_at": 1001,
            "message": [
                {"type": "at", "data": {"qq": "99999"}},
                {
                    "type": "text",
                    "data": {"text": " 我的 QQ 是 20002，明晚一起打牌吗？"},
                },
            ],
        }
        asyncio.run(bot.on_event(client, cfg, ordinary))
        self.assertEqual([], client.actions)
        self.assertEqual(
            bot.at_message("30003", "我只跟释迦大人聊天"),
            client.sent[-1]["params"]["message"],
        )

        # 即使正文完全仿照登记格式，复制出来的文本 @ 也不是可信结构化 @。
        copied_at = dict(ordinary)
        copied_at["message_id"] = 15
        copied_at["message"] = [
            {
                "type": "text",
                "data": {"text": "@释迦的助理 邀请人QQ:20002"},
            }
        ]
        sent_count = len(client.sent)
        asyncio.run(bot.on_event(client, cfg, copied_at))
        self.assertEqual([], client.actions)
        self.assertEqual(sent_count, len(client.sent))
        self.assertIsNone(storage.get_active_member_verification("456", "30003"))

    def test严格登记格式不接受附加聊天_多个QQ_其他消息段和非目标群(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
            }
        )
        client = FakeOneBotClient({"99999"})
        base = {
            "post_type": "message",
            "message_type": "group",
            "group_id": 456,
            "user_id": 30003,
            "self_id": 99999,
            "time": 1001,
            "message_id": 16,
            "_grandumi_received_at": 1001,
            "message": [
                {"type": "at", "data": {"qq": "99999"}},
                {"type": "text", "data": {"text": " 邀请人 QQ：20002"}},
            ],
        }
        self.assertEqual("20002", bot.extract_strict_inviter_registration_qq(base))

        invalid_messages = (
            [
                {"type": "at", "data": {"qq": "99999"}},
                {
                    "type": "text",
                    "data": {"text": "邀请人QQ:20002，顺便问一下卡组"},
                },
            ],
            [
                {"type": "at", "data": {"qq": "99999"}},
                {
                    "type": "text",
                    "data": {"text": "邀请人QQ:20002 另一个是30003"},
                },
            ],
            [
                {"type": "at", "data": {"qq": "99999"}},
                {"type": "text", "data": {"text": "邀请人QQ:20002"}},
                {"type": "image", "data": {"file": "not-downloaded.jpg"}},
            ],
        )
        for index, message in enumerate(invalid_messages, start=17):
            with self.subTest(message=message):
                event = dict(base)
                event["message_id"] = index
                event["message"] = message
                self.assertIsNone(bot.extract_strict_inviter_registration_qq(event))
                asyncio.run(bot.on_event(client, cfg, event))
                self.assertEqual(
                    bot.at_message("30003", "我只跟释迦大人聊天"),
                    client.sent[-1]["params"]["message"],
                )

        other_group = dict(base)
        other_group["group_id"] = 789
        other_group["message_id"] = 20
        asyncio.run(bot.on_event(client, cfg, other_group))
        self.assertEqual([], client.actions)
        self.assertEqual(
            bot.at_message("30003", "我只跟释迦大人聊天"),
            client.sent[-1]["params"]["message"],
        )

    def test入群通知丢失时仅真实艾特可恢复已成功审批的登记(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
                "new_member_verification_timeout_seconds": 1800,
            }
        )
        client = FakeOneBotClient({"20002", "99999"})
        request = add_request()
        request["time"] = 1000
        request["_grandumi_received_at"] = 1000
        asyncio.run(bot.on_event(client, cfg, request))
        row = storage.get_active_member_verification("456", "10001")
        self.assertEqual("awaiting_join", row["state"])

        copied_at = {
            "post_type": "message",
            "message_type": "group",
            "group_id": 456,
            "user_id": 10001,
            "self_id": 99999,
            "time": 1010,
            "message_id": 20,
            "_grandumi_received_at": 1010,
            "message": [
                {
                    "type": "text",
                    "data": {"text": "@释迦的助理 邀请人QQ:20002"},
                }
            ],
        }
        asyncio.run(bot.on_event(client, cfg, copied_at))
        self.assertEqual([], client.sent)
        self.assertEqual(
            "awaiting_join", storage.get_member_verification(row["id"])["state"]
        )

        client.members.add("10001")
        real_at = dict(copied_at)
        real_at["time"] = 1011
        real_at["message_id"] = 21
        real_at["_grandumi_received_at"] = 1011
        real_at["message"] = [
            {"type": "at", "data": {"qq": "99999"}},
            {"type": "text", "data": {"text": " 邀请人QQ:20002"}},
        ]
        asyncio.run(bot.on_event(client, cfg, real_at))
        completed = storage.get_member_verification(row["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual([], client.sent)

    def test群内发起人的过期flag动作失败不去重并允许重试(self):
        client = FakeOneBotClient({"10001", "99999"})
        client.set_error_count = 2
        client.set_error = RuntimeError("flag 已过期")
        event = add_request(comment="", flag="expired-member-invite")

        asyncio.run(bot.on_event(client, approval_cfg(), event))
        asyncio.run(bot.on_event(client, approval_cfg(), event))

        self.assertEqual(
            [
                "get_group_member_list",
                "set_group_add_request",
                "get_group_member_list",
                "set_group_add_request",
            ],
            [name for name, _ in client.actions],
        )
        self.assertNotIn(
            "456:expired-member-invite", bot._handled_group_add_requests
        )
        for name, params in client.actions:
            if name == "set_group_add_request":
                self.assertEqual("add", params["sub_type"])
                self.assertIs(True, params["approve"])

    def test审批成功去重缓存保持固定上限(self):
        limit = bot._HANDLED_GROUP_ADD_REQUEST_LIMIT
        for index in range(limit + 1):
            bot._remember_handled_group_add_request(f"456:request-{index}")

        self.assertEqual(limit, len(bot._handled_group_add_requests))
        self.assertNotIn("456:request-0", bot._handled_group_add_requests)
        self.assertIn(f"456:request-{limit}", bot._handled_group_add_requests)


if __name__ == "__main__":
    unittest.main()
