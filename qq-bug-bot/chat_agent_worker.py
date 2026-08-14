# -*- coding: utf-8 -*-
"""GrandUMI QQ 普通只读聊天与管理员全权限 Agent 常驻工作器。"""

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
from agent_worker import (
    BRIDGE_PREFIX,
    WorkerError,
    is_windows_dll_init_failure,
    load_config,
    require_success,
    resolve_codex_command,
    run_process,
)


class ChatAgentWorker:
    def __init__(
        self,
        cfg: dict,
        media_root: Path | None = None,
        mode: str = "chat",
        admin_workspace: Path | None = None,
    ):
        self.cfg = cfg
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

    def log(self, message: str) -> None:
        line = f"{datetime.now().isoformat(timespec='seconds')} {message}"
        print(line, flush=True)
        with self.log_file.open("a", encoding="utf-8") as file:
            file.write(line + "\n")

    def bridge(self, command: str, payload: dict | None = None) -> dict:
        if command not in (
            "chat-claim", "admin-claim", "chat-complete", "bug-intake-complete",
            "chat-release", "status",
        ):
            raise WorkerError(f"非法聊天桥接命令: {command}")
        suffix = command
        if command in ("chat-claim", "admin-claim"):
            lease_key = (
                "admin_agent_lease_seconds"
                if command == "admin-claim"
                else "chat_lease_seconds"
            )
            default_lease = 7200 if command == "admin-claim" else 900
            lease = max(120, int(self.cfg.get(lease_key, default_lease)))
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
    ) -> dict:
        schema = self.repo / "qq-bug-bot" / "schemas" / schema_name
        if not schema.is_file():
            raise WorkerError(f"找不到 Agent 输出 Schema: {schema}")
        args = [
            resolve_codex_command(str(self.cfg.get("codex_command") or "codex")),
            "--ask-for-approval", "never",
        ]
        if admin_mode:
            args.extend([
                "--search",
                "exec",
                "--dangerously-bypass-approvals-and-sandbox",
            ])
        else:
            args.append("exec")
        model = str(self.cfg.get("chat_model") or self.cfg.get("model") or "").strip()
        if model:
            args.extend(["--model", model])
        target_workdir = self.admin_workspace if admin_mode else self.workdir
        args.extend(["--ephemeral", "--json", "--skip-git-repo-check"])
        if not admin_mode:
            args.extend(["--sandbox", "read-only"])
        args.extend([
            "--output-schema", str(schema),
            "-C", str(target_workdir),
        ])
        args.append(prompt)
        for image_path in image_paths or []:
            args.extend(["--image", str(image_path)])
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
            env_extra=env_extra,
        )
        if result.returncode != 0:
            detail = (result.stderr or result.stdout or "未知错误").strip()
            raise WorkerError(f"聊天 Codex 执行失败（{result.returncode}）：{detail[-2000:]}")
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
            media_dir, image_paths = self.prepare_images(job)
            if kind == "admin_agent":
                if self.mode != "admin":
                    raise WorkerError("普通聊天工作器拒绝管理员任务")
                result = self.run_codex(
                    chat_protocol.build_admin_agent_prompt(job),
                    image_paths=image_paths,
                    admin_mode=True,
                )
                reply = str(result.get("reply") or "").strip()
                if not reply or len(reply) > 500:
                    raise WorkerError("管理员 Codex 返回的 reply 长度无效")
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
        except Exception as exc:
            self.log(f"聊天 #{chat_id} 处理失败：{exc}")
            try:
                self.bridge(
                    "chat-release",
                    {
                        "chat_id": chat_id,
                        "claim_token": job["claim_token"],
                        "error": str(exc)[:1000],
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
                self.log(f"聊天 #{chat_id} 释放失败：{bridge_exc}")
        finally:
            if media_dir:
                shutil.rmtree(media_dir, ignore_errors=True)

    def self_check(self) -> None:
        for name in ("ssh",):
            if not shutil.which(name):
                raise WorkerError(f"未找到命令: {name}")
        resolve_codex_command(str(self.cfg.get("codex_command") or "codex"))
        self.bridge("status")
        admin_mode = self.mode == "admin"
        result = self.run_codex(
            "不要运行命令、读取或修改文件。仅按 Schema 输出 reply 字段，内容为‘聊天自检通过’。",
            admin_mode=admin_mode,
        )
        reply = str(result.get("reply") or "")
        if "自检通过" not in reply:
            raise WorkerError("聊天 Codex 自检返回意外结果")
        self.log("自检通过")

    def run_once(self) -> bool:
        command = "admin-claim" if self.mode == "admin" else "chat-claim"
        data = self.bridge(command)
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
    args = parser.parse_args()
    try:
        worker = ChatAgentWorker(
            load_config(args.config.resolve()),
            args.media_root.resolve() if args.media_root else None,
            args.mode,
            args.admin_workspace.resolve() if args.admin_workspace else None,
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
