# -*- coding: utf-8 -*-
"""GrandUMI QQ 群聊只读 Agent 常驻工作器。"""

import argparse
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
    def __init__(self, cfg: dict):
        self.cfg = cfg
        self.repo = Path(cfg["repository_root"]).resolve()
        self.logs_root = Path(cfg["logs_root"]).resolve()
        self.logs_root.mkdir(parents=True, exist_ok=True)
        self.workdir = self.logs_root / "chat-sandbox"
        self.workdir.mkdir(parents=True, exist_ok=True)
        configured_id = str(cfg.get("chat_worker_id") or "").strip()
        raw_id = configured_id or f"{socket.gethostname()}-chat-{os.getpid()}"
        self.worker_id = re.sub(r"[^A-Za-z0-9._-]", "-", raw_id)[:80]
        self.log_file = self.logs_root / "chat-agent-worker.log"

    def log(self, message: str) -> None:
        line = f"{datetime.now().isoformat(timespec='seconds')} {message}"
        print(line, flush=True)
        with self.log_file.open("a", encoding="utf-8") as file:
            file.write(line + "\n")

    def bridge(self, command: str, payload: dict | None = None) -> dict:
        if command not in ("chat-claim", "chat-complete", "chat-release", "status"):
            raise WorkerError(f"非法聊天桥接命令: {command}")
        suffix = command
        if command == "chat-claim":
            lease = max(120, int(self.cfg.get("chat_lease_seconds", 900)))
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

    def run_codex(self, prompt: str) -> str:
        schema = self.repo / "qq-bug-bot" / "schemas" / "chat.schema.json"
        if not schema.is_file():
            raise WorkerError(f"找不到聊天输出 Schema: {schema}")
        args = [
            resolve_codex_command(str(self.cfg.get("codex_command") or "codex")),
            "--ask-for-approval", "never",
            "exec", "--ephemeral", "--json", "--skip-git-repo-check",
            "--sandbox", "read-only",
            "--output-schema", str(schema),
            "-C", str(self.workdir),
            prompt,
        ]
        model = str(self.cfg.get("chat_model") or self.cfg.get("model") or "").strip()
        if model:
            args[4:4] = ["--model", model]
        env_extra = {}
        codex_proxy = str(self.cfg.get("codex_proxy") or "").strip()
        if codex_proxy:
            env_extra["HTTP_PROXY"] = codex_proxy
            env_extra["HTTPS_PROXY"] = codex_proxy
        result = run_process(
            args,
            cwd=self.workdir,
            timeout=max(30, int(self.cfg.get("chat_timeout_seconds", 300))),
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
        reply = str(value.get("reply") or "").strip()
        if not reply or len(reply) > 500:
            raise WorkerError("聊天 Codex 返回的 reply 长度无效")
        return reply

    def process_job(self, job: dict) -> None:
        chat_id = int(job["id"])
        self.log(f"开始处理聊天 #{chat_id}")
        try:
            reply = self.run_codex(chat_protocol.build_chat_prompt(job))
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
                            1, int(self.cfg.get("chat_max_attempts", 3))
                        ),
                    },
                )
            except Exception as bridge_exc:
                self.log(f"聊天 #{chat_id} 释放失败：{bridge_exc}")

    def self_check(self) -> None:
        for name in ("ssh",):
            if not shutil.which(name):
                raise WorkerError(f"未找到命令: {name}")
        resolve_codex_command(str(self.cfg.get("codex_command") or "codex"))
        self.bridge("status")
        reply = self.run_codex(
            "不要运行命令或读取文件。仅按 Schema 输出 reply 字段，内容为‘聊天自检通过’。"
        )
        if "自检通过" not in reply:
            raise WorkerError("聊天 Codex 自检返回意外结果")
        self.log("自检通过")

    def run_once(self) -> bool:
        data = self.bridge("chat-claim")
        job = data.get("job")
        if not job:
            return False
        self.process_job(job)
        return True

    def run_forever(self) -> None:
        interval = max(2, int(self.cfg.get("chat_poll_seconds", 5)))
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
    args = parser.parse_args()
    try:
        worker = ChatAgentWorker(load_config(args.config.resolve()))
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
