# -*- coding: utf-8 -*-

import asyncio
import contextlib
import hashlib
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

BOT_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = BOT_DIR.parent
sys.path.insert(0, str(BOT_DIR))

import export_live_qq_whitelist as live_export


GROUP_1 = "297542853"
GROUP_2 = "524996856"
GROUP_IDS = (GROUP_1, GROUP_2)
GROUP_NAMES = {
    GROUP_1: "GrandUMI测试群",
    GROUP_2: "GrandUMI 2群",
}
BOT_QQ = "90001"


def group_info(count=2, group_id=GROUP_1, group_name=None):
    return {
        "status": "ok",
        "retcode": 0,
        "data": {
            "group_id": int(group_id),
            "group_name": group_name or GROUP_NAMES[group_id],
            "member_count": count,
        },
    }


def member_list(members=("10001", "10002"), group_id=GROUP_1):
    return {
        "status": "ok",
        "retcode": 0,
        "data": [
            {"group_id": int(group_id), "user_id": int(qq)}
            for qq in members
        ],
    }


def stable_group_responses(group_id, members=("10001", "10002")):
    count = len(members)
    return [
        group_info(count, group_id),
        member_list(members, group_id),
        group_info(count, group_id),
    ]


class FakeOneBot:
    def __init__(self, responses):
        self.responses = list(responses)
        self.actions = []

    async def call_action(self, action, params):
        self.actions.append((action, params))
        if not self.responses:
            raise AssertionError("测试响应不足")
        return self.responses.pop(0)


class LiveExportValidationTests(unittest.TestCase):
    def test固定双群分别稳定采样排除机器人并生成去重并集(self):
        responses = stable_group_responses(
            GROUP_1, ("10001", "10002", BOT_QQ)
        ) + stable_group_responses(
            GROUP_2, ("10002", "10003", BOT_QQ)
        )
        onebot = FakeOneBot(responses)
        result = asyncio.run(
            live_export.collect_stable_snapshot(
                onebot, [BOT_QQ], retry_delay_seconds=0
            )
        )

        self.assertEqual(["10001", "10002", "10003"], result["members"])
        self.assertEqual(list(GROUP_IDS), result["source"]["group_ids"])
        self.assertEqual([BOT_QQ], result["source"]["excluded_bot_qqs"])
        self.assertEqual(
            [GROUP_1, GROUP_2],
            [group["group_id"] for group in result["source"]["groups"]],
        )
        self.assertEqual(
            [3, 3],
            [group["api_raw_count"] for group in result["source"]["groups"]],
        )
        self.assertEqual(
            [2, 2],
            [group["eligible_count"] for group in result["source"]["groups"]],
        )
        self.assertEqual(6, result["validation"]["original_count"])
        self.assertEqual(4, result["validation"]["eligible_count"])
        self.assertEqual(3, result["validation"]["unique_count"])
        self.assertEqual(1, result["validation"]["duplicate_count"])
        self.assertEqual(2, result["validation"]["excluded_bot_count"])
        self.assertEqual(
            live_export._snapshot_sha256(result["members"]),
            result["snapshot_sha256"],
        )
        self.assertEqual(
            [
                "get_group_info",
                "get_group_member_list",
                "get_group_info",
            ] * 2,
            [action for action, _ in onebot.actions],
        )
        self.assertEqual(
            [True] * 6,
            [params["no_cache"] for _, params in onebot.actions],
        )
        self.assertEqual(
            [GROUP_1] * 3 + [GROUP_2] * 3,
            [str(params["group_id"]) for _, params in onebot.actions],
        )

    def test任一群人数变化只做有限重试且不交付部分结果(self):
        responses = stable_group_responses(GROUP_1)
        for _ in range(live_export.MAX_STABILITY_ATTEMPTS):
            responses.extend(
                [
                    group_info(2, GROUP_2),
                    member_list(("10001", "10002", "10003"), GROUP_2),
                    group_info(3, GROUP_2),
                ]
            )
        onebot = FakeOneBot(responses)

        with self.assertRaisesRegex(
            live_export.ExportError, f"群 {GROUP_2} 连续 3 次"
        ):
            asyncio.run(
                live_export.collect_stable_snapshot(
                    onebot, retry_delay_seconds=0
                )
            )
        self.assertEqual(12, len(onebot.actions))

    def test群号群名串群重复空值无效QQ和群名漂移全部拒绝(self):
        group = live_export.TARGET_GROUPS[0]
        cases = [
            [group_info(group_id=GROUP_2), member_list(), group_info()],
            [group_info(group_name="其他群"), member_list(), group_info()],
            [group_info(), member_list(group_id=GROUP_2), group_info()],
            [group_info(), member_list(("10001", "10001")), group_info()],
            [
                group_info(),
                {"data": [{"group_id": int(GROUP_1), "user_id": ""}]},
                group_info(),
            ],
            [
                group_info(),
                {"data": [{"group_id": int(GROUP_1), "user_id": "１２３４５"}]},
                group_info(),
            ],
            [
                group_info(group_name=GROUP_NAMES[GROUP_1]),
                member_list(),
                group_info(group_name="新群名"),
            ],
        ]
        for responses in cases:
            with self.subTest(responses=responses):
                with self.assertRaises(live_export.ExportError):
                    asyncio.run(
                        live_export.collect_snapshot(FakeOneBot(responses), group)
                    )

    def test配置令牌只用于连接并按同步口径汇总机器人QQ(self):
        config = {
            "ws_url": "ws://napcat:3001",
            "access_token": "仅用于单元测试",
            "qq_whitelist_sync_excluded_qqs": [BOT_QQ],
            "assistant_connections": [
                {"expected_self_id": "3215228879"},
                {"expected_self_id": "184689168"},
            ],
            "expected_self_id": "3215228879",
        }
        with mock.patch(
            "builtins.open", mock.mock_open(read_data=json.dumps(config))
        ):
            url, excluded = live_export._load_export_config()
        self.assertIn("access_token=", url)
        self.assertNotIn("仅用于单元测试", url)
        self.assertEqual(("184689168", "3215228879", BOT_QQ), excluded)
        with self.assertRaises(live_export.ExportError):
            live_export._build_websocket_url(
                {
                    "ws_url": "ws://napcat:3001?access_token=已有值",
                    "access_token": "",
                }
            )

    def test远端服务失败时标准输出为空且返回非零(self):
        standard_output = io.StringIO()
        standard_error = io.StringIO()
        with mock.patch.object(
            live_export,
            "export_live_whitelist",
            new=mock.AsyncMock(
                side_effect=live_export.ExportError("NapCat 未运行")
            ),
        ):
            with contextlib.redirect_stdout(
                standard_output
            ), contextlib.redirect_stderr(standard_error):
                exit_code = live_export.main()

        self.assertEqual(1, exit_code)
        self.assertEqual("", standard_output.getvalue())
        self.assertIn("导出失败", standard_error.getvalue())
        self.assertNotIn("members", standard_error.getvalue())


class OneClickEntryStaticTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cmd_path = REPO_ROOT / "一键导出QQ白名单.cmd"
        cls.ps_path = BOT_DIR / "export-live-qq-whitelist.ps1"
        cls.remote_path = BOT_DIR / "export_live_qq_whitelist.py"
        cls.verifier_path = BOT_DIR / "verify_qq_whitelist_export.mjs"
        cls.cmd = cls.cmd_path.read_text(encoding="utf-8-sig")
        cls.ps = cls.ps_path.read_text(encoding="utf-8-sig")
        cls.remote = cls.remote_path.read_text(encoding="utf-8")
        cls.verifier = cls.verifier_path.read_text(encoding="utf-8")

    def test根目录入口明显且双击后保留窗口与退出码(self):
        self.assertTrue(self.cmd_path.is_file())
        self.assertIn("export-live-qq-whitelist.ps1", self.cmd)
        self.assertRegex(self.cmd.lower(), r"\bpause\b")
        self.assertIn("exit /b %GRANDUMI_EXPORT_EXIT%", self.cmd)

    def test本地中转只能通过统一帮助脚本进入E盘并在finally清理(self):
        self.assertIn("ops\\windows\\GrandUmiTemp.ps1", self.ps)
        self.assertIn(
            "Get-GrandUmiTempDirectory -Category 'QqWhitelistExport'", self.ps
        )
        self.assertIn("E:\\GrandUMI-Temp\\", self.ps)
        self.assertRegex(
            self.ps,
            r"finally\s*\{[\s\S]*Remove-Item -LiteralPath \$tempFile",
        )
        for forbidden in (
            "GetTempPath",
            "$env:TEMP",
            "$env:TMP",
            "%TEMP%",
            "C:\\",
        ):
            self.assertNotIn(forbidden, self.ps)

    def test远端通过标准输入执行且不留下远端临时文件(self):
        self.assertIn("docker compose exec -T bug-bot python -c", self.ps)
        self.assertIn("sys.stdin.buffer.read()", self.ps)
        self.assertIn("base64.b64decode", self.ps)
        self.assertIn("source hash mismatch", self.ps)
        self.assertIn("$standardInput.BaseStream.WriteAsync", self.ps)
        self.assertIn("-InputBytes $remoteTransport.PayloadBytes", self.ps)
        self.assertIn("SSH 连接失败时会在源码仍写入管道期间提前退出", self.ps)
        self.assertIn("StandardError = $standardErrorTask.GetAwaiter().GetResult()", self.ps)
        self.assertNotIn("/tmp/", self.ps + self.remote)
        self.assertNotIn("tempfile", self.remote)
        self.assertNotIn("send_group_msg", self.remote)

    def test传输自检证明WindowsPowerShell子进程收到的源码逐字节一致(self):
        result = subprocess.run(
            [
                "powershell.exe",
                "-NoLogo",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(self.ps_path),
                "-TransportSelfTest",
            ],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
            timeout=45,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        payload = json.loads(result.stdout.strip())
        self.assertTrue(payload["payloadAscii"])
        self.assertGreaterEqual(payload["powershellMajor"], 5)
        self.assertEqual(
            payload["sourceSha256"], payload["roundTripSha256"]
        )
        self.assertEqual(
            payload["sourceSha256"], payload["childDecodedSha256"]
        )
        self.assertGreater(payload["sourceByteLength"], 0)

    def test命令行入口允许自动化保留退出码且默认双击仍暂停(self):
        self.assertIn('if /I "%~1"=="-NoPause" goto :finished', self.cmd)
        self.assertIn(":finished", self.cmd)

    def test目标群不可由一键脚本参数改写且远端命令输入受限(self):
        top_level_parameters = self.ps.split("Set-StrictMode", 1)[0]
        self.assertNotRegex(top_level_parameters, r"(?i)\$Group")
        self.assertEqual(GROUP_IDS, live_export.TARGET_GROUP_IDS)
        self.assertEqual(
            [GROUP_1, GROUP_2],
            [group["group_id"] for group in live_export.TARGET_GROUPS],
        )
        self.assertIn("RemoteDir -notmatch", self.ps)
        self.assertIn("SshTarget -notmatch", self.ps)

    def test本地必须调用游戏同一解析器并校验双群成员摘要(self):
        self.assertIn(
            "../opcgpro-web/src/lib/qqWhitelist.mjs", self.verifier
        )
        self.assertIn("previewQqWhitelistJson", self.verifier)
        self.assertIn('createHash("sha256")', self.verifier)
        self.assertIn("snapshot_sha256", self.verifier)
        self.assertGreaterEqual(self.ps.count("Invoke-LocalVerifier"), 3)
        self.assertIn("$snapshotAge.TotalMinutes -gt 5", self.ps)
        self.assertIn("StrictHostKeyChecking=yes", self.ps)

    def test并发导出只清理本次创建的最终文件(self):
        self.assertIn("$finalCreatedByThisRun = $false", self.ps)
        self.assertIn(
            "[IO.File]::Copy($tempFile, $finalPath, $false)", self.ps
        )
        self.assertRegex(
            self.ps,
            r"if \(\$finalCreatedByThisRun -and \$finalPath -and -not \$finalVerified",
        )

    def test实现不硬编码密码令牌或读取现有导出快照(self):
        combined = self.ps + self.remote + self.verifier
        self.assertNotRegex(
            combined, r"(?i)password\s*=\s*['\"][^'\"]+['\"]"
        )
        self.assertNotRegex(
            combined, r"(?i)access_token\s*=\s*['\"][^'\"]+['\"]"
        )
        self.assertNotIn(
            "qq-whitelist-297542853-524996856-2026", combined
        )

    def test双群导出文件沿用现有Git忽略边界(self):
        gitignore = (REPO_ROOT / ".gitignore").read_text(encoding="utf-8")
        self.assertIn("/qq-whitelist-297542853-*-live.json", gitignore)
        self.assertIn(
            "qq-whitelist-297542853-524996856-$timestamp-live.json", self.ps
        )


class NodeVerifierTests(unittest.TestCase):
    def make_payload(self):
        members = ["10001", "10002", "10003"]
        snapshot_sha256 = hashlib.sha256(
            "".join(f"{qq}\n" for qq in members).encode("utf-8")
        ).hexdigest()
        return {
            "source": {
                "protocol": "OneBot 11",
                "actions": list(live_export.ACTION_SEQUENCE),
                "group_ids": list(GROUP_IDS),
                "groups": [
                    {
                        "group_id": GROUP_1,
                        "group_name": GROUP_NAMES[GROUP_1],
                        "api_raw_count": 3,
                        "eligible_count": 2,
                        "excluded_bot_count": 1,
                        "excluded_bot_qqs": [BOT_QQ],
                        "members": ["10001", "10002"],
                        "group_info_count_before": 3,
                        "group_info_count_after": 3,
                        "stability_attempt": 1,
                    },
                    {
                        "group_id": GROUP_2,
                        "group_name": GROUP_NAMES[GROUP_2],
                        "api_raw_count": 2,
                        "eligible_count": 2,
                        "excluded_bot_count": 0,
                        "excluded_bot_qqs": [],
                        "members": ["10002", "10003"],
                        "group_info_count_before": 2,
                        "group_info_count_after": 2,
                        "stability_attempt": 1,
                    },
                ],
                "excluded_bot_qqs": [BOT_QQ],
                "fetched_at": "2026-08-27T22:30:00.123+08:00",
            },
            "validation": {
                "original_count": 5,
                "eligible_count": 4,
                "unique_count": 3,
                "duplicate_count": 1,
                "excluded_bot_count": 1,
                "invalid_count": 0,
                "cross_group_count": 0,
                "group_ids_seen": list(GROUP_IDS),
            },
            "snapshot_sha256": snapshot_sha256,
            "members": members,
        }

    def run_verifier(self, payload):
        temp_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT") or None
        with tempfile.TemporaryDirectory(dir=temp_root) as directory:
            path = Path(directory) / "whitelist.json"
            path.write_text(
                json.dumps(payload, ensure_ascii=False), encoding="utf-8"
            )
            return subprocess.run(
                [
                    "node",
                    str(BOT_DIR / "verify_qq_whitelist_export.mjs"),
                    str(path),
                ],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
                timeout=30,
            )

    def test游戏解析器接受完整双群导出并返回逐群计数与摘要(self):
        result = self.run_verifier(self.make_payload())
        self.assertEqual(0, result.returncode, result.stderr)
        summary = json.loads(result.stdout)
        self.assertEqual(3, summary["memberCount"])
        self.assertEqual(
            [3, 2],
            [group["memberCount"] for group in summary["groupMemberCounts"]],
        )
        self.assertEqual(1, summary["crossGroupDuplicateCount"])
        self.assertEqual(1, summary["excludedBotCount"])
        self.assertRegex(summary["snapshotSha256"], r"^[0-9a-f]{64}$")
        self.assertRegex(summary["sha256"], r"^[0-9a-f]{64}$")

    def test游戏解析器拒绝重复无效身份计数机器人和摘要不一致(self):
        cases = []

        duplicate = self.make_payload()
        duplicate["members"] = ["10001", "10001", "10003"]
        cases.append(duplicate)

        invalid = self.make_payload()
        invalid["members"] = ["10001", "１２３４５", "10003"]
        cases.append(invalid)

        wrong_groups = self.make_payload()
        wrong_groups["source"]["group_ids"] = [GROUP_2, GROUP_1]
        cases.append(wrong_groups)

        unstable = self.make_payload()
        unstable["source"]["groups"][1]["group_info_count_after"] = 3
        cases.append(unstable)

        bot_reinserted = self.make_payload()
        bot_reinserted["members"][2] = BOT_QQ
        cases.append(bot_reinserted)

        wrong_count = self.make_payload()
        wrong_count["validation"]["duplicate_count"] = 0
        cases.append(wrong_count)

        wrong_union = self.make_payload()
        wrong_union["source"]["groups"][1]["members"] = ["10002", "10004"]
        cases.append(wrong_union)

        wrong_digest = self.make_payload()
        wrong_digest["snapshot_sha256"] = "0" * 64
        cases.append(wrong_digest)

        for payload in cases:
            with self.subTest(payload=payload):
                result = self.run_verifier(payload)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", result.stdout)
                self.assertIn("本地白名单校验失败", result.stderr)


if __name__ == "__main__":
    unittest.main()
