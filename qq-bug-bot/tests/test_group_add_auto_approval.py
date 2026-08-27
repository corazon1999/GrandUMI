# -*- coding: utf-8 -*-

import asyncio
import json
import sys
import unittest
from pathlib import Path

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot


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
        raise AssertionError(f"未预期的 OneBot 动作：{action}")


class GroupAddAutoApprovalTests(unittest.TestCase):
    def setUp(self):
        bot._handled_group_add_requests.clear()

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

    def test自动审批与旧入群后验证同时配置时不启动二次验证(self):
        cfg = approval_cfg()
        cfg.update(
            {
                "new_member_verification_enabled": True,
                "new_member_verification_groups": [456],
            }
        )
        notice = {
            "post_type": "notice",
            "notice_type": "group_increase",
            "sub_type": "approve",
            "group_id": 456,
            "user_id": 10001,
            "self_id": 99999,
        }
        client = FakeOneBotClient()
        self.assertFalse(
            asyncio.run(bot.handle_member_verification_notice(client, cfg, notice))
        )
        self.assertEqual(set(), bot.member_verification_groups(cfg))
        self.assertEqual([], client.actions)

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
