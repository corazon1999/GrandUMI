# -*- coding: utf-8 -*-

import ast
import json
import re
import unittest
from pathlib import Path


BOT_DIR = Path(__file__).resolve().parents[1]


class DeployFileTests(unittest.TestCase):
    @staticmethod
    def _load_shell_config_migration():
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")
        heredoc = re.search(
            r'python3 - "\$deploy_dir/config\.server\.json" "\$enable_agent" '
            r"<<'PY'\n(?P<source>.*?)\nPY\n",
            shell,
            re.DOTALL,
        )
        if not heredoc:
            raise AssertionError("未找到 config.server.json 的 Python 迁移脚本")
        tree = ast.parse(heredoc.group("source"))
        function = next(
            (
                node
                for node in tree.body
                if isinstance(node, ast.FunctionDef) and node.name == "migrate_config"
            ),
            None,
        )
        if function is None:
            raise AssertionError("未找到 migrate_config 配置迁移函数")
        namespace = {}
        exec(
            compile(
                ast.Module(body=[function], type_ignores=[]),
                "deploy-bot-server.sh:migrate_config",
                "exec",
            ),
            namespace,
        )
        return namespace["migrate_config"]

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
        self.assertRegex(powershell, re.escape('".env.example"'))
        self.assertRegex(shell, r'files="[^"]*\.env\.example(?: |")')

    def test_NapCat锁定设备身份和镜像并使用信号包装(self):
        compose = (BOT_DIR / "docker-compose.yml").read_text(encoding="utf-8")
        environment = (BOT_DIR / ".env.example").read_text(encoding="utf-8")
        wrapper = (BOT_DIR / "napcat-init.sh").read_text(encoding="utf-8")
        powershell = (BOT_DIR / "deploy-bot-server.ps1").read_text(
            encoding="utf-8-sig"
        )
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")

        self.assertNotIn("mlikiowa/napcat-docker:latest", compose)
        self.assertIn(
            "mlikiowa/napcat-docker@sha256:"
            "31bc4657c4bb5a2a44d11c12df863dd6d5bd109e78163e34cf09d8149cf9b078",
            compose,
        )
        self.assertRegex(
            compose,
            r"mlikiowa/napcat-docker@sha256:[0-9a-f]{64}",
        )
        self.assertIn('mac_address: "${NAPCAT_MAC_ADDRESS:', compose)
        self.assertIn('hostname: "${NAPCAT_HOSTNAME:', compose)
        self.assertIn("NAPCAT_MAC_ADDRESS=", environment)
        self.assertIn("NAPCAT_HOSTNAME=", environment)
        self.assertIn('ACCOUNT: "${NAPCAT_ACCOUNT:-}"', compose)
        self.assertIn("NAPCAT_ACCOUNT=", environment)
        self.assertIn("grandumi-napcat-init.sh", compose)
        self.assertIn("setsid /bin/bash", wrapper)
        self.assertIn('kill -TERM -- "-$child_pid"', wrapper)
        self.assertIn('kill -KILL -- "-$child_pid"', wrapper)
        self.assertIn("napcat_quick_password_md5", compose)
        self.assertIn("/run/secrets/napcat_quick_password_md5", wrapper)
        self.assertIn("^[a-fA-F0-9]{32}$", wrapper)
        self.assertNotIn("NAPCAT_QUICK_PASSWORD_MD5:", compose)
        self.assertIn(
            "napcat-quick-password-md5.secret",
            (BOT_DIR / ".gitignore").read_text(encoding="utf-8"),
        )
        for content in (powershell, shell):
            self.assertIn("napcat-init.sh", content)
            self.assertNotIn("napcat-quick-password-md5.secret", content)

    def test_三助理NapCat账号设备和持久化卷完全隔离(self):
        compose = (BOT_DIR / "docker-compose.yml").read_text(encoding="utf-8")
        environment = (BOT_DIR / ".env.example").read_text(encoding="utf-8")
        digest = (
            "mlikiowa/napcat-docker@sha256:"
            "31bc4657c4bb5a2a44d11c12df863dd6d5bd109e78163e34cf09d8149cf9b078"
        )

        self.assertEqual(3, compose.count(f"image: {digest}"))
        for service, container, account, port in (
            ("napcat-eagle", "grandumi-napcat-eagle", "NAPCAT_EAGLE_ACCOUNT", 6100),
            ("napcat-shark", "grandumi-napcat-shark", "NAPCAT_SHARK_ACCOUNT", 6101),
        ):
            self.assertIn(f"  {service}:", compose)
            self.assertIn(f"container_name: {container}", compose)
            self.assertIn(f'ACCOUNT: "${{{account}:-}}"', compose)
            self.assertIn(f'"127.0.0.1:{port}:6099"', compose)
            self.assertIn(account + "=", environment)
            self.assertIn(f"      - {service}", compose)

        expected_volumes = (
            "napcat_qq", "napcat_config", "napcat_plugins",
            "napcat_eagle_qq", "napcat_eagle_config", "napcat_eagle_plugins",
            "napcat_shark_qq", "napcat_shark_config", "napcat_shark_plugins",
        )
        for volume in expected_volumes:
            self.assertIn(f"  {volume}:", compose)
        self.assertIn("NAPCAT_EAGLE_MAC_ADDRESS=", environment)
        self.assertIn("NAPCAT_SHARK_MAC_ADDRESS=", environment)

    def test_多助理配置默认保留主助理并安全关闭未知账号(self):
        for name, expected_urls in (
            (
                "config.example.json",
                ("ws://127.0.0.1:3001", "ws://127.0.0.1:3002", "ws://127.0.0.1:3003"),
            ),
            (
                "config.server.example.json",
                ("ws://napcat:3001", "ws://napcat-eagle:3001", "ws://napcat-shark:3001"),
            ),
        ):
            config = json.loads((BOT_DIR / name).read_text(encoding="utf-8"))
            connections = config["assistant_connections"]
            self.assertEqual(
                ["primary", "s-eagle", "s-shark"],
                [item["id"] for item in connections],
            )
            self.assertEqual(
                ["primary", "admin_only", "admin_only"],
                [item["role"] for item in connections],
            )
            self.assertEqual([True, False, False], [item["enabled"] for item in connections])
            self.assertEqual(list(expected_urls), [item["ws_url"] for item in connections])
            self.assertEqual(
                ["3215228879", "3430685803", "184689168"],
                [item["expected_self_id"] for item in connections],
            )
            self.assertEqual(651846226, config["admin_agent_owner_qq"])

    def test_部署迁移只为现有蛇鲨连接启用指定群欢迎(self):
        migrate = self._load_shell_config_migration()
        config = {
            "assistant_connections": [
                {
                    "id": "primary",
                    "access_token": "primary-secret",
                    "new_member_welcome_enabled": False,
                    "new_member_welcome_groups": [111],
                },
                {
                    "id": "s-eagle",
                    "access_token": "eagle-secret",
                    "new_member_welcome_enabled": True,
                    "new_member_welcome_groups": [111],
                },
                {
                    "id": "s-shark",
                    "access_token": "shark-secret",
                    "custom": "保留",
                },
                {"id": "future-assistant", "access_token": "future-secret"},
                "无效连接记录",
            ]
        }

        result = migrate(config)

        self.assertIs(config, result)
        connections = result["assistant_connections"]
        for connection in (connections[0], connections[2]):
            self.assertIs(connection["new_member_welcome_enabled"], True)
            self.assertEqual([297542853], connection["new_member_welcome_groups"])
        self.assertIs(connections[1]["new_member_welcome_enabled"], False)
        self.assertEqual([], connections[1]["new_member_welcome_groups"])
        self.assertEqual(
            ["primary-secret", "eagle-secret", "shark-secret", "future-secret"],
            [connection["access_token"] for connection in connections[:4]],
        )
        self.assertEqual("保留", connections[2]["custom"])
        self.assertNotIn("new_member_welcome_enabled", connections[3])
        self.assertEqual("无效连接记录", connections[4])

        missing = {"assistant_connections": [{"id": "s-eagle"}]}
        migrate(missing)
        self.assertEqual(["s-eagle"], [item["id"] for item in missing["assistant_connections"]])

    def test_三助理上线清单覆盖身份核验重放恢复和回滚(self):
        checklist = (BOT_DIR / "三助理上线清单.md").read_text(encoding="utf-8")
        for required in (
            "651846226", "expected_self_id", "get_login_info",
            "message_id", "不得新增第二条任务", "s-蛇", "s-鹰", "s-鲨",
            "enabled=false", "失败恢复与回滚", "reply_sent_at",
        ):
            self.assertIn(required, checklist)

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

    def test_白名单同步域名固定直连唯一服务器且不使用弃用正式域名(self):
        compose = (BOT_DIR / "docker-compose.yml").read_text(encoding="utf-8")
        for host in ("test.grand-umi.com", "ygo.grand-umi.com"):
            self.assertIn(f'- "{host}:103.146.230.37"', compose)
        self.assertNotIn('- "grand-umi.com:', compose)
        self.assertNotIn("host-gateway", compose)

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
            self.assertIn("location = /internal/qq-whitelist/sync/failure", nginx)
            self.assertIn("allow 103.146.230.37;", nginx)
            self.assertNotIn("allow 8.210.155.25;", nginx)
            self.assertIn("deny all;", nginx)
            self.assertIn(
                'X-GrandUMI-Internal-Source "qq-bug-bot@103.146.230.37"',
                nginx,
            )
        self.assertIn('if ($host != "direct.grand-umi.com")', production_nginx)
        for service in services:
            self.assertIn(
                "EnvironmentFile=-/etc/grandumi/qq-whitelist-sync.env", service
            )
        self.assertIn("GRANDUMI_QQ_WHITELIST_SYNC_ENABLED=0", environment_example)
        self.assertIn(
            "GRANDUMI_QQ_WHITELIST_SYNC_PROXY_ID=qq-bug-bot@103.146.230.37",
            environment_example,
        )
        self.assertIn("GRANDUMI_QQ_WHITELIST_SYNC_SECRET=REPLACE_ME", environment_example)
        self.assertNotRegex(environment_example, r"SECRET=[0-9a-fA-F]{64}")

    def test_机器人运维入口默认使用新香港主机(self):
        expected = "103.146.230.37"
        legacy = "8.210.155.25"
        files = [
            BOT_DIR / "deploy-bot-server.ps1",
            BOT_DIR / "configure-github-token.ps1",
            BOT_DIR / "export-live-qq-whitelist.ps1",
            BOT_DIR / "agent-worker.example.json",
            BOT_DIR / "一键导出QQ白名单.md",
        ]
        for path in files:
            content = path.read_text(encoding="utf-8-sig")
            self.assertIn(expected, content, path.name)
            self.assertNotIn(legacy, content, path.name)

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
