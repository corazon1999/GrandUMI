# -*- coding: utf-8 -*-
"""模拟桌面版升级及常驻工作器保留旧 PATH 时的命令恢复。"""

import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import agent_worker


@unittest.skipUnless(os.name == "nt", "仅验证 Windows 桌面版入口恢复")
class CodexCommandRecoveryTests(unittest.TestCase):
    def setUp(self):
        configured_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT")
        if not configured_root:
            self.fail("命令恢复测试必须设置 E 盘 GRANDUMI_TEST_TEMP_ROOT")
        temp_root = Path(configured_root).resolve()
        self.assertEqual("E:", temp_root.drive.upper())
        temp_root.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=temp_root)
        self.addCleanup(self.temp.cleanup)
        self.local_app_data = Path(self.temp.name) / "Local"
        self.bin_root = self.local_app_data / "OpenAI" / "Codex" / "bin"
        self.old = self.bin_root / "1111111111111111" / "codex.exe"
        self.new = self.bin_root / "2222222222222222" / "codex.exe"
        self.new.parent.mkdir(parents=True)
        self.new.write_bytes("测试原生入口".encode("utf-8"))
        env = mock.patch.dict(os.environ, {"LOCALAPPDATA": str(self.local_app_data)})
        env.start()
        self.addCleanup(env.stop)
        which = mock.patch.object(agent_worker.shutil, "which", return_value=None)
        which.start()
        self.addCleanup(which.stop)

    def test旧版本被移除且PATH失效时恢复当前原生入口(self):
        self.assertEqual(
            str(self.new), agent_worker.resolve_codex_command(str(self.old))
        )

    def test默认命令在常驻进程旧PATH中丢失时仍可恢复(self):
        for command in ("codex", "codex.exe"):
            with self.subTest(command=command):
                self.assertEqual(str(self.new), agent_worker.resolve_codex_command(command))

    def test多个受管版本选择最新入口且忽略其他目录(self):
        self.old.parent.mkdir(parents=True)
        self.old.write_bytes("旧入口".encode("utf-8"))
        os.utime(self.old, (100, 100))
        os.utime(self.new, (200, 200))
        unrelated = self.bin_root / "unmanaged" / "codex.exe"
        unrelated.parent.mkdir()
        unrelated.write_bytes("其他程序".encode("utf-8"))
        os.utime(unrelated, (300, 300))
        self.assertEqual(str(self.new), agent_worker.resolve_codex_command("codex"))

    def test仍存在的显式入口保持用户配置(self):
        self.old.parent.mkdir(parents=True)
        self.old.write_bytes("显式入口".encode("utf-8"))
        self.assertEqual(str(self.old), agent_worker.resolve_codex_command(str(self.old)))

    def test自定义路径丢失时不自动替换用户指定程序(self):
        custom = Path(self.temp.name) / "custom" / "codex.exe"
        with self.assertRaisesRegex(agent_worker.WorkerError, "未找到 Codex 命令"):
            agent_worker.resolve_codex_command(str(custom))

    def test受管目录中没有可用程序时保持任务排队(self):
        self.new.unlink()
        with self.assertRaisesRegex(agent_worker.WorkerError, "未找到 Codex 命令"):
            agent_worker.resolve_codex_command(str(self.old))


if __name__ == "__main__":
    unittest.main()
