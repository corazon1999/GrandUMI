# -*- coding: utf-8 -*-
"""验证常驻工作器切换本机代理时不会丢失或耗尽待处理消息。"""

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import chat_agent_worker


class ChatWorkerProxyTests(unittest.TestCase):
    def setUp(self):
        configured_root = os.environ.get("GRANDUMI_TEST_TEMP_ROOT")
        if not configured_root:
            self.fail("代理回归测试必须设置 GRANDUMI_TEST_TEMP_ROOT")
        temp_root = Path(configured_root).resolve()
        if os.name == "nt":
            self.assertEqual("E:", temp_root.drive.upper())
        temp_root.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=temp_root)
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        repo = root / "repo"
        schema_dir = repo / "qq-bug-bot" / "schemas"
        schema_dir.mkdir(parents=True)
        (schema_dir / "chat.schema.json").write_text("{}", encoding="utf-8")
        (repo / ".git").mkdir()
        (repo / "AGENTS.md").write_text("测试规则", encoding="utf-8")
        self.cfg = {
            "server": "root@example.com",
            "remote_bot_dir": "/opt/qq-bug-bot",
            "repository_root": str(repo),
            "jobs_root": str(root / "jobs"),
            "logs_root": str(root / "logs"),
            "codex_proxy": "http://127.0.0.1:7891",
        }
        self.config_path = root / "worker.json"
        self.write_config()
        self.repo = repo

    def write_config(self):
        self.config_path.write_text(json.dumps(self.cfg), encoding="utf-8")

    def worker(self, mode="chat"):
        return chat_agent_worker.ChatAgentWorker(
            self.cfg.copy(), mode=mode, admin_workspace=self.repo,
            config_path=self.config_path,
        )

    def test聊天和管理员代理未启动时均不领取消息(self):
        for mode in ("chat", "admin"):
            with self.subTest(mode=mode):
                worker = self.worker(mode)
                with mock.patch.object(worker, "resolve_current_codex_command"), \
                     mock.patch.object(chat_agent_worker.socket, "create_connection", side_effect=OSError("端口关闭")), \
                     mock.patch.object(worker, "bridge") as bridge:
                    with self.assertRaisesRegex(chat_agent_worker.WorkerError, "任务保持排队"):
                        worker.run_once()
                bridge.assert_not_called()

    def test热更新代理后检查新端口而不沿用旧端口(self):
        worker = self.worker()
        self.cfg["codex_proxy"] = "http://127.0.0.1:9098"
        self.write_config()
        with mock.patch.object(chat_agent_worker.socket, "create_connection") as connect:
            worker.validate_current_codex_proxy()
        connect.assert_called_once_with(("127.0.0.1", 9098), timeout=2)

    def test热更新代理传入聊天和管理员模型子进程(self):
        event = {"type": "item.completed", "item": {
            "type": "agent_message", "text": json.dumps({"reply": "自检通过"}),
        }}
        result = subprocess.CompletedProcess([], 0, json.dumps(event) + "\n", "")
        for mode in ("chat", "admin"):
            with self.subTest(mode=mode):
                self.cfg["codex_proxy"] = "http://127.0.0.1:7891"
                self.write_config()
                worker = self.worker(mode)
                self.cfg["codex_proxy"] = "http://127.0.0.1:9098"
                self.write_config()
                with mock.patch.object(worker, "resolve_current_codex_command", return_value="codex.exe"), \
                     mock.patch.object(chat_agent_worker, "run_process", return_value=result) as run:
                    worker.run_codex("测试", admin_mode=mode == "admin")
                self.assertEqual({"HTTP_PROXY": "http://127.0.0.1:9098", "HTTPS_PROXY": "http://127.0.0.1:9098"}, run.call_args.kwargs["env_extra"])

    def test无代理或远程代理不探测本机端口(self):
        for proxy in ("", "http://proxy.example.com:8080"):
            with self.subTest(proxy=proxy):
                self.cfg["codex_proxy"] = proxy
                self.write_config()
                with mock.patch.object(chat_agent_worker.socket, "create_connection") as connect:
                    self.worker().validate_current_codex_proxy()
                connect.assert_not_called()

    def test代理端口无效时不领取消息且不暴露凭据(self):
        self.cfg["codex_proxy"] = "http://测试账号:测试密码@127.0.0.1:invalid"
        self.write_config()
        with self.assertRaisesRegex(chat_agent_worker.WorkerError, "代理配置无效") as error:
            self.worker().validate_current_codex_proxy()
        self.assertNotIn("测试密码", str(error.exception))


if __name__ == "__main__":
    unittest.main()
