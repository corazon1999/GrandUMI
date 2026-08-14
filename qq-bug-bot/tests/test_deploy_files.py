# -*- coding: utf-8 -*-

import re
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

    def test_本机工作器使用常驻控制台且停止旧实例(self):
        installer = (BOT_DIR / "install-agent-worker.ps1").read_text(
            encoding="utf-8-sig"
        )
        self.assertIn("-Execute $python", installer)
        self.assertNotIn("-Execute $pythonw", installer)
        self.assertIn("Stop-ScheduledTask", installer)

    def test_聊天工作器隐藏常驻并自动重启(self):
        installer = (BOT_DIR / "install-chat-agent-worker.ps1").read_text(
            encoding="utf-8-sig"
        )
        self.assertIn("pythonw.exe", installer)
        self.assertIn("RestartCount 100", installer)
        self.assertIn("Start-ScheduledTask", installer)
        self.assertIn("女帝汉库克", installer)


if __name__ == "__main__":
    unittest.main()
