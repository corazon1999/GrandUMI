# -*- coding: utf-8 -*-

import asyncio
import contextlib
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


GROUP_ID = "297542853"
GROUP_NAME = "GrandUMI测试群"


def group_info(count=2, group_id=GROUP_ID, group_name=GROUP_NAME):
    return {
        "status": "ok",
        "retcode": 0,
        "data": {
            "group_id": int(group_id),
            "group_name": group_name,
            "member_count": count,
        },
    }


def member_list(members=("10001", "10002"), group_id=GROUP_ID):
    return {
        "status": "ok",
        "retcode": 0,
        "data": [
            {"group_id": int(group_id), "user_id": int(qq)}
            for qq in members
        ],
    }


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
    def test稳定快照严格执行三段无缓存调用(self):
        onebot = FakeOneBot([group_info(), member_list(), group_info()])
        result = asyncio.run(
            live_export.collect_stable_snapshot(
                onebot, retry_delay_seconds=0
            )
        )

        self.assertEqual(["10001", "10002"], result["members"])
        self.assertEqual(
            ["get_group_info", "get_group_member_list", "get_group_info"],
            [action for action, _ in onebot.actions],
        )
        self.assertEqual(
            [True, True, True],
            [params["no_cache"] for _, params in onebot.actions],
        )
        self.assertTrue(
            all(params["group_id"] == int(GROUP_ID) for _, params in onebot.actions)
        )

    def test人数变化只做有限重试且绝不接受不稳定结果(self):
        responses = []
        for _ in range(live_export.MAX_STABILITY_ATTEMPTS):
            responses.extend([group_info(2), member_list(("10001", "10002", "10003")), group_info(3)])
        onebot = FakeOneBot(responses)

        with self.assertRaisesRegex(live_export.ExportError, "连续 3 次"):
            asyncio.run(
                live_export.collect_stable_snapshot(
                    onebot, retry_delay_seconds=0
                )
            )
        self.assertEqual(9, len(onebot.actions))

    def test群号群名串群重复空值和无效QQ全部拒绝(self):
        cases = [
            [group_info(group_id="297542854"), member_list(), group_info()],
            [group_info(group_name="其他群"), member_list(), group_info()],
            [group_info(), member_list(group_id="297542854"), group_info()],
            [group_info(), member_list(("10001", "10001")), group_info()],
            [group_info(), {"data": [{"group_id": int(GROUP_ID), "user_id": ""}]}, group_info()],
            [group_info(), {"data": [{"group_id": int(GROUP_ID), "user_id": "１２３４５"}]}, group_info()],
        ]
        for responses in cases:
            with self.subTest(responses=responses):
                with self.assertRaises(live_export.ExportError):
                    asyncio.run(live_export.collect_snapshot(FakeOneBot(responses)))

    def test配置令牌只从现有配置读取且不会覆盖URL内令牌(self):
        url = live_export._build_websocket_url(
            {"ws_url": "ws://napcat:3001", "access_token": "仅用于单元测试"}
        )
        self.assertIn("access_token=", url)
        self.assertNotIn("仅用于单元测试", url)
        with self.assertRaises(live_export.ExportError):
            live_export._build_websocket_url(
                {"ws_url": "ws://napcat:3001?access_token=已有值", "access_token": ""}
            )

    def test远端服务失败时标准输出为空且返回非零(self):
        standard_output = io.StringIO()
        standard_error = io.StringIO()
        with mock.patch.object(
            live_export,
            "export_live_whitelist",
            new=mock.AsyncMock(side_effect=live_export.ExportError("NapCat 未运行")),
        ):
            with contextlib.redirect_stdout(standard_output), contextlib.redirect_stderr(standard_error):
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
        self.assertIn("Get-GrandUmiTempDirectory -Category 'QqWhitelistExport'", self.ps)
        self.assertIn("E:\\GrandUMI-Temp\\", self.ps)
        self.assertRegex(self.ps, r"finally\s*\{[\s\S]*Remove-Item -LiteralPath \$tempFile")
        for forbidden in ("GetTempPath", "$env:TEMP", "$env:TMP", "%TEMP%", "C:\\"):
            self.assertNotIn(forbidden, self.ps)

    def test远端通过标准输入执行且不留下远端临时文件(self):
        self.assertIn("docker compose exec -T bug-bot python -", self.ps)
        self.assertIn("StandardInput.Write($InputText)", self.ps)
        self.assertNotIn("/tmp/", self.ps + self.remote)
        self.assertNotIn("tempfile", self.remote)
        self.assertNotIn("send_group_msg", self.remote)

    def test目标群不可由一键脚本参数改写且远端命令输入受限(self):
        self.assertNotRegex(self.ps, r"param\([\s\S]*Group")
        self.assertEqual(GROUP_ID, live_export.TARGET_GROUP_ID)
        self.assertEqual(GROUP_NAME, live_export.TARGET_GROUP_NAME)
        self.assertIn("RemoteDir -notmatch", self.ps)
        self.assertIn("SshTarget -notmatch", self.ps)

    def test本地必须调用游戏同一解析器并计算SHA256(self):
        self.assertIn("../opcgpro-web/src/lib/qqWhitelist.mjs", self.verifier)
        self.assertIn("previewQqWhitelistJson", self.verifier)
        self.assertIn('createHash("sha256")', self.verifier)
        self.assertGreaterEqual(self.ps.count("Invoke-LocalVerifier"), 3)
        self.assertIn("$snapshotAge.TotalMinutes -gt 5", self.ps)
        self.assertIn("StrictHostKeyChecking=yes", self.ps)

    def test并发导出只清理本次创建的最终文件(self):
        self.assertIn("$finalCreatedByThisRun = $false", self.ps)
        self.assertIn("[IO.File]::Copy($tempFile, $finalPath, $false)", self.ps)
        self.assertRegex(
            self.ps,
            r"if \(\$finalCreatedByThisRun -and \$finalPath -and -not \$finalVerified",
        )

    def test实现不硬编码密码令牌或读取现有导出快照(self):
        combined = self.ps + self.remote + self.verifier
        self.assertNotRegex(combined, r"(?i)password\s*=\s*['\"][^'\"]+['\"]")
        self.assertNotRegex(combined, r"(?i)access_token\s*=\s*['\"][^'\"]+['\"]")
        self.assertNotIn("qq-whitelist-297542853-2026", combined)

    def test一键导出文件不会污染Git工作区(self):
        gitignore = (REPO_ROOT / ".gitignore").read_text(encoding="utf-8")
        self.assertIn("/qq-whitelist-297542853-*-live.json", gitignore)


class NodeVerifierTests(unittest.TestCase):
    def make_payload(self):
        return {
            "source": {
                "protocol": "OneBot 11",
                "actions": list(live_export.ACTION_SEQUENCE),
                "group_id": GROUP_ID,
                "group_name": GROUP_NAME,
                "fetched_at": "2026-08-27T22:30:00.123+08:00",
                "stability_attempt": 1,
                "api_raw_count": 2,
                "group_info_count_before": 2,
                "group_info_count_after": 2,
            },
            "validation": {
                "original_count": 2,
                "unique_count": 2,
                "duplicate_count": 0,
                "invalid_count": 0,
                "cross_group_count": 0,
                "group_ids_seen": [GROUP_ID],
            },
            "members": ["10001", "10002"],
        }

    def run_verifier(self, payload):
        temp_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT") or None
        with tempfile.TemporaryDirectory(dir=temp_root) as directory:
            path = Path(directory) / "whitelist.json"
            path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
            return subprocess.run(
                ["node", str(BOT_DIR / "verify_qq_whitelist_export.mjs"), str(path)],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
                timeout=30,
            )

    def test游戏解析器接受完整有效导出并返回摘要(self):
        result = self.run_verifier(self.make_payload())
        self.assertEqual(0, result.returncode, result.stderr)
        summary = json.loads(result.stdout)
        self.assertEqual(2, summary["memberCount"])
        self.assertRegex(summary["sha256"], r"^[0-9a-f]{64}$")

    def test游戏解析器拒绝重复无效和身份不一致(self):
        cases = []

        duplicate = self.make_payload()
        duplicate["members"] = ["10001", "10001"]
        cases.append(duplicate)

        invalid = self.make_payload()
        invalid["members"] = ["10001", "１２３４５"]
        cases.append(invalid)

        wrong_group = self.make_payload()
        wrong_group["source"]["group_id"] = "297542854"
        cases.append(wrong_group)

        unstable = self.make_payload()
        unstable["source"]["group_info_count_after"] = 3
        cases.append(unstable)

        for payload in cases:
            with self.subTest(payload=payload):
                result = self.run_verifier(payload)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", result.stdout)
                self.assertIn("本地白名单校验失败", result.stderr)


if __name__ == "__main__":
    unittest.main()
