# -*- coding: utf-8 -*-

import re
import json
import unittest
from pathlib import Path


BOT_DIR = Path(__file__).resolve().parents[1]


class DeployFileTests(unittest.TestCase):
    def test_Dockerfile复制的文件均进入构建上下文(self):
        dockerfile = (BOT_DIR / "Dockerfile").read_text(encoding="utf-8")
        dockerignore = (BOT_DIR / ".dockerignore").read_text(encoding="utf-8")
        allowed = {
            line[1:].strip()
            for line in dockerignore.splitlines()
            if line.startswith("!")
        }
        copied = []
        for line in dockerfile.splitlines():
            if line.startswith("COPY ") and line.endswith(" ./"):
                copied.extend(line.removeprefix("COPY ").removesuffix(" ./").split())
        self.assertTrue(copied)
        self.assertEqual([], sorted(name for name in copied if name not in allowed))

    def test_部署包包含Docker上下文规则(self):
        powershell = (BOT_DIR / "deploy-bot-server.ps1").read_text(
            encoding="utf-8-sig"
        )
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")
        self.assertRegex(powershell, re.escape('".dockerignore"'))
        self.assertRegex(shell, r'files="[^"]*\.dockerignore(?: |")')

    def test_配置切换和回滚均强制重建机器人(self):
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")
        self.assertGreaterEqual(shell.count("--force-recreate bug-bot"), 2)

    def test_白名单同步模块进入镜像部署包且配置默认关闭(self):
        dockerfile = (BOT_DIR / "Dockerfile").read_text(encoding="utf-8")
        dockerignore = (BOT_DIR / ".dockerignore").read_text(encoding="utf-8")
        powershell = (BOT_DIR / "deploy-bot-server.ps1").read_text(
            encoding="utf-8-sig"
        )
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")
        config = json.loads(
            (BOT_DIR / "config.server.example.json").read_text(encoding="utf-8")
        )
        for content in (dockerfile, dockerignore, powershell, shell):
            self.assertIn("qq_whitelist_sync.py", content)
        self.assertIs(config["qq_whitelist_sync_enabled"], False)
        self.assertEqual(297542853, config["qq_whitelist_sync_group_id"])
        self.assertEqual("GrandUMI测试群", config["qq_whitelist_sync_group_name"])
        self.assertEqual(
            "GRANDUMI_QQ_WHITELIST_SYNC_SECRET",
            config["qq_whitelist_sync_secret_env"],
        )

    def test_白名单内部入口同时受固定来源本机代理和未提交密钥保护(self):
        repo = BOT_DIR.parent
        production_nginx = (
            repo / "ops/server/grandumi-production-proxy.nginx"
        ).read_text(encoding="utf-8")
        test_nginx = (repo / "ops/server/grandumi-test.nginx").read_text(
            encoding="utf-8"
        )
        environment_example = (
            repo / "ops/server/grandumi-qq-whitelist-sync.env.example"
        ).read_text(encoding="utf-8")
        services = [
            (repo / "ops/server/grandumi-test-backend.service").read_text(
                encoding="utf-8"
            ),
            (repo / "ops/server/grandumi-production-backend.service").read_text(
                encoding="utf-8"
            ),
            (repo / "ops/server/grandumi-production-backend@.service").read_text(
                encoding="utf-8"
            ),
        ]
        for nginx in (production_nginx, test_nginx):
            self.assertIn("location = /internal/qq-whitelist/sync", nginx)
            self.assertIn("allow 8.210.155.25;", nginx)
            self.assertIn("deny all;", nginx)
            self.assertIn("X-GrandUMI-Internal-Source", nginx)
        self.assertIn('if ($host != "direct.grand-umi.com")', production_nginx)
        for service in services:
            self.assertIn(
                "EnvironmentFile=-/etc/grandumi/qq-whitelist-sync.env", service
            )
        self.assertIn("GRANDUMI_QQ_WHITELIST_SYNC_ENABLED=0", environment_example)
        self.assertIn("GRANDUMI_QQ_WHITELIST_SYNC_SECRET=REPLACE_ME", environment_example)
        self.assertNotRegex(environment_example, r"SECRET=[0-9a-fA-F]{64}")

    def test_Bug工作器隐藏常驻且停止旧实例(self):
        installer = (BOT_DIR / "install-agent-worker.ps1").read_text(
            encoding="utf-8-sig"
        )
        self.assertIn("pythonw.exe", installer)
        self.assertIn("-Execute $pythonw", installer)
        self.assertIn("Stop-ScheduledTask", installer)

    def test_聊天工作器隐藏常驻并自动重启(self):
        installer = (BOT_DIR / "install-chat-agent-worker.ps1").read_text(
            encoding="utf-8-sig"
        )
        self.assertIn("pythonw.exe", installer)
        self.assertIn("RestartCount 100", installer)
        self.assertIn("Start-ScheduledTask", installer)
        self.assertIn("女帝汉库克", installer)
        self.assertIn("Get-GrandUmiTempDirectory", installer)
        self.assertIn("--media-root", installer)
        self.assertIn("RepetitionInterval (New-TimeSpan -Minutes 5)", installer)
        self.assertIn("AllowStartIfOnBatteries", installer)
        self.assertIn("DontStopIfGoingOnBatteries", installer)

    def test_管理员工作器独立隐藏常驻并绑定项目工作区(self):
        installer = (BOT_DIR / "install-admin-agent-worker.ps1").read_text(
            encoding="utf-8-sig"
        )
        self.assertIn("pythonw.exe", installer)
        self.assertIn("GrandUMI-Admin-Agent", installer)
        self.assertIn("--mode admin", installer)
        self.assertIn("--admin-workspace", installer)
        self.assertIn("D:\\Self\\GrandUMI", installer)
        self.assertIn("RestartCount 100", installer)
        self.assertIn("RepetitionInterval (New-TimeSpan -Minutes 5)", installer)
        self.assertIn("AllowStartIfOnBatteries", installer)
        self.assertIn("DontStopIfGoingOnBatteries", installer)


if __name__ == "__main__":
    unittest.main()
