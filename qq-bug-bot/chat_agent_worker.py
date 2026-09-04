# -*- coding: utf-8 -*-
"""GrandUMI QQ 普通只读聊天与唯一管理员项目 Agent 常驻工作器。"""

import argparse
import hashlib
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path
from pathlib import PurePosixPath

import chat_protocol
import admin_agent_security
from repository_workspace_lock import RepositoryWorkspaceLock
from agent_worker import (
    BRIDGE_PREFIX,
    WorkerError,
    is_windows_dll_init_failure,
    load_config,
    require_success,
    resolve_codex_command,
    run_process,
)


# 租约覆盖 Codex 超时、最多 8 张图片的 SCP 超时，以及最终桥接重试。
# 这样本工作器尚未收束时，服务端不会提前把同一任务重新发给另一实例。
ADMIN_AGENT_LEASE_MARGIN_SECONDS = 1800
_SENSITIVE_ENV_MARKERS = (
    "TOKEN", "SECRET", "PASSWORD", "COOKIE", "API_KEY", "PRIVATE_KEY",
    "CREDENTIAL",
)


class ChatAgentWorker:
    def __init__(
        self,
        cfg: dict,
        media_root: Path | None = None,
        mode: str = "chat",
        admin_workspace: Path | None = None,
        workspace_lock_root: Path | None = None,
        config_path: Path | None = None,
    ):
        self.cfg = cfg
        self.config_path = Path(config_path).resolve() if config_path else None
        self.repo = Path(cfg["repository_root"]).resolve()
        if mode not in ("chat", "admin"):
            raise WorkerError(f"不支持的聊天工作器模式: {mode}")
        self.mode = mode
        configured_admin_workspace = str(
            cfg.get("admin_workspace_root") or ""
        ).strip()
        admin_root = admin_workspace or (
            Path(configured_admin_workspace) if configured_admin_workspace else None
        )
        if self.mode == "admin" and admin_root is None:
            raise WorkerError("管理员 Agent 缺少 admin_workspace_root")
        self.admin_workspace = (
            Path(admin_root).resolve() if admin_root else None
        )
        if self.admin_workspace and not self.admin_workspace.is_dir():
            raise WorkerError(
                f"管理员 Agent 工作区不存在: {self.admin_workspace}"
            )
        configured_lock_root = str(
            cfg.get("workspace_lock_root") or ""
        ).strip()
        self.workspace_lock_root = (
            Path(workspace_lock_root or configured_lock_root).resolve()
            if workspace_lock_root or configured_lock_root
            else None
        )
        self.logs_root = Path(cfg["logs_root"]).resolve()
        self.logs_root.mkdir(parents=True, exist_ok=True)
        self.workdir = self.logs_root / "chat-sandbox"
        self.workdir.mkdir(parents=True, exist_ok=True)
        configured_id = str(cfg.get("chat_worker_id") or "").strip()
        raw_id = configured_id or (
            f"{socket.gethostname()}-{self.mode}-{os.getpid()}"
        )
        self.worker_id = re.sub(r"[^A-Za-z0-9._-]", "-", raw_id)[:80]
        log_name = (
            "admin-agent-worker.log"
            if self.mode == "admin"
            else "chat-agent-worker.log"
        )
        self.log_file = self.logs_root / log_name
        configured_media = str(cfg.get("chat_media_root") or "").strip()
        self.media_root = Path(
            media_root or configured_media or os.environ.get(
                "GRANDUMI_QQ_MEDIA_ROOT", "E:/GrandUMI-Temp/QQBotMedia"
            )
        ).resolve()
        self._loaded_source_fingerprint = self.source_fingerprint()

    @staticmethod
    def source_fingerprint() -> str:
        """识别常驻进程启动后执行入口或安全提示是否已经被更新。"""
        digest = hashlib.sha256()
        for path in (
            Path(__file__),
            Path(chat_protocol.__file__),
            Path(admin_agent_security.__file__),
        ):
            digest.update(path.name.encode("utf-8"))
            digest.update(b"\0")
            digest.update(path.read_bytes())
            digest.update(b"\0")
        return digest.hexdigest()

    def current_config(self) -> dict:
        """读取允许热更新的本机配置；远端队列与工作区仍固定为启动值。"""
        return load_config(self.config_path) if self.config_path else self.cfg

    def resolve_current_codex_command(self) -> str:
        """重新读取可热更新的 Codex 路径，但不切换任务所连接的远端队列。"""
        cfg = self.current_config()
        command = str(cfg.get("codex_command") or "codex")
        return resolve_codex_command(command)

    def validate_admin_runtime_settings(self) -> None:
        """管理员模型和推理强度只能等于代码固定值，配置不得降级。"""
        cfg = self.current_config()
        configured_model = str(
            cfg.get("admin_agent_model") or admin_agent_security.QQ_ADMIN_MODEL
        ).strip()
        configured_effort = str(
            cfg.get("admin_agent_reasoning_effort")
            or admin_agent_security.QQ_ADMIN_REASONING_EFFORT
        ).strip().lower()
        if configured_model != admin_agent_security.QQ_ADMIN_MODEL:
            raise WorkerError(
                "admin_agent_model 必须固定为 "
                f"{admin_agent_security.QQ_ADMIN_MODEL}"
            )
        if configured_effort != admin_agent_security.QQ_ADMIN_REASONING_EFFORT:
            raise WorkerError(
                "admin_agent_reasoning_effort 必须固定为 "
                f"{admin_agent_security.QQ_ADMIN_REASONING_EFFORT}"
            )

    def validate_admin_workspace(self) -> None:
        """全权限模式只能在带项目规则的真实 Git 工作区内启动。"""
        if self.admin_workspace is None or not self.admin_workspace.is_dir():
            raise WorkerError("管理员 Agent 工作区不存在")
        if not (self.admin_workspace / "AGENTS.md").is_file():
            raise WorkerError("管理员 Agent 工作区缺少 AGENTS.md")
        git_marker = self.admin_workspace / ".git"
        if not git_marker.exists():
            raise WorkerError("管理员 Agent 工作区不是 Git 工作区")

    @staticmethod
    def sensitive_environment_names() -> set[str]:
        """Codex 子进程使用登录态文件，不继承可能被项目命令读取的凭据变量。"""
        return {
            key
            for key in os.environ
            if any(marker in key.upper() for marker in _SENSITIVE_ENV_MARKERS)
        }

    def log(self, message: str) -> None:
        line = f"{datetime.now().isoformat(timespec='seconds')} {message}"
        print(line, flush=True)
        with self.log_file.open("a", encoding="utf-8") as file:
            file.write(line + "\n")

    def bridge(self, command: str, payload: dict | None = None) -> dict:
        if command not in (
            "chat-claim", "admin-claim", "chat-complete", "bug-intake-complete",
            "chat-release", "admin-reject", "status",
        ):
            raise WorkerError(f"非法聊天桥接命令: {command}")
        suffix = command
        if command in ("chat-claim", "admin-claim"):
            lease_key = (
                "admin_agent_lease_seconds"
                if command == "admin-claim"
                else "chat_lease_seconds"
            )
            if command == "admin-claim":
                timeout = max(
                    30, int(self.cfg.get("admin_agent_timeout_seconds", 7200))
                )
                default_lease = timeout + ADMIN_AGENT_LEASE_MARGIN_SECONDS
                lease = max(
                    120,
                    int(self.cfg.get(lease_key, default_lease)),
                    timeout + ADMIN_AGENT_LEASE_MARGIN_SECONDS,
                )
            else:
                lease = max(120, int(self.cfg.get(lease_key, 900)))
            suffix += f" --worker-id {self.worker_id} --lease-seconds {lease}"
        remote = (
            f"cd '{self.cfg['remote_bot_dir']}' && "
            f"docker compose exec -T bug-bot python agent_bridge.py {suffix}"
        )
        result = None
        for attempt in range(1, 4):
            result = run_process(
                [
                    "ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=15",
                    self.cfg["server"], remote,
                ],
                timeout=90,
                input_text=(
                    json.dumps(payload, ensure_ascii=False) if payload else None
                ),
            )
            if result.returncode == 0:
                break
            if is_windows_dll_init_failure(result.returncode) and attempt < 3:
                time.sleep(1)
                continue
            break
        assert result is not None
        require_success(result, f"服务器聊天桥接 {command}")
        for line in reversed(result.stdout.splitlines()):
            if line.startswith(BRIDGE_PREFIX):
                data = json.loads(line[len(BRIDGE_PREFIX):])
                if not data.get("ok"):
                    raise WorkerError(str(data.get("error") or "聊天桥接拒绝请求"))
                return data
        raise WorkerError("服务器聊天桥接未返回结构化结果")

    def run_codex(
        self,
        prompt: str,
        schema_name: str = "chat.schema.json",
        image_paths=None,
        admin_mode: bool = False,
        admin_self_check: bool = False,
    ) -> dict:
        schema = self.repo / "qq-bug-bot" / "schemas" / schema_name
        if not schema.is_file():
            raise WorkerError(f"找不到 Agent 输出 Schema: {schema}")
        args = [
            self.resolve_current_codex_command(),
            "--ask-for-approval", "never",
        ]
        if admin_mode and not admin_self_check:
            args.append("--search")
        args.append("exec")
        if admin_mode:
            self.validate_admin_runtime_settings()
            if not admin_self_check:
                self.validate_admin_workspace()
            args.extend([
                "--model", admin_agent_security.QQ_ADMIN_MODEL,
                "-c",
                'model_reasoning_effort="'
                + admin_agent_security.QQ_ADMIN_REASONING_EFFORT
                + '"',
                "-c", "agents.enabled=true",
                "--ignore-user-config",
            ])
            target_workdir = self.workdir if admin_self_check else self.admin_workspace
            if target_workdir is None:
                raise WorkerError("管理员 Agent 缺少工作区")
            sandbox = "read-only" if admin_self_check else "danger-full-access"
        else:
            model = str(
                self.cfg.get("chat_model") or self.cfg.get("model") or ""
            ).strip()
            if model:
                args.extend(["--model", model])
            target_workdir = self.workdir
            sandbox = "read-only"
        args.extend(["--ephemeral", "--json"])
        if not admin_mode or admin_self_check:
            args.append("--skip-git-repo-check")
        args.extend(["--sandbox", sandbox])
        args.extend([
            "--output-schema", str(schema),
            "-C", str(target_workdir),
        ])
        if admin_mode:
            for image_path in image_paths or []:
                args.extend(["--image", str(image_path)])
            # 管理员原文经 stdin 传入，避免出现在 Windows 进程命令行中。
            args.append("-")
            input_text = prompt
        else:
            args.append(prompt)
            for image_path in image_paths or []:
                args.extend(["--image", str(image_path)])
            input_text = None
        env_extra = {}
        codex_proxy = str(self.cfg.get("codex_proxy") or "").strip()
        if codex_proxy:
            env_extra["HTTP_PROXY"] = codex_proxy
            env_extra["HTTPS_PROXY"] = codex_proxy
        result = run_process(
            args,
            cwd=target_workdir,
            timeout=max(
                30,
                int(
                    self.cfg.get(
                        "admin_agent_timeout_seconds"
                        if admin_mode else "chat_timeout_seconds",
                        7200 if admin_mode else 300,
                    )
                ),
            ),
            input_text=input_text,
            env_extra=env_extra,
            remove_env_keys=(
                self.sensitive_environment_names() if admin_mode else None
            ),
        )
        if result.returncode != 0:
            detail = (result.stderr or result.stdout or "未知错误").strip()
            detail = admin_agent_security.redact_sensitive_text(detail[-2000:])
            raise WorkerError(f"聊天 Codex 执行失败（{result.returncode}）：{detail}")
        messages = []
        for line in result.stdout.splitlines():
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                continue
            if (
                isinstance(event, dict)
                and event.get("type") == "item.completed"
                and event.get("item", {}).get("type") == "agent_message"
            ):
                messages.append(event.get("item", {}).get("text"))
        messages = [item for item in messages if isinstance(item, str)]
        if not messages:
            raise WorkerError("聊天 Codex 没有返回最终结构化消息")
        try:
            value = json.loads(messages[-1])
        except json.JSONDecodeError as exc:
            raise WorkerError(f"聊天 Codex 最终消息不是 JSON: {exc}") from exc
        if not isinstance(value, dict):
            raise WorkerError("聊天 Codex 最终消息必须是 JSON 对象")
        return value

    @staticmethod
    def _validate_media_item(item: dict) -> tuple[str, int, str]:
        item = item or {}
        name = str(item.get("name") or "")
        stem, dot, extension = name.partition(".")
        if (
            len(stem) != 32
            or any(ch not in "0123456789abcdef" for ch in stem)
            or dot != "."
            or extension not in ("png", "jpg", "webp")
        ):
            raise WorkerError("服务器返回了无效图片文件名")
        size = int((item or {}).get("size") or 0)
        maximum = max(
            64 * 1024,
            int(item.get("max_size") or 20 * 1024 * 1024),
        )
        if size <= 0 or size > maximum:
            raise WorkerError("服务器返回了无效图片大小")
        digest = str((item or {}).get("sha256") or "")
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise WorkerError("服务器返回了无效图片摘要")
        return name, size, digest

    def prepare_images(self, job: dict):
        media = list(job.get("media") or [])
        if not media:
            return None, []
        if os.name == "nt" and self.media_root.drive.upper() != "E:":
            raise WorkerError("QQ 识图临时目录必须位于 E 盘")
        self.media_root.mkdir(parents=True, exist_ok=True)
        token = str(job.get("claim_token") or "")[:12]
        job_dir = (self.media_root / f"job-{int(job['id'])}-{token}").resolve()
        if job_dir.parent != self.media_root:
            raise WorkerError("QQ 识图临时目录越界")
        if job_dir.exists():
            shutil.rmtree(job_dir)
        job_dir.mkdir(parents=True)
        images = []
        try:
            for item in media[:8]:
                name, expected_size, expected_digest = self._validate_media_item(item)
                local_path = job_dir / name
                remote_path = PurePosixPath(
                    str(self.cfg["remote_bot_dir"]), "data", "media", name
                )
                result = run_process(
                    [
                        "scp", "-o", "BatchMode=yes", "-o", "ConnectTimeout=15",
                        f"{self.cfg['server']}:{remote_path}", str(local_path),
                    ],
                    timeout=90,
                )
                require_success(result, "下载 QQ 识图临时文件")
                data = local_path.read_bytes()
                if len(data) != expected_size:
                    raise WorkerError("QQ 图片大小校验失败")
                if hashlib.sha256(data).hexdigest() != expected_digest:
                    raise WorkerError("QQ 图片摘要校验失败")
                images.append(local_path)
            return job_dir, images
        except Exception:
            shutil.rmtree(job_dir, ignore_errors=True)
            raise

    def cleanup_local_media(self) -> None:
        if os.name == "nt" and self.media_root.drive.upper() != "E:":
            raise WorkerError("QQ 识图临时目录必须位于 E 盘")
        if not self.media_root.is_dir():
            return
        for child in self.media_root.iterdir():
            if child.is_dir() and re.fullmatch(
                r"job-[0-9]+-[0-9a-f]{1,12}", child.name
            ):
                shutil.rmtree(child, ignore_errors=True)

    def process_job(self, job: dict) -> None:
        chat_id = int(job["id"])
        kind = str(job.get("kind") or "chat")
        self.log(f"开始处理{kind} #{chat_id}")
        media_dir = None
        try:
            if kind == "admin_agent":
                if self.mode != "admin":
                    raise WorkerError("普通聊天工作器拒绝管理员任务")
                admin_agent_security.validate_qq_admin_job(job)
                media_dir, image_paths = self.prepare_images(job)
            else:
                media_dir, image_paths = self.prepare_images(job)
            if kind == "admin_agent":
                result = self.run_codex(
                    chat_protocol.build_admin_agent_prompt(job),
                    image_paths=image_paths,
                    admin_mode=True,
                )
                reply = admin_agent_security.safe_qq_admin_reply(
                    result.get("reply")
                )
                self.bridge(
                    "chat-complete",
                    {
                        "chat_id": chat_id,
                        "claim_token": job["claim_token"],
                        "reply": reply,
                    },
                )
                self.log(f"管理员任务 #{chat_id} 已完成")
            elif kind == "bug_intake":
                if self.mode != "chat":
                    raise WorkerError("管理员工作器拒绝 Bug 检查任务")
                result = self.run_codex(
                    chat_protocol.build_bug_intake_prompt(job),
                    "bug-intake.schema.json",
                    image_paths,
                )
                decision = str(result.get("decision") or "").strip()
                description = str(
                    result.get("cleaned_description") or ""
                ).strip()
                reply = str(result.get("reply") or "").strip()
                if decision not in ("record", "clarify", "ignore"):
                    raise WorkerError("Bug 检查返回了无效 decision")
                if decision == "record" and not description:
                    raise WorkerError("Bug 检查未返回可记录的问题描述")
                if decision == "clarify" and not reply:
                    raise WorkerError("Bug 检查未返回具体追问")
                completed = self.bridge(
                    "bug-intake-complete",
                    {
                        "chat_id": chat_id,
                        "claim_token": job["claim_token"],
                        "decision": decision,
                        "cleaned_description": description,
                        "reply": reply,
                    },
                )
                self.log(
                    f"Bug检查 #{chat_id} 已完成：{decision} "
                    f"feedback={completed.get('feedback_id')}"
                )
            else:
                if self.mode != "chat":
                    raise WorkerError("管理员工作器拒绝普通聊天任务")
                result = self.run_codex(
                    chat_protocol.build_chat_prompt(job),
                    image_paths=image_paths,
                )
                reply = str(result.get("reply") or "").strip()
                if not reply or len(reply) > 500:
                    raise WorkerError("聊天 Codex 返回的 reply 长度无效")
                self.bridge(
                    "chat-complete",
                    {
                        "chat_id": chat_id,
                        "claim_token": job["claim_token"],
                        "reply": reply,
                    },
                )
                self.log(f"聊天 #{chat_id} 已完成")
        except admin_agent_security.SecurityPolicyError as exc:
            error = admin_agent_security.redact_sensitive_text(str(exc))[:1000]
            self.log(f"管理员任务 #{chat_id} 来源校验失败，已静默隔离：{error}")
            try:
                self.bridge(
                    "admin-reject",
                    {
                        "chat_id": chat_id,
                        "claim_token": job["claim_token"],
                        "error": error,
                    },
                )
            except Exception as bridge_exc:
                self.log(
                    f"管理员任务 #{chat_id} 隔离失败："
                    + admin_agent_security.redact_sensitive_text(str(bridge_exc))
                )
        except Exception as exc:
            error = admin_agent_security.redact_sensitive_text(str(exc))[:1000]
            self.log(f"聊天 #{chat_id} 处理失败：{error}")
            try:
                self.bridge(
                    "chat-release",
                    {
                        "chat_id": chat_id,
                        "claim_token": job["claim_token"],
                        "error": error,
                        "max_attempts": max(
                            1,
                            int(
                                self.cfg.get(
                                    "admin_agent_max_attempts"
                                    if self.mode == "admin"
                                    else "chat_max_attempts",
                                    3,
                                )
                            ),
                        ),
                    },
                )
            except Exception as bridge_exc:
                self.log(
                    f"聊天 #{chat_id} 释放失败："
                    + admin_agent_security.redact_sensitive_text(str(bridge_exc))
                )
        finally:
            if media_dir:
                shutil.rmtree(media_dir, ignore_errors=True)

    def self_check(self) -> None:
        for name in ("ssh",):
            if not shutil.which(name):
                raise WorkerError(f"未找到命令: {name}")
        self.bridge("status")
        if self.mode == "admin":
            self.validate_admin_runtime_settings()
            result = self.run_codex(
                "不要读取文件、运行命令、调用工具或访问网络。"
                "仅按 Schema 输出 reply 字段，内容为‘管理员 Agent 自检通过’。",
                admin_mode=True,
                admin_self_check=True,
            )
            reply = admin_agent_security.safe_qq_admin_reply(result.get("reply"))
            if "管理员 Agent 自检通过" not in reply:
                raise WorkerError("管理员 Codex 自检返回意外结果")
            self.log("自检通过")
            return
        self.resolve_current_codex_command()
        result = self.run_codex(
            "不要运行命令、读取或修改文件。仅按 Schema 输出 reply 字段，内容为‘聊天自检通过’。",
        )
        reply = str(result.get("reply") or "")
        if "自检通过" not in reply:
            raise WorkerError("聊天 Codex 自检返回意外结果")
        self.log("自检通过")

    def run_once(self) -> bool:
        # 先确认本机依赖，再领取有次数上限的远端任务。CLI 更新或配置切换期间
        # 保持任务排队，避免同一故障在数秒内耗尽全部尝试次数。
        self.resolve_current_codex_command()
        if self.mode != "admin":
            data = self.bridge("chat-claim")
            job = data.get("job")
            if not job:
                return False
            self.process_job(job)
            return True

        assert self.admin_workspace is not None
        self.validate_admin_runtime_settings()
        self.validate_admin_workspace()
        with RepositoryWorkspaceLock(
            self.admin_workspace, self.workspace_lock_root
        ) as repository_lock:
            if not repository_lock.acquired:
                return False
            data = self.bridge("admin-claim")
            job = data.get("job")
            if not job:
                return False
            self.process_job(job)
            return True

    def run_forever(self) -> None:
        poll_key = (
            "admin_agent_poll_seconds"
            if self.mode == "admin"
            else "chat_poll_seconds"
        )
        interval = max(2, int(self.cfg.get(poll_key, 5)))
        self.cleanup_local_media()
        self.log(f"聊天工作器启动：{self.worker_id}")
        while True:
            if self.source_fingerprint() != self._loaded_source_fingerprint:
                self.log("工作器代码已更新，退出当前进程并等待计划任务重启")
                return
            try:
                if self.run_once():
                    continue
            except Exception as exc:
                self.log(f"聊天工作循环异常：{exc}")
            time.sleep(interval)


def main() -> int:
    parser = argparse.ArgumentParser(description="GrandUMI QQ 群聊 Agent 工作器")
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--self-check", action="store_true")
    parser.add_argument("--media-root", type=Path)
    parser.add_argument("--mode", choices=("chat", "admin"), default="chat")
    parser.add_argument("--admin-workspace", type=Path)
    parser.add_argument("--workspace-lock-root", type=Path)
    args = parser.parse_args()
    try:
        config_path = args.config.resolve()
        worker = ChatAgentWorker(
            load_config(config_path),
            args.media_root.resolve() if args.media_root else None,
            args.mode,
            args.admin_workspace.resolve() if args.admin_workspace else None,
            args.workspace_lock_root.resolve() if args.workspace_lock_root else None,
            config_path,
        )
        if args.self_check:
            worker.self_check()
        elif args.once:
            worker.run_once()
        else:
            worker.run_forever()
        return 0
    except (OSError, ValueError, WorkerError, subprocess.TimeoutExpired) as exc:
        print(f"[错误] {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
