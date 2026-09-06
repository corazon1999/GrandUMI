# -*- coding: utf-8 -*-

import asyncio
import json
import os
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot
import storage


ORIGINAL_GROUP_ID = 297542853
SECOND_GROUP_ID = 524996856


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

    def test二群各种答案和空答案均直接同意且不查询不解析不建验证记录(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [SECOND_GROUP_ID],
            }
        )
        cases = (
            ("问题：请填写邀请人qq号\n答案：20002", "second-valid-answer"),
            ("答案：完全乱填 20002 30003", "second-invalid-answer"),
            ("", "second-empty-answer"),
            (None, "second-none-answer"),
        )

        with (
            mock.patch.object(
                bot,
                "get_authoritative_group_members",
                new=mock.AsyncMock(side_effect=AssertionError("二群不得查询成员列表")),
            ) as member_query,
            mock.patch.object(
                bot,
                "extract_group_request_inviter_qq",
                side_effect=AssertionError("二群不得解析邀请人答案"),
            ) as answer_parser,
            mock.patch.object(
                storage,
                "prepare_member_verification_approval",
                side_effect=AssertionError("二群不得创建邀请人验证记录"),
            ) as prepare_verification,
        ):
            for comment, flag in cases:
                with self.subTest(comment=comment, flag=flag):
                    client = FakeOneBotClient()
                    event = add_request(
                        comment=comment,
                        group_id=SECOND_GROUP_ID,
                        flag=flag,
                    )

                    asyncio.run(bot.on_event(client, cfg, event))

                    self.assertEqual(
                        ["set_group_add_request"],
                        [name for name, _ in client.actions],
                    )
                    self.assertEqual(
                        {"flag": flag, "sub_type": "add", "approve": True},
                        client.actions[0][1],
                    )

            client = FakeOneBotClient()
            event = add_request(
                group_id=SECOND_GROUP_ID,
                flag="second-missing-comment",
            )
            event.pop("comment")
            asyncio.run(bot.on_event(client, cfg, event))
            self.assertEqual(
                [
                    (
                        "set_group_add_request",
                        {
                            "flag": "second-missing-comment",
                            "sub_type": "add",
                            "approve": True,
                        },
                    )
                ],
                client.actions,
            )

        member_query.assert_not_awaited()
        answer_parser.assert_not_called()
        prepare_verification.assert_not_called()
        with sqlite3.connect(storage.DB_PATH) as conn:
            count = conn.execute(
                "SELECT COUNT(*) FROM member_verifications"
            ).fetchone()[0]
        self.assertEqual(0, count)

    def test原群继续实时核验邀请人并创建新人验证预备记录(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [ORIGINAL_GROUP_ID],
                "new_member_verification_timeout_seconds": 1800,
            }
        )
        client = FakeOneBotClient({"20002", "99999"})
        event = add_request(group_id=ORIGINAL_GROUP_ID, flag="original-approved")
        event["time"] = 1000
        event["_grandumi_received_at"] = 1000

        asyncio.run(bot.on_event(client, cfg, event))

        self.assertEqual(
            ["get_group_member_list", "set_group_add_request"],
            [name for name, _ in client.actions],
        )
        self.assertEqual(
            {"group_id": ORIGINAL_GROUP_ID, "no_cache": True},
            client.actions[0][1],
        )
        self.assertEqual(
            {
                "flag": "original-approved",
                "sub_type": "add",
                "approve": True,
            },
            client.actions[1][1],
        )
        prepared = storage.get_active_member_verification(
            str(ORIGINAL_GROUP_ID), "10001"
        )
        self.assertEqual("awaiting_join", prepared["state"])
        self.assertEqual("20002", prepared["inviter_qq"])

    def test原群无效答案和成员查询失败仍按原规则失败关闭(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))

        invalid_client = FakeOneBotClient({"99999"})
        invalid_event = add_request(
            comment="",
            group_id=ORIGINAL_GROUP_ID,
            flag="original-invalid",
        )
        asyncio.run(bot.on_event(invalid_client, cfg, invalid_event))
        self.assertEqual(
            ["get_group_member_list", "set_group_add_request"],
            [name for name, _ in invalid_client.actions],
        )
        self.assertIs(False, invalid_client.actions[-1][1]["approve"])
        self.assertIn("未填写有效", invalid_client.actions[-1][1]["reason"])

        failed_client = FakeOneBotClient({"20002", "99999"})
        failed_client.member_error = RuntimeError("成员接口暂时不可用")
        failed_event = add_request(
            group_id=ORIGINAL_GROUP_ID,
            flag="original-query-failed",
        )
        asyncio.run(bot.on_event(failed_client, cfg, failed_event))
        self.assertEqual(
            ["get_group_member_list"],
            [name for name, _ in failed_client.actions],
        )
        self.assertNotIn(
            f"{ORIGINAL_GROUP_ID}:original-query-failed",
            bot._handled_group_add_requests,
        )

    def test二群仅接受字段完整的真实add请求且绝不接受邀请机器人事件(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        missing_flag = add_request(group_id=SECOND_GROUP_ID)
        missing_flag.pop("flag")
        missing_applicant = add_request(group_id=SECOND_GROUP_ID)
        missing_applicant.pop("user_id")
        invalid_fields = (
            missing_flag,
            add_request(group_id=SECOND_GROUP_ID, flag=""),
            add_request(group_id=SECOND_GROUP_ID, flag="   "),
            add_request(group_id=SECOND_GROUP_ID, flag=12345),
            missing_applicant,
            add_request(group_id=SECOND_GROUP_ID, applicant="无效QQ"),
            add_request(group_id=SECOND_GROUP_ID, self_id=None),
        )
        for event in invalid_fields:
            with self.subTest(kind="invalid-field", event=event):
                client = FakeOneBotClient()
                self.assertTrue(
                    asyncio.run(bot.handle_group_add_auto_approval(client, cfg, event))
                )
                self.assertEqual([], client.actions)

        ignored = (
            add_request(group_id=SECOND_GROUP_ID, sub_type="invite"),
            add_request(group_id=999999999),
            {
                **add_request(group_id=SECOND_GROUP_ID),
                "post_type": "notice",
            },
            {
                **add_request(group_id=SECOND_GROUP_ID),
                "request_type": "friend",
            },
        )
        for event in ignored:
            with self.subTest(kind="ignored-structure", event=event):
                client = FakeOneBotClient()
                self.assertFalse(
                    asyncio.run(bot.handle_group_add_auto_approval(client, cfg, event))
                )
                self.assertEqual([], client.actions)

        disabled_or_out_of_scope = (
            approval_cfg((ORIGINAL_GROUP_ID,)),
            {
                "group_add_auto_approval_enabled": False,
                "group_add_auto_approval_groups": [SECOND_GROUP_ID],
            },
        )
        for disabled_cfg in disabled_or_out_of_scope:
            with self.subTest(kind="disabled-or-out-of-scope", cfg=disabled_cfg):
                client = FakeOneBotClient()
                self.assertFalse(
                    asyncio.run(
                        bot.handle_group_add_auto_approval(
                            client,
                            disabled_cfg,
                            add_request(group_id=SECOND_GROUP_ID),
                        )
                    )
                )
                self.assertEqual([], client.actions)

    def test二群错误登录身份事件在入口处失败关闭(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        cfg["_expected_self_id"] = "3215228879"
        event = add_request(group_id=SECOND_GROUP_ID, self_id=99999)
        client = FakeOneBotClient()

        asyncio.run(bot.on_event(client, cfg, event))

        self.assertEqual([], client.actions)
        self.assertNotIn(
            f"{SECOND_GROUP_ID}:request-1", bot._handled_group_add_requests
        )

    def test二群明确失败和超时不去重且恢复后可重试(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        failures = (
            bot.OneBotActionRejected("OneBot 明确拒绝"),
            TimeoutError("OneBot 审批超时"),
        )
        for index, failure in enumerate(failures):
            with self.subTest(failure=type(failure).__name__):
                flag = f"second-retry-{index}"
                event = add_request(group_id=SECOND_GROUP_ID, flag=flag)
                client = FakeOneBotClient()
                client.set_error_count = 1
                client.set_error = failure

                asyncio.run(bot.on_event(client, cfg, event))

                request_key = f"{SECOND_GROUP_ID}:{flag}"
                self.assertNotIn(request_key, bot._handled_group_add_requests)
                self.assertEqual(
                    ["set_group_add_request"],
                    [name for name, _ in client.actions],
                )

                asyncio.run(bot.on_event(client, cfg, event))

                self.assertIn(request_key, bot._handled_group_add_requests)
                self.assertEqual(
                    ["set_group_add_request", "set_group_add_request"],
                    [name for name, _ in client.actions],
                )

    def test二群取消不去重且成功重复事件保持幂等(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        event = add_request(
            group_id=SECOND_GROUP_ID,
            flag="second-cancelled-then-success",
        )
        client = FakeOneBotClient()
        client.set_error_count = 1
        client.set_error = asyncio.CancelledError()

        with self.assertRaises(asyncio.CancelledError):
            asyncio.run(bot.handle_group_add_auto_approval(client, cfg, event))
        request_key = f"{SECOND_GROUP_ID}:second-cancelled-then-success"
        self.assertNotIn(request_key, bot._handled_group_add_requests)

        asyncio.run(bot.on_event(client, cfg, event))
        asyncio.run(bot.on_event(client, cfg, event))

        self.assertIn(request_key, bot._handled_group_add_requests)
        self.assertEqual(
            ["set_group_add_request", "set_group_add_request"],
            [name for name, _ in client.actions],
        )
        for _, params in client.actions:
            self.assertEqual(
                {
                    "flag": "second-cancelled-then-success",
                    "sub_type": "add",
                    "approve": True,
                },
                params,
            )

    def test二群并发重复事件经生产分发锁只审批一次(self):
        cfg = approval_cfg((ORIGINAL_GROUP_ID, SECOND_GROUP_ID))
        event = add_request(group_id=SECOND_GROUP_ID, flag="second-concurrent")
        client = FakeOneBotClient()

        async def dispatch_duplicates():
            lock = asyncio.Lock()
            await asyncio.gather(
                bot._dispatch_event(lock, client, cfg, dict(event)),
                bot._dispatch_event(lock, client, cfg, dict(event)),
            )

        asyncio.run(dispatch_duplicates())

        self.assertEqual(
            ["set_group_add_request"],
            [name for name, _ in client.actions],
        )
        self.assertIn(
            f"{SECOND_GROUP_ID}:second-concurrent",
            bot._handled_group_add_requests,
        )

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

    def test群成员直接邀请在可靠入群通知中自动记录双方映射(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
            }
        )
        client = FakeOneBotClient({"20002", "99999"})
        request = add_request(
            applicant=20002,
            comment="",
            flag="member-direct-invite",
        )
        asyncio.run(bot.on_event(client, cfg, request))
        self.assertEqual(
            {"flag": "member-direct-invite", "sub_type": "add", "approve": True},
            client.actions[-1][1],
        )

        client.members.add("10001")
        notice = {
            "post_type": "notice",
            "notice_type": "group_increase",
            "sub_type": "invite",
            "group_id": 456,
            "user_id": 10001,
            "operator_id": 20002,
            "self_id": 99999,
            "time": 1010,
            "_grandumi_received_at": 1010,
        }
        asyncio.run(bot.on_event(client, cfg, notice))

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
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        self.assertNotIn(
            "get_group_member_list",
            [name for name, _ in client.actions[2:]],
        )

    def test自动审批成功后真实入群自动落账且重复通知幂等(self):
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
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        completed = storage.get_member_verification(prejoin["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertEqual(1010, completed["join_event_time"])
        self.assertEqual([], storage.get_member_verification_responses(prejoin["id"]))
        confirmation = client.actions[-1][1]["message"][1]["data"]["text"]
        self.assertIn("根据加群申请自动记录邀请人 QQ：20002", confirmation)
        self.assertIn("无需重复填写", confirmation)
        action_count = len(client.actions)
        asyncio.run(bot.on_event(client, cfg, notice))
        self.assertEqual(action_count, len(client.actions))
        self.assertEqual({"456"}, bot.member_verification_groups(cfg))

    def test审批响应失败遗留预备记录可由真实入群前向恢复(self):
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
        request = add_request(flag="recovery-request")
        request["time"] = 1000
        request["_grandumi_received_at"] = 1000
        asyncio.run(bot.on_event(client, cfg, request))
        prepared = storage.get_active_member_verification("456", "10001")
        self.assertEqual("approval_pending", prepared["state"])

        client.members.add("10001")
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
        asyncio.run(bot.on_event(client, cfg, notice))

        completed = storage.get_member_verification(prepared["id"])
        self.assertEqual("verified", completed["state"])
        self.assertEqual("20002", completed["inviter_qq"])
        self.assertIsNone(storage.get_active_member_verification("456", "10001"))
        action_count = len(client.actions)
        asyncio.run(bot.on_event(client, cfg, notice))
        self.assertEqual(action_count, len(client.actions))

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
                "10001",
                "你的加群审批结果尚未确认，暂时不能登记。请稍后由本人真正 "
                "@“释迦的助理”，并只发送“邀请人QQ：123456789”；"
                "若一直无法登记，请联系释迦大人核对。",
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
                "30003",
                "邀请人 QQ 只能由刚入群且处于待验证状态的新人本人登记，不能代填。"
                "若你就是刚入群的新人，请真正 @“释迦的助理”，并只发送"
                "“邀请人QQ：123456789”；若仍提示没有待登记验证，请联系释迦大人核对。",
            ),
            client.actions[-1][1]["message"],
        )

        for message_id, text in (
            (14, " 请问邀请人QQ要怎么登记？"),
            (15, " 邀请人QQ"),
            (16, " 我想登记邀请人QQ是20002"),
        ):
            with self.subTest(text=text):
                inquiry = dict(attempt)
                inquiry["user_id"] = 30003
                inquiry["message_id"] = message_id
                inquiry["message"] = [
                    {"type": "at", "data": {"qq": "99999"}},
                    {"type": "text", "data": {"text": text}},
                ]
                asyncio.run(bot.on_event(client, cfg, inquiry))
                reply = client.actions[-1][1]["message"][1]["data"]["text"]
                self.assertIn("只能由", reply)
                self.assertIn("新人本人登记", reply)
                self.assertIn("邀请人QQ：123456789", reply)
                self.assertIn("联系释迦大人", reply)
                self.assertIsNone(
                    storage.get_active_member_verification("456", "30003")
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

    def test登记格式错误会引导但其他消息段和非目标群不误消费(self):
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

        malformed_registration_messages = (
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
        )
        for index, message in enumerate(malformed_registration_messages, start=17):
            with self.subTest(message=message):
                event = dict(base)
                event["message_id"] = index
                event["message"] = message
                self.assertIsNone(bot.extract_strict_inviter_registration_qq(event))
                asyncio.run(bot.on_event(client, cfg, event))
                reply = client.actions[-1][1]["message"][1]["data"]["text"]
                self.assertIn("新人本人登记", reply)
                self.assertIn("邀请人QQ：123456789", reply)

        attachment = dict(base)
        attachment["message_id"] = 19
        attachment["message"] = [
            {"type": "at", "data": {"qq": "99999"}},
            {"type": "text", "data": {"text": "邀请人QQ:20002"}},
            {"type": "image", "data": {"file": "not-downloaded.jpg"}},
        ]
        self.assertIsNone(bot.extract_strict_inviter_registration_qq(attachment))
        action_count = len(client.actions)
        asyncio.run(bot.on_event(client, cfg, attachment))
        self.assertEqual(action_count, len(client.actions))
        self.assertEqual(
            bot.at_message("30003", "我只跟释迦大人聊天"),
            client.sent[-1]["params"]["message"],
        )

        other_group = dict(base)
        other_group["group_id"] = 789
        other_group["message_id"] = 20
        action_count = len(client.actions)
        asyncio.run(bot.on_event(client, cfg, other_group))
        self.assertEqual(action_count, len(client.actions))
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
