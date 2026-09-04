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
            r"<<'PY' \|\| rollback\n(?P<source>.*?)\nPY\n",
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
            self.assertEqual(
                [True, False, False],
                [item["new_member_welcome_enabled"] for item in connections],
            )
            self.assertEqual(
                [[297542853, 524996856], [], []],
                [item["new_member_welcome_groups"] for item in connections],
            )
            self.assertEqual(651846226, config["admin_agent_owner_qq"])

    def test_部署迁移幂等镜像二群并把白名单权威迁移为双群两小时(self):
        migrate = self._load_shell_config_migration()
        config = {
            "allowed_groups": [297542853],
            "abuse_moderation_enabled": True,
            "abuse_moderation_groups": [297542853],
            "group_add_auto_approval_enabled": True,
            "group_add_auto_approval_groups": [297542853],
            "new_member_verification_enabled": False,
            "new_member_verification_groups": [],
            "qq_whitelist_sync_enabled": True,
            "qq_whitelist_sync_group_id": 297542853,
            "qq_whitelist_sync_secret_env": "PRIVATE_SECRET_NAME",
            "assistant_connections": [
                {
                    "id": "primary",
                    "access_token": "primary-secret",
                    "enabled": True,
                    "new_member_welcome_enabled": True,
                    "new_member_welcome_groups": [297542853],
                },
                {
                    "id": "s-eagle",
                    "access_token": "eagle-secret",
                    "enabled": False,
                    "new_member_welcome_enabled": False,
                    "new_member_welcome_groups": [],
                },
                {
                    "id": "s-shark",
                    "access_token": "shark-secret",
                    "enabled": True,
                    "custom": "保留",
                    "new_member_welcome_enabled": False,
                    "new_member_welcome_groups": [],
                },
                {"id": "future-assistant", "access_token": "future-secret"},
                "无效连接记录",
            ]
        }
        original = json.loads(json.dumps(config, ensure_ascii=False))

        result = migrate(config)

        self.assertIsNot(config, result)
        self.assertEqual(original, config)
        for key in (
            "allowed_groups",
            "abuse_moderation_groups",
            "group_add_auto_approval_groups",
        ):
            self.assertEqual([297542853, 524996856], result[key])
        self.assertIs(result["abuse_moderation_enabled"], True)
        self.assertIs(result["group_add_auto_approval_enabled"], True)
        self.assertIs(result["new_member_verification_enabled"], False)
        self.assertEqual([], result["new_member_verification_groups"])
        self.assertIs(result["qq_whitelist_sync_enabled"], True)
        self.assertEqual(297542853, result["qq_whitelist_sync_group_id"])
        self.assertNotEqual(524996856, result["qq_whitelist_sync_group_id"])
        self.assertEqual(
            [297542853, 524996856],
            result["qq_whitelist_sync_group_ids"],
        )
        self.assertEqual(2, result["qq_whitelist_sync_interval_hours"])
        self.assertEqual(
            "PRIVATE_SECRET_NAME", result["qq_whitelist_sync_secret_env"]
        )
        connections = result["assistant_connections"]
        self.assertIs(connections[0]["new_member_welcome_enabled"], True)
        self.assertEqual(
            [297542853, 524996856],
            connections[0]["new_member_welcome_groups"],
        )
        for connection in (connections[1], connections[2]):
            self.assertIs(connection["new_member_welcome_enabled"], False)
            self.assertEqual([], connection["new_member_welcome_groups"])
        self.assertEqual(
            [True, False, True],
            [item["enabled"] for item in connections[:3]],
        )
        self.assertEqual(
            ["primary-secret", "eagle-secret", "shark-secret", "future-secret"],
            [connection["access_token"] for connection in connections[:4]],
        )
        self.assertEqual("保留", connections[2]["custom"])
        self.assertNotIn("new_member_welcome_enabled", connections[3])
        self.assertEqual("无效连接记录", connections[4])
        self.assertEqual(result, migrate(result))

    def test_部署迁移不把空作用域解释为自动开启二群(self):
        migrate = self._load_shell_config_migration()
        disabled = {
            "allowed_groups": [],
            "abuse_moderation_enabled": False,
            "abuse_moderation_groups": [],
            "group_add_auto_approval_enabled": False,
            "group_add_auto_approval_groups": [],
            "new_member_verification_enabled": False,
            "new_member_verification_groups": [],
            "qq_whitelist_sync_group_id": 297542853,
            "assistant_connections": [
                {
                    "id": "primary",
                    "new_member_welcome_enabled": False,
                    "new_member_welcome_groups": [],
                }
            ],
        }
        disabled_result = migrate(disabled)
        self.assertEqual([], disabled_result["allowed_groups"])
        self.assertEqual([], disabled_result["abuse_moderation_groups"])
        self.assertEqual([], disabled_result["group_add_auto_approval_groups"])
        self.assertEqual(
            [297542853, 524996856],
            disabled_result["qq_whitelist_sync_group_ids"],
        )
        self.assertEqual(2, disabled_result["qq_whitelist_sync_interval_hours"])

        invalid = dict(disabled)
        invalid["allowed_groups"] = "297542853"
        with self.assertRaisesRegex(ValueError, "allowed_groups 必须是群号数组"):
            migrate(invalid)

        legacy = {
            "allowed_groups": ["297542853"],
            "new_member_welcome_groups": ["297542853"],
        }
        legacy_result = migrate(legacy)
        self.assertEqual(["297542853", 524996856], legacy_result["allowed_groups"])
        self.assertEqual(
            ["297542853", 524996856],
            legacy_result["new_member_welcome_groups"],
        )

    def test_私密配置使用同目录唯一临时文件原子替换且失败回滚(self):
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")
        for required in (
            "tempfile.mkstemp",
            "os.fchown",
            "stat.S_IMODE(source_stat.st_mode)",
            "os.fsync(file.fileno())",
            "os.replace(tmp, path)",
            "os.fsync(directory_fd)",
            "<<'PY' || rollback",
            'mv -f "$rollback_config" "$deploy_dir/config.server.json"',
        ):
            self.assertIn(required, shell)
        self.assertNotIn('tmp = path + ".new"', shell)

    def test_三助理上线清单覆盖身份核验重放恢复和回滚(self):
        checklist = (BOT_DIR / "三助理上线清单.md").read_text(encoding="utf-8")
        for required in (
            "651846226", "expected_self_id", "get_login_info",
            "message_id", "不得新增第二条任务", "s-蛇", "s-鹰", "s-鲨",
            "enabled=false", "失败恢复与回滚", "reply_sent_at",
            "297542853", "524996856", "QQ 通道永远不构成授权",
        ):
            self.assertIn(required, checklist)

    def test_管理员工作器固定Sol高推理且租约覆盖超时(self):
        config = json.loads(
            (BOT_DIR / "agent-worker.example.json").read_text(encoding="utf-8")
        )
        installer = (BOT_DIR / "install-admin-agent-worker.ps1").read_text(
            encoding="utf-8-sig"
        )
        self.assertEqual("gpt-5.6-sol", config["admin_agent_model"])
        self.assertEqual("high", config["admin_agent_reasoning_effort"])
        self.assertGreaterEqual(
            config["admin_agent_lease_seconds"],
            config["admin_agent_timeout_seconds"] + 1800,
        )
        for required in ("gpt-5.6-sol / high", 'Join-Path $adminRoot ".git"'):
            self.assertIn(required, installer)

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
        self.assertNotEqual(524996856, config["qq_whitelist_sync_group_id"])
        self.assertEqual("GrandUMI测试群", config["qq_whitelist_sync_group_name"])
        self.assertEqual(
            [297542853, 524996856], config["qq_whitelist_sync_group_ids"]
        )
        self.assertEqual(2, config["qq_whitelist_sync_interval_hours"])
        self.assertEqual(
            "GRANDUMI_QQ_WHITELIST_SYNC_SECRET",
            config["qq_whitelist_sync_secret_env"],
        )

    def test_辱骂治理模块进入镜像部署包且样例边界明确(self):
        dockerfile = (BOT_DIR / "Dockerfile").read_text(encoding="utf-8")
        dockerignore = (BOT_DIR / ".dockerignore").read_text(encoding="utf-8")
        powershell = (BOT_DIR / "deploy-bot-server.ps1").read_text(
            encoding="utf-8-sig"
        )
        shell = (BOT_DIR / "deploy-bot-server.sh").read_text(encoding="utf-8")
        for content in (dockerfile, dockerignore, powershell, shell):
            self.assertIn("abuse_moderation.py", content)

        local = json.loads(
            (BOT_DIR / "config.example.json").read_text(encoding="utf-8")
        )
        server = json.loads(
            (BOT_DIR / "config.server.example.json").read_text(encoding="utf-8")
        )
        self.assertIs(local["abuse_moderation_enabled"], False)
        self.assertEqual([], local["abuse_moderation_groups"])
        self.assertIs(server["abuse_moderation_enabled"], True)
        self.assertEqual(
            [297542853, 524996856], server["abuse_moderation_groups"]
        )
        expected_exemptions = [651846226, 3215228879, 3430685803, 184689168]
        self.assertEqual(expected_exemptions, local["abuse_moderation_exempt_qqs"])
        self.assertEqual(expected_exemptions, server["abuse_moderation_exempt_qqs"])

        readme = (BOT_DIR / "README.md").read_text(encoding="utf-8")
        for required in (
            "set_group_ban",
            "86400",
            "abuse_moderation_actions",
            "顶层结构化",
            "不会自动重试或延长",
            "普通部署脚本保留现有开关",
        ):
            self.assertIn(required, readme)

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
            "GRANDUMI_QQ_WHITELIST_SYNC_GROUP_IDS=297542853,524996856",
            environment_example,
        )
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
        self.assertIn("--workspace-lock-root", installer)
        self.assertIn('Get-GrandUmiTempDirectory -Category "Locks"', installer)
        self.assertIn("D:\\Self\\GrandUMI", installer)
        self.assertIn("RestartCount 100", installer)
        self.assertIn("RepetitionInterval (New-TimeSpan -Minutes 5)", installer)
        self.assertIn("AllowStartIfOnBatteries", installer)
        self.assertIn("DontStopIfGoingOnBatteries", installer)


if __name__ == "__main__":
    unittest.main()
