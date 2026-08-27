# -*- coding: utf-8 -*-

import asyncio
import os
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest import mock

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import qq_whitelist_sync as sync
import storage


GROUP_ID = "297542853"
GROUP_NAME = "GrandUMI测试群"


def make_config(**overrides):
    values = {
        "enabled": True,
        "group_id": GROUP_ID,
        "group_name": GROUP_NAME,
        "timezone_name": "Asia/Singapore",
        "endpoint": "http://127.0.0.1:8080/internal/qq-whitelist/sync",
        "secret": "0123456789abcdef0123456789abcdef",
        "minimum_member_count": 1,
        "maximum_shrink_percent": 25,
        "maximum_delay_seconds": 600,
        "update_retry_delays": (0, 0, 0),
        "notification_retry_delays": (0, 0, 0),
        "http_timeout_seconds": 5,
    }
    values.update(overrides)
    return sync.SyncConfig(**values)


class FakeOneBot:
    def __init__(self, members=("10001", "10002"), events=None):
        self.members = list(members)
        self.group_id = GROUP_ID
        self.group_name = GROUP_NAME
        self.reported_count = len(self.members)
        self.actions = []
        self.events = events if events is not None else []
        self.send_failures = []

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, params))
        if action == "get_group_info":
            self.events.append("group_info")
            return {
                "status": "ok",
                "retcode": 0,
                "data": {
                    "group_id": int(self.group_id),
                    "group_name": self.group_name,
                    "member_count": self.reported_count,
                },
            }
        if action == "get_group_member_list":
            self.events.append("members")
            return {
                "status": "ok",
                "retcode": 0,
                "data": [
                    {"group_id": int(self.group_id), "user_id": int(qq)}
                    for qq in self.members
                ],
            }
        if action == "send_group_msg":
            self.events.append("notify_attempt")
            failure = self.send_failures.pop(0) if self.send_failures else None
            if failure:
                raise failure
            self.events.append("notified")
            return {"status": "ok", "retcode": 0, "data": {"message_id": 1}}
        raise AssertionError(f"未预期 OneBot 动作：{action}")


class FakeGameClient:
    def __init__(self, events=None):
        self.events = events if events is not None else []
        self.sync_calls = []
        self.status_response = None
        self.sync_failures = []
        self.acks = []
        self.failure_reports = []
        self.failure_report_failures = []
        self.failure_report_response = None

    async def synchronize(self, payload):
        self.events.append("sync")
        self.sync_calls.append(payload)
        failure = self.sync_failures.pop(0) if self.sync_failures else None
        if failure:
            raise failure
        return committed_response(payload)

    async def status(self, operation_key, client_instance_id):
        self.events.append("status")
        return self.status_response

    async def acknowledge(self, operation_key, client_instance_id, version):
        self.events.append("ack")
        self.acks.append((operation_key, client_instance_id, version))
        return {
            "operationKey": operation_key,
            "notificationAcknowledgedAt": 123456,
        }

    async def report_failure(self, payload):
        self.events.append("failure_report")
        self.failure_reports.append(payload)
        failure = (
            self.failure_report_failures.pop(0)
            if self.failure_report_failures
            else None
        )
        if failure:
            raise failure
        if self.failure_report_response is not None:
            return self.failure_report_response
        return {
            "status": "failure_recorded",
            "committed": False,
            "operationKey": payload["operationKey"],
            "replayed": False,
            "update": {
                "outcome": "failure",
                "operationKey": payload["operationKey"],
                "scheduledHour": payload["scheduledHour"],
            },
        }


def committed_response(payload, owner=True):
    return {
        "operationKey": payload["operationKey"],
        "scheduledHour": payload["scheduledHour"],
        "groupId": payload["groupId"],
        "groupName": payload["groupName"],
        "version": 7,
        "memberCount": payload["reportedMemberCount"],
        "notificationOwner": owner,
        "notificationAcknowledgedAt": None,
    }


class QqWhitelistSyncTestCase(unittest.TestCase):
    def setUp(self):
        temp_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT") or None
        self.temp = tempfile.TemporaryDirectory(
            dir=temp_root, ignore_cleanup_errors=True
        )
        self.old_path = storage.DB_PATH
        storage.DB_PATH = os.path.join(self.temp.name, "feedback.db")
        storage.init_db()
        self.hour = sync.current_hour_epoch(
            datetime(2026, 8, 27, 7, 5, tzinfo=timezone.utc)
        )

    def tearDown(self):
        storage.DB_PATH = self.old_path
        self.temp.cleanup()


class SchedulerTests(QqWhitelistSyncTestCase):
    def test配置默认安全关闭且启用时必须显式授权目标群和环境密钥(self):
        disabled = sync.SyncConfig.from_bot_config({})
        self.assertFalse(disabled.enabled)
        enabled_config = {
            "qq_whitelist_sync_enabled": True,
            "qq_whitelist_sync_group_id": int(GROUP_ID),
            "qq_whitelist_sync_group_name": GROUP_NAME,
            "qq_whitelist_sync_timezone": "Asia/Singapore",
            "qq_whitelist_sync_endpoint": "https://direct.grand-umi.com/internal/qq-whitelist/sync",
            "qq_whitelist_sync_secret_env": "TEST_SYNC_SECRET",
            "allowed_groups": [int(GROUP_ID)],
        }
        with mock.patch.dict(
            os.environ,
            {"TEST_SYNC_SECRET": "0123456789abcdef0123456789abcdef"},
            clear=False,
        ):
            self.assertTrue(
                sync.SyncConfig.from_bot_config(enabled_config).enabled
            )
            with self.assertRaises(sync.SyncConfigurationError):
                sync.SyncConfig.from_bot_config(
                    {**enabled_config, "allowed_groups": []}
                )

    def test下个自然整点按UTC加8墙钟计算且不累计漂移(self):
        before = datetime(2026, 8, 27, 7, 59, 59, 999999, tzinfo=timezone.utc)
        target = sync.next_hour(before)
        self.assertEqual("2026-08-27T16:00:00+08:00", target.isoformat())
        exact = datetime(2026, 8, 27, 8, 0, 0, tzinfo=timezone.utc)
        self.assertEqual(
            "2026-08-27T17:00:00+08:00", sync.next_hour(exact).isoformat()
        )

    def test旧小时不补发也不访问QQ或游戏服务(self):
        onebot = FakeOneBot()
        game = FakeGameClient()
        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 601,
            )
        )
        self.assertEqual("stale", result["status"])
        self.assertEqual([], onebot.actions)
        self.assertEqual([], game.sync_calls)


class SnapshotValidationTests(QqWhitelistSyncTestCase):
    def test实时接口同时使用no_cache并核验群信息与成员数(self):
        onebot = FakeOneBot(("10001", "10002", "10003"))
        result = asyncio.run(sync.get_realtime_group_snapshot(onebot, make_config()))
        self.assertEqual(3, result["reportedMemberCount"])
        self.assertEqual(
            [True, True], [params["no_cache"] for _, params in onebot.actions]
        )

    def test空名单错误群名人数不符重复和异常缩水全部拒绝(self):
        cases = []
        empty = FakeOneBot(())
        cases.append(empty)
        wrong_name = FakeOneBot()
        wrong_name.group_name = "错误群"
        cases.append(wrong_name)
        wrong_count = FakeOneBot()
        wrong_count.reported_count = 99
        cases.append(wrong_count)
        duplicate = FakeOneBot(("10001", "10001"))
        cases.append(duplicate)
        for onebot in cases:
            with self.subTest(actions=onebot.members, name=onebot.group_name):
                with self.assertRaises(sync.SyncRejectedError):
                    asyncio.run(
                        sync.get_realtime_group_snapshot(
                            onebot, make_config(), previous_count=2
                        )
                    )

        shrink = FakeOneBot(tuple(str(10000 + index) for index in range(70)))
        with self.assertRaises(sync.SyncRejectedError):
            asyncio.run(
                sync.get_realtime_group_snapshot(
                    shrink, make_config(), previous_count=100
                )
            )


class EndToEndStateMachineTests(QqWhitelistSyncTestCase):
    def test成功提交后才通知且消息包含固定成功短语(self):
        events = []
        onebot = FakeOneBot(events=events)
        game = FakeGameClient(events=events)

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notified", result["status"])
        self.assertLess(events.index("sync"), events.index("notified"))
        sent = [params for action, params in onebot.actions if action == "send_group_msg"]
        self.assertEqual(1, len(sent))
        self.assertIn(sync.SUCCESS_PHRASE, sent[0]["message"])
        self.assertEqual(1, len(game.acks))

    def test游戏更新持续失败绝不通知且上一任务标为失败(self):
        onebot = FakeOneBot()
        game = FakeGameClient()
        game.sync_failures = [
            sync.SyncTransportError("写入失败一"),
            sync.SyncTransportError("写入失败二"),
            sync.SyncTransportError("写入失败三"),
        ]

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("failed", result["status"])
        self.assertFalse(
            any(action == "send_group_msg" for action, _ in onebot.actions)
        )
        row = storage.get_qq_whitelist_sync(
            sync.build_operation_key(GROUP_ID, self.hour)
        )
        self.assertEqual("failed", row["state"])
        self.assertIsNotNone(row["failure_reported_at"])
        self.assertEqual(1, len(game.failure_reports))

    def test失败报告暂时不可达会持久保留并在恢复后只补报不重跑更新(self):
        onebot = FakeOneBot()
        game = FakeGameClient()
        game.sync_failures = [
            sync.SyncTransportError("写入失败一"),
            sync.SyncTransportError("写入失败二"),
            sync.SyncTransportError("写入失败三"),
        ]
        game.failure_report_failures = [sync.SyncTransportError("报告端点不可达")]

        first = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )
        operation_key = sync.build_operation_key(GROUP_ID, self.hour)
        self.assertEqual("failed", first["status"])
        self.assertIsNone(
            storage.get_qq_whitelist_sync(operation_key)["failure_reported_at"]
        )

        recovery = asyncio.run(
            sync.recover_unreported_failure_reports(
                make_config(), game, now_fn=lambda: self.hour + 2
            )
        )
        self.assertEqual(
            {"recovered": 1, "pending": 0, "currentCommitted": False},
            recovery,
        )
        self.assertIsNotNone(
            storage.get_qq_whitelist_sync(operation_key)["failure_reported_at"]
        )
        self.assertEqual(3, len(game.sync_calls))
        self.assertEqual(2, len(game.failure_reports))

    def test失败回报发现服务端其实已提交时以前者纠正并继续唯一通知(self):
        onebot = FakeOneBot()
        game = FakeGameClient()
        game.sync_failures = [
            sync.SyncTransportError("提交响应丢失一"),
            sync.SyncTransportError("提交响应丢失二"),
            sync.SyncTransportError("提交响应丢失三"),
        ]
        operation_key = sync.build_operation_key(GROUP_ID, self.hour)
        game.failure_report_response = {
            "status": "committed",
            "committed": True,
            "operationKey": operation_key,
            "scheduledHour": self.hour,
            "groupId": GROUP_ID,
            "groupName": GROUP_NAME,
            "version": 7,
            "memberCount": 2,
            "notificationOwner": True,
            "notificationAcknowledgedAt": None,
        }

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notified", result["status"])
        self.assertEqual(1, len(game.failure_reports))
        self.assertEqual(
            1,
            sum(1 for action, _ in onebot.actions if action == "send_group_msg"),
        )

    def test延迟补报恢复当前小时提交后会继续通知且不重跑白名单(self):
        operation_key = sync.build_operation_key(GROUP_ID, self.hour)
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(
            now=self.hour
        )
        storage.prepare_qq_whitelist_sync(
            operation_key,
            self.hour,
            GROUP_ID,
            GROUP_NAME,
            instance_id,
            now=self.hour,
        )
        storage.fail_qq_whitelist_sync(
            operation_key, "提交响应与状态查询均中断", now=self.hour
        )
        game = FakeGameClient()
        game.failure_report_response = {
            "status": "committed",
            "committed": True,
            "operationKey": operation_key,
            "scheduledHour": self.hour,
            "groupId": GROUP_ID,
            "groupName": GROUP_NAME,
            "version": 7,
            "memberCount": 2,
            "notificationOwner": True,
            "notificationAcknowledgedAt": None,
        }

        recovery = asyncio.run(
            sync.recover_unreported_failure_reports(
                make_config(), game, now_fn=lambda: self.hour + 2
            )
        )
        self.assertTrue(recovery["currentCommitted"])
        onebot = FakeOneBot()
        result = asyncio.run(
            sync.recover_current_hour(
                onebot,
                make_config(),
                game,
                now_fn=lambda: self.hour + 3,
            )
        )
        self.assertEqual("notified", result["status"])
        self.assertEqual([], game.sync_calls)
        self.assertEqual(
            1,
            sum(1 for action, _ in onebot.actions if action == "send_group_msg"),
        )

    def test延迟补报恢复旧小时提交时只前向归档而不留下待通知状态(self):
        operation_key = sync.build_operation_key(GROUP_ID, self.hour)
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(
            now=self.hour
        )
        storage.prepare_qq_whitelist_sync(
            operation_key,
            self.hour,
            GROUP_ID,
            GROUP_NAME,
            instance_id,
            now=self.hour,
        )
        storage.fail_qq_whitelist_sync(
            operation_key, "旧小时提交响应丢失", now=self.hour
        )
        game = FakeGameClient()
        game.failure_report_response = {
            "status": "committed",
            "committed": True,
            "operationKey": operation_key,
            "scheduledHour": self.hour,
            "groupId": GROUP_ID,
            "groupName": GROUP_NAME,
            "version": 7,
            "memberCount": 2,
            "notificationOwner": True,
            "notificationAcknowledgedAt": None,
        }

        recovery = asyncio.run(
            sync.recover_unreported_failure_reports(
                make_config(), game, now_fn=lambda: self.hour + 3601
            )
        )

        self.assertFalse(recovery["currentCommitted"])
        self.assertEqual(
            "expired", storage.get_qq_whitelist_sync(operation_key)["state"]
        )

    def test拉取群快照超时会重试而不会终止整点任务(self):
        class TimeoutOnceOneBot(FakeOneBot):
            def __init__(self):
                super().__init__()
                self.info_attempts = 0

            async def call_action(self, action, params, timeout=20):
                if action == "get_group_info":
                    self.info_attempts += 1
                    if self.info_attempts == 1:
                        raise asyncio.TimeoutError()
                return await super().call_action(action, params, timeout)

        onebot = TimeoutOnceOneBot()
        game = FakeGameClient()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notified", result["status"])
        self.assertEqual(2, onebot.info_attempts)
        self.assertEqual(1, len(game.sync_calls))

    def test更新响应丢失时从服务端幂等状态恢复且只通知一次(self):
        class CommitThenTimeoutClient(FakeGameClient):
            def __init__(self):
                super().__init__()
                self.committed = None

            async def status(self, operation_key, client_instance_id):
                self.events.append("status")
                return self.committed

            async def synchronize(self, payload):
                self.events.append("sync")
                self.sync_calls.append(payload)
                self.committed = committed_response(payload)
                raise sync.SyncTransportError("提交成功后响应连接中断")

        onebot = FakeOneBot()
        game = CommitThenTimeoutClient()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notified", result["status"])
        self.assertEqual(1, len(game.sync_calls))
        self.assertEqual(
            1,
            sum(1 for action, _ in onebot.actions if action == "send_group_msg"),
        )

    def test群通知明确失败会有限重试且不重复写游戏版本(self):
        onebot = FakeOneBot()
        onebot.send_failures = [RuntimeError("OneBot 明确拒绝"), None]
        game = FakeGameClient()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notified", result["status"])
        self.assertEqual(1, len(game.sync_calls))
        self.assertEqual(
            2,
            sum(1 for action, _ in onebot.actions if action == "send_group_msg"),
        )
        row = storage.get_qq_whitelist_sync(
            sync.build_operation_key(GROUP_ID, self.hour)
        )
        self.assertEqual(2, row["notification_attempts"])

    def test通知超时属于未知送达状态不得自动重复发送(self):
        onebot = FakeOneBot()
        onebot.send_failures = [asyncio.TimeoutError(), None]
        game = FakeGameClient()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notification_uncertain", result["status"])
        self.assertEqual(
            1,
            sum(1 for action, _ in onebot.actions if action == "send_group_msg"),
        )

    def test重启从服务端幂等状态恢复通知且后续不会重复通知(self):
        operation_key = sync.build_operation_key(GROUP_ID, self.hour)
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(
            now=self.hour
        )
        storage.prepare_qq_whitelist_sync(
            operation_key,
            self.hour,
            GROUP_ID,
            GROUP_NAME,
            instance_id,
            now=self.hour,
        )
        onebot = FakeOneBot()
        game = FakeGameClient()
        game.status_response = committed_response(
            {
                "operationKey": operation_key,
                "scheduledHour": self.hour,
                "groupId": GROUP_ID,
                "groupName": GROUP_NAME,
                "reportedMemberCount": 2,
            }
        )

        first = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 1,
            )
        )
        second = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                game,
                now_fn=lambda: self.hour + 2,
            )
        )

        self.assertEqual("notified", first["status"])
        self.assertEqual("notified", second["status"])
        self.assertEqual(0, len(game.sync_calls))
        self.assertEqual(
            1,
            sum(1 for action, _ in onebot.actions if action == "send_group_msg"),
        )

    def test重启遇到发送中状态转为不确定而非重复通知(self):
        operation_key = sync.build_operation_key(GROUP_ID, self.hour)
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id()
        storage.prepare_qq_whitelist_sync(
            operation_key,
            self.hour,
            GROUP_ID,
            GROUP_NAME,
            instance_id,
        )
        storage.mark_qq_whitelist_sync_committed(
            operation_key, 7, 2, True, f"{sync.SUCCESS_PHRASE}（2 人，v7）"
        )
        storage.claim_qq_whitelist_sync_notification(operation_key)
        onebot = FakeOneBot()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.hour,
                FakeGameClient(),
                now_fn=lambda: self.hour + 1,
            )
        )

        self.assertEqual("notification_uncertain", result["status"])
        self.assertFalse(
            any(action == "send_group_msg" for action, _ in onebot.actions)
        )


if __name__ == "__main__":
    unittest.main()
