# -*- coding: utf-8 -*-

import asyncio
import json
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


GROUP_1 = "297542853"
GROUP_2 = "524996856"
GROUP_IDS = (GROUP_1, GROUP_2)
GROUP_1_NAME = "GrandUMI测试群"
GROUP_2_NAME = "GrandUMI二群（实时名称可变）"
BOT_1 = "3215228879"
BOT_2 = "3430685803"


class ExplicitOneBotRejection(RuntimeError):
    onebot_explicit_rejection = True


def make_config(**overrides):
    values = {
        "enabled": True,
        "groups": (
            sync.SyncGroup(GROUP_1, GROUP_1_NAME),
            sync.SyncGroup(GROUP_2, None),
        ),
        "timezone_name": "Asia/Singapore",
        "endpoint": "http://127.0.0.1:8080/internal/qq-whitelist/sync",
        "secret": "0123456789abcdef0123456789abcdef",
        "excluded_member_ids": (BOT_1, BOT_2),
        "interval_hours": 2,
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
    def __init__(self, groups=None, events=None):
        self.groups = groups or {
            GROUP_1: {
                "name": GROUP_1_NAME,
                "members": ["10001", "10002", BOT_1],
            },
            GROUP_2: {
                "name": GROUP_2_NAME,
                "members": ["10002", "10003", BOT_2],
            },
        }
        self.events = events if events is not None else []
        self.actions = []
        self.action_failures = {}
        self.send_failures = {GROUP_1: [], GROUP_2: []}

    async def call_action(self, action, params, timeout=20):
        self.actions.append((action, dict(params)))
        group_id = str(params.get("group_id") or "")
        failures = self.action_failures.get((action, group_id), [])
        if failures:
            failure = failures.pop(0)
            if failure:
                raise failure
        if action == "get_group_info":
            self.events.append(f"info:{group_id}")
            group = self.groups[group_id]
            return {
                "status": "ok",
                "retcode": 0,
                "data": {
                    "group_id": int(group.get("returned_group_id", group_id)),
                    "group_name": group["name"],
                    "member_count": group.get(
                        "reported_count", len(group["members"])
                    ),
                },
            }
        if action == "get_group_member_list":
            self.events.append(f"members:{group_id}")
            group = self.groups[group_id]
            if "raw_response" in group:
                return group["raw_response"]
            return {
                "status": "ok",
                "retcode": 0,
                "data": [
                    {
                        "group_id": int(item.get("group_id", group_id)),
                        "user_id": item.get("user_id"),
                    }
                    if isinstance(item, dict)
                    else {"group_id": int(group_id), "user_id": int(item)}
                    for item in group["members"]
                ],
            }
        if action == "send_group_msg":
            self.events.append(f"notify_attempt:{group_id}")
            failures = self.send_failures.setdefault(group_id, [])
            failure = failures.pop(0) if failures else None
            if failure:
                raise failure
            self.events.append(f"notified:{group_id}")
            return {
                "status": "ok",
                "retcode": 0,
                "data": {"message_id": len(self.actions)},
            }
        raise AssertionError(f"未预期 OneBot 动作：{action}")


class FakeGameClient:
    def __init__(self, events=None):
        self.events = events if events is not None else []
        self.sync_calls = []
        self.status_calls = []
        self.sync_failures = []
        self.committed = {}
        self.next_version = 7
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
        existing = self.committed.get(payload["operationKey"])
        if existing:
            return dict(existing)
        response = committed_response(payload, version=self.next_version)
        self.next_version += 1
        self.committed[payload["operationKey"]] = dict(response)
        return response

    async def status(self, operation_key, client_instance_id):
        self.events.append("status")
        self.status_calls.append((operation_key, client_instance_id))
        response = self.committed.get(operation_key)
        return dict(response) if response else None

    async def acknowledge(self, operation_key, client_instance_id, version):
        self.events.append("ack")
        self.acks.append((operation_key, client_instance_id, version))
        response = dict(self.committed[operation_key])
        response.update(
            {
                "notificationOwner": False,
                "notificationAcknowledgedAt": 123456,
            }
        )
        return response

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
        committed = self.committed.get(payload["operationKey"])
        if committed:
            return {"status": "committed", "committed": True, **committed}
        return {
            "status": "failure_recorded",
            "committed": False,
            "protocolVersion": 2,
            "operationKey": payload["operationKey"],
            "sourceGroupIds": list(GROUP_IDS),
            "replayed": False,
            "update": {
                "outcome": "failure",
                "operationKey": payload["operationKey"],
                "scheduledHour": payload["scheduledHour"],
            },
        }


def committed_response(payload, owner=True, version=7):
    return {
        "protocolVersion": 2,
        "operationKey": payload["operationKey"],
        "scheduledHour": payload["scheduledHour"],
        "groupId": GROUP_1,
        "groupName": sync.SOURCE_SET_KEY,
        "sourceGroupIds": list(GROUP_IDS),
        "version": version,
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
        self.slot = sync.scheduled_slot_epoch(
            datetime(2026, 9, 4, 8, 5, tzinfo=timezone.utc)
        )

    def tearDown(self):
        storage.DB_PATH = self.old_path
        self.temp.cleanup()

    def prepare_bound_row(self, config=None):
        config = config or make_config()
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(
            now=self.slot
        )
        storage.prepare_qq_whitelist_sync_slot(
            sync.build_slot_key(config.group_ids, self.slot),
            self.slot,
            config.source_set_key,
            config.group_id,
            instance_id,
            now=self.slot,
        )
        snapshot = asyncio.run(
            sync.get_combined_group_snapshot(FakeOneBot(), config)
        )
        operation_key = sync.build_operation_key(
            config.group_ids, self.slot, snapshot["members"]
        )
        row = storage.bind_qq_whitelist_sync_snapshot(
            config.source_set_key,
            self.slot,
            operation_key,
            json.dumps(snapshot["sourceGroups"], ensure_ascii=False),
            snapshot["snapshotSha256"],
            json.dumps(snapshot["members"], ensure_ascii=False),
            now=self.slot,
        )
        return row


class SchedulerAndConfigurationTests(QqWhitelistSyncTestCase):
    def test旧单群私密配置安全迁移为固定双群且过滤助理账号(self):
        cfg = {
            "qq_whitelist_sync_enabled": True,
            "qq_whitelist_sync_group_id": int(GROUP_1),
            "qq_whitelist_sync_group_name": GROUP_1_NAME,
            "qq_whitelist_sync_timezone": "Asia/Singapore",
            "qq_whitelist_sync_endpoint": "https://direct.grand-umi.com/internal/qq-whitelist/sync",
            "qq_whitelist_sync_secret_env": "TEST_SYNC_SECRET",
            "allowed_groups": [int(GROUP_1), int(GROUP_2)],
            "assistant_connections": [
                {"expected_self_id": BOT_1},
                {"expected_self_id": BOT_2},
            ],
        }
        with mock.patch.dict(
            os.environ,
            {"TEST_SYNC_SECRET": "0123456789abcdef0123456789abcdef"},
            clear=False,
        ):
            parsed = sync.SyncConfig.from_bot_config(cfg)
            self.assertEqual(GROUP_IDS, parsed.group_ids)
            self.assertEqual((BOT_1, BOT_2), parsed.excluded_member_ids)
            self.assertEqual(2, parsed.interval_hours)
            with self.assertRaises(sync.SyncConfigurationError):
                sync.SyncConfig.from_bot_config(
                    {**cfg, "allowed_groups": [int(GROUP_1)]}
                )
            with self.assertRaises(sync.SyncConfigurationError):
                sync.SyncConfig.from_bot_config(
                    {**cfg, "qq_whitelist_sync_interval_hours": 1}
                )

    def test双群配置必须固定顺序且默认安全关闭(self):
        self.assertFalse(sync.SyncConfig.from_bot_config({}).enabled)
        base = {
            "qq_whitelist_sync_enabled": True,
            "qq_whitelist_sync_group_id": int(GROUP_1),
            "qq_whitelist_sync_group_name": GROUP_1_NAME,
            "qq_whitelist_sync_group_ids": [int(GROUP_2), int(GROUP_1)],
            "qq_whitelist_sync_endpoint": "https://direct.grand-umi.com/internal/qq-whitelist/sync",
            "qq_whitelist_sync_secret_env": "TEST_SYNC_SECRET",
            "allowed_groups": [int(GROUP_1), int(GROUP_2)],
        }
        with mock.patch.dict(
            os.environ,
            {"TEST_SYNC_SECRET": "0123456789abcdef0123456789abcdef"},
            clear=False,
        ):
            with self.assertRaises(sync.SyncConfigurationError):
                sync.SyncConfig.from_bot_config(base)

    def test两小时时隙按UTC加8墙钟计算且跨日无漂移(self):
        before = datetime(2026, 9, 4, 17, 59, 59, tzinfo=timezone.utc)
        self.assertEqual(
            "2026-09-05T00:00:00+08:00",
            datetime.fromtimestamp(
                sync.scheduled_slot_epoch(before), tz=sync.BUSINESS_TIMEZONE
            ).isoformat(),
        )
        self.assertEqual(
            "2026-09-05T02:00:00+08:00",
            sync.next_slot(before).isoformat(),
        )
        exact = datetime(2026, 9, 4, 18, 0, tzinfo=timezone.utc)
        self.assertEqual(
            "2026-09-05T04:00:00+08:00",
            sync.next_slot(exact).isoformat(),
        )

    def test过期或奇数整点不访问QQ和游戏服务(self):
        for scheduled, now in (
            (self.slot, self.slot + 601),
            (self.slot + 3600, self.slot + 3601),
        ):
            onebot = FakeOneBot()
            game = FakeGameClient()
            result = asyncio.run(
                sync.execute_sync_hour(
                    onebot,
                    make_config(),
                    scheduled,
                    game,
                    now_fn=lambda now=now: now,
                )
            )
            self.assertEqual("stale", result["status"])
            self.assertEqual([], onebot.actions)
            self.assertEqual([], game.sync_calls)

    def test窗口内重启只恢复已持久化时隙且窗口外将其过期(self):
        config = make_config()
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(
            now=self.slot
        )
        storage.prepare_qq_whitelist_sync_slot(
            sync.build_slot_key(config.group_ids, self.slot),
            self.slot,
            config.source_set_key,
            config.group_id,
            instance_id,
            now=self.slot,
        )
        game = FakeGameClient()
        result = asyncio.run(
            sync.recover_current_slot(
                FakeOneBot(),
                config,
                game,
                now_fn=lambda: self.slot + 5,
            )
        )
        self.assertEqual("notified", result["status"])
        self.assertEqual(1, len(game.sync_calls))

        next_slot = self.slot + 7200
        storage.prepare_qq_whitelist_sync_slot(
            sync.build_slot_key(config.group_ids, next_slot),
            next_slot,
            config.source_set_key,
            config.group_id,
            instance_id,
            now=next_slot,
        )
        stale = asyncio.run(
            sync.recover_current_slot(
                FakeOneBot(),
                config,
                game,
                now_fn=lambda: next_slot + 601,
            )
        )
        self.assertEqual("nothing_to_recover", stale["status"])
        self.assertEqual(
            "expired",
            storage.get_qq_whitelist_sync_for_slot(
                config.source_set_key, next_slot
            )["state"],
        )

    def test同一时隙重放不重复写且下一时隙使用新操作键(self):
        onebot = FakeOneBot()
        game = FakeGameClient()
        config = make_config()
        first = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                config,
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        replay = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                config,
                self.slot,
                game,
                now_fn=lambda: self.slot + 2,
            )
        )
        following = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                config,
                self.slot + 7200,
                game,
                now_fn=lambda: self.slot + 7201,
            )
        )
        self.assertEqual(("notified", "notified", "notified"), (
            first["status"], replay["status"], following["status"]
        ))
        self.assertEqual(2, len(game.sync_calls))
        self.assertNotEqual(
            game.sync_calls[0]["operationKey"],
            game.sync_calls[1]["operationKey"],
        )

    def test从旧版单群数据库切换时同一时隙安全跳过而不冲突(self):
        instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(
            now=self.slot
        )
        storage.prepare_qq_whitelist_sync(
            sync.build_operation_key(GROUP_1, self.slot),
            self.slot,
            GROUP_1,
            GROUP_1_NAME,
            instance_id,
            now=self.slot,
        )
        onebot = FakeOneBot()
        game = FakeGameClient()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )

        self.assertEqual("legacy_slot_already_used", result["status"])
        self.assertEqual([], onebot.actions)
        self.assertEqual([], game.sync_calls)


class SnapshotValidationTests(QqWhitelistSyncTestCase):
    def test两个群完整采样后全局去重并过滤机器人且确定性排序(self):
        result = asyncio.run(
            sync.get_combined_group_snapshot(FakeOneBot(), make_config())
        )
        self.assertEqual(["10001", "10002", "10003"], result["members"])
        self.assertEqual(3, result["reportedMemberCount"])
        self.assertEqual(
            [3, 3],
            [item["reportedMemberCount"] for item in result["sourceGroups"]],
        )
        self.assertEqual(
            [1, 1],
            [item["excludedMemberCount"] for item in result["sourceGroups"]],
        )
        self.assertEqual(
            sync.snapshot_sha256(result["members"]),
            result["snapshotSha256"],
        )

    def test第二个群快照失败时绝不用第一个群的部分并集覆盖权威库(self):
        onebot = FakeOneBot()
        onebot.action_failures[("get_group_info", GROUP_2)] = [
            asyncio.TimeoutError(),
            asyncio.TimeoutError(),
            asyncio.TimeoutError(),
        ]
        game = FakeGameClient()
        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        self.assertEqual("failed", result["status"])
        self.assertEqual([], game.sync_calls)
        self.assertFalse(
            any(action == "send_group_msg" for action, _ in onebot.actions)
        )
        row = storage.get_qq_whitelist_sync_for_slot(
            sync.SOURCE_SET_KEY, self.slot
        )
        self.assertTrue(row["operation_key"].endswith(":capture-failed"))
        self.assertIsNone(row["snapshot_members_json"])

    def test错误群重复无效QQ人数不稳和异常缩水全部失败关闭(self):
        cases = []
        wrong_group = FakeOneBot()
        wrong_group.groups[GROUP_2]["returned_group_id"] = GROUP_1
        cases.append(wrong_group)
        duplicate = FakeOneBot()
        duplicate.groups[GROUP_2]["members"] = ["10003", "10003"]
        cases.append(duplicate)
        invalid = FakeOneBot()
        invalid.groups[GROUP_2]["members"] = [
            {"user_id": "bad"},
            "10003",
        ]
        cases.append(invalid)
        wrong_count = FakeOneBot()
        wrong_count.groups[GROUP_2]["reported_count"] = 99
        cases.append(wrong_count)
        for onebot in cases:
            with self.subTest(actions=onebot.actions):
                with self.assertRaises(sync.SyncRejectedError):
                    asyncio.run(
                        sync.get_combined_group_snapshot(onebot, make_config())
                    )

        many = [str(10000 + index) for index in range(100)]
        small = FakeOneBot(
            {
                GROUP_1: {"name": GROUP_1_NAME, "members": many[:35]},
                GROUP_2: {"name": GROUP_2_NAME, "members": many[35:70]},
            }
        )
        with self.assertRaises(sync.SyncRejectedError):
            asyncio.run(
                sync.get_combined_group_snapshot(
                    small, make_config(), previous_count=100
                )
            )


class EndToEndStateMachineTests(QqWhitelistSyncTestCase):
    def test权威同步明确成功后才分别通知两个群且内容为去重总人数(self):
        events = []
        onebot = FakeOneBot(events=events)
        game = FakeGameClient(events=events)
        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        self.assertEqual("notified", result["status"])
        self.assertLess(events.index("sync"), events.index(f"notified:{GROUP_1}"))
        sent = [
            params
            for action, params in onebot.actions
            if action == "send_group_msg"
        ]
        self.assertEqual([int(GROUP_1), int(GROUP_2)], [x["group_id"] for x in sent])
        self.assertTrue(all("两个群去重后共 3 人" in x["message"] for x in sent))
        self.assertEqual(1, len(game.acks))
        payload = game.sync_calls[0]
        self.assertEqual(2, payload["protocolVersion"])
        self.assertEqual(list(GROUP_IDS), [x["groupId"] for x in payload["sourceGroups"]])
        self.assertTrue(payload["operationKey"].endswith(payload["operationKey"].split(":")[-1]))
        self.assertIn(sync.snapshot_sha256(payload["members"]), payload["operationKey"])

    def test游戏响应身份类型或人数与本地快照不一致时绝不通知(self):
        mutations = (
            {"protocolVersion": 1},
            {"groupId": GROUP_2},
            {"groupName": "错误集合"},
            {"memberCount": 99},
            {"notificationOwner": "true"},
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                row = self.prepare_bound_row()
                payload = sync._payload_from_stored_snapshot(row, make_config())
                response = {**committed_response(payload), **mutation}
                with self.assertRaises(sync.SyncTransportError):
                    sync._persist_committed_response(
                        response,
                        row["operation_key"],
                        make_config(),
                        self.slot,
                        self.slot,
                    )
                self.assertEqual(
                    "started",
                    storage.get_qq_whitelist_sync(row["operation_key"])["state"],
                )

    def test游戏更新持续失败绝不通知且失败报告保持双群身份(self):
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
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        self.assertEqual("failed", result["status"])
        self.assertEqual(3, len(game.sync_calls))
        self.assertFalse(
            any(action == "send_group_msg" for action, _ in onebot.actions)
        )
        self.assertEqual(list(GROUP_IDS), game.failure_reports[0]["sourceGroupIds"])

    def test提交响应丢失后按完整操作键恢复且不重复写权威版本(self):
        class CommitThenTimeout(FakeGameClient):
            async def synchronize(self, payload):
                response = await super().synchronize(payload)
                raise sync.SyncTransportError("提交成功后响应丢失")

        game = CommitThenTimeout()
        onebot = FakeOneBot()
        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        self.assertEqual("notified", result["status"])
        self.assertEqual(1, len(game.sync_calls))
        self.assertEqual(1, len(game.committed))
        self.assertEqual(2, sum(
            action == "send_group_msg" for action, _ in onebot.actions
        ))

    def test重启从服务端提交状态恢复双群通知且再次恢复不重发(self):
        row = self.prepare_bound_row()
        payload = sync._payload_from_stored_snapshot(row, make_config())
        game = FakeGameClient()
        game.committed[row["operation_key"]] = committed_response(payload)
        onebot = FakeOneBot()
        first = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        second = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 2,
            )
        )
        self.assertEqual(("notified", "notified"), (first["status"], second["status"]))
        self.assertEqual([], game.sync_calls)
        self.assertEqual(2, sum(
            action == "send_group_msg" for action, _ in onebot.actions
        ))

    def test每群通知状态独立且一个群明确失败不会让另一个重复发送(self):
        onebot = FakeOneBot()
        onebot.send_failures[GROUP_1] = [
            ExplicitOneBotRejection("OneBot 明确拒绝"),
            ExplicitOneBotRejection("OneBot 明确拒绝"),
            ExplicitOneBotRejection("OneBot 明确拒绝"),
        ]
        game = FakeGameClient()
        first = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        self.assertEqual("notification_failed", first["status"])
        operation_key = game.sync_calls[0]["operationKey"]
        states = {
            item["group_id"]: item["state"]
            for item in storage.list_qq_whitelist_sync_notifications(operation_key)
        }
        self.assertEqual({GROUP_1: "pending", GROUP_2: "sent"}, states)
        second = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 2,
            )
        )
        self.assertEqual("notified", second["status"])
        sent_groups = [
            str(params["group_id"])
            for action, params in onebot.actions
            if action == "send_group_msg" and params["group_id"] == int(GROUP_2)
        ]
        self.assertEqual([GROUP_2], sent_groups)
        self.assertEqual(1, len(game.sync_calls))

    def test一个群通知未知不盲目重发但另一个群仍独立通知(self):
        onebot = FakeOneBot()
        onebot.send_failures[GROUP_1] = [asyncio.TimeoutError()]
        game = FakeGameClient()
        first = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        attempts_after_first = sum(
            action == "send_group_msg" for action, _ in onebot.actions
        )
        second = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 2,
            )
        )
        self.assertEqual("notification_uncertain", first["status"])
        self.assertEqual("notification_uncertain", second["status"])
        self.assertEqual(2, attempts_after_first)
        self.assertEqual(attempts_after_first, sum(
            action == "send_group_msg" for action, _ in onebot.actions
        ))
        self.assertEqual([], game.acks)

    def test一个群通知发生非明确RuntimeError也冻结未知且继续另一个群(self):
        onebot = FakeOneBot()
        onebot.send_failures[GROUP_1] = [RuntimeError("发送结果未得到确认")]
        game = FakeGameClient()

        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )

        self.assertEqual("notification_uncertain", result["status"])
        operation_key = game.sync_calls[0]["operationKey"]
        states = {
            item["group_id"]: item["state"]
            for item in storage.list_qq_whitelist_sync_notifications(operation_key)
        }
        self.assertEqual({GROUP_1: "uncertain", GROUP_2: "sent"}, states)

    def test进程重启把旧发送中群冻结为未知并继续通知另一个群(self):
        row = self.prepare_bound_row()
        payload = sync._payload_from_stored_snapshot(row, make_config())
        game = FakeGameClient()
        game.committed[row["operation_key"]] = committed_response(payload)
        sync._persist_committed_response(
            game.committed[row["operation_key"]],
            row["operation_key"],
            make_config(),
            self.slot,
            self.slot,
        )
        storage.claim_qq_whitelist_sync_group_notification(
            row["operation_key"], GROUP_1, "旧进程"
        )
        onebot = FakeOneBot()
        result = asyncio.run(
            sync.execute_sync_hour(
                onebot,
                make_config(),
                self.slot,
                game,
                now_fn=lambda: self.slot + 1,
            )
        )
        self.assertEqual("notification_uncertain", result["status"])
        sent = [
            params["group_id"]
            for action, params in onebot.actions
            if action == "send_group_msg"
        ]
        self.assertEqual([int(GROUP_2)], sent)

    def test并发执行复用首份持久化快照且每群最多通知一次(self):
        class YieldingGame(FakeGameClient):
            async def synchronize(self, payload):
                await asyncio.sleep(0)
                return await super().synchronize(payload)

        async def run_both():
            onebot = FakeOneBot()
            game = YieldingGame()
            results = await asyncio.gather(
                sync.execute_sync_hour(
                    onebot,
                    make_config(),
                    self.slot,
                    game,
                    now_fn=lambda: self.slot + 1,
                ),
                sync.execute_sync_hour(
                    onebot,
                    make_config(),
                    self.slot,
                    game,
                    now_fn=lambda: self.slot + 1,
                ),
            )
            return onebot, game, results

        onebot, game, results = asyncio.run(run_both())
        self.assertTrue(all(result["status"] in {
            "notified", "committed", "notification_failed"
        } for result in results))
        self.assertEqual(1, len(game.committed))
        sent = [
            params["group_id"]
            for action, params in onebot.actions
            if action == "send_group_msg"
        ]
        self.assertEqual(1, sent.count(int(GROUP_1)))
        self.assertEqual(1, sent.count(int(GROUP_2)))


if __name__ == "__main__":
    unittest.main()
