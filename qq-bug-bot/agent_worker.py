# -*- coding: utf-8 -*-
"""GrandUMI 本机 Bug 自动修复工作器。

队列与 QQ 会话保存在服务器机器人数据库；本机只通过 SSH 领取/回写任务。
模型仅在独立 Git worktree 与 workspace-write 沙箱中工作；普通工作器只能留下
待审提交，不能合并、推送或部署。
"""

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
from uuid import uuid4

import agent_protocol

BASE_DIR = Path(__file__).resolve().parent
DEFAULT_CONFIG = Path(
    os.environ.get(
        "BUG_AGENT_CONFIG_PATH",
        str(BASE_DIR / "agent-worker.json"),
    )
)
BRIDGE_PREFIX = "AGENT_BRIDGE_JSON="
WINDOWS_DLL_INIT_FAILED = 0xC0000142
WINDOWS_NO_WINDOW = (
    getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
)


class WorkerError(RuntimeError):
    pass


class ReviewRejected(WorkerError):
    """独立复核发现可用于修订的问题。"""

    def __init__(self, review: dict):
        self.review = review
        issues = "；".join(str(x) for x in review.get("issues", []))
        super().__init__(f"独立复核未通过: {issues or review.get('summary')}")


def is_windows_dll_init_failure(returncode: int) -> bool:
    """识别 Windows 创建子进程前的 DLL 初始化失败；远端命令尚未执行。"""
    return os.name == "nt" and (returncode & 0xFFFFFFFF) == WINDOWS_DLL_INIT_FAILED


def resolve_codex_command(command: str) -> str:
    """Windows 下绕过 npm 的 .cmd，避免多行提示词被 cmd.exe 截断。"""
    resolved = shutil.which(command)
    if not resolved:
        candidate = Path(command).resolve()
        if not candidate.is_file():
            raise WorkerError(f"未找到 Codex 命令: {command}")
        resolved = str(candidate)
    path = Path(resolved)
    if os.name != "nt" or path.suffix.lower() not in (".cmd", ".bat"):
        return str(path)

    package_root = path.parent / "node_modules" / "@openai" / "codex"
    native = sorted(
        package_root.glob(
            "node_modules/@openai/codex-win32-*/vendor/*/bin/codex.exe"
        )
    )
    if not native:
        raise WorkerError(
            f"Codex 的 Windows 原生执行文件不存在，拒绝通过批处理传递多行提示词: {package_root}"
        )
    return str(native[0].resolve())


def load_config(path: Path) -> dict:
    # Windows PowerShell 5.1 的 Set-Content -Encoding UTF8 会写入 BOM。
    with path.open("r", encoding="utf-8-sig") as file:
        cfg = json.load(file)
    required = ("server", "remote_bot_dir", "repository_root", "jobs_root", "logs_root")
    for key in required:
        if not str(cfg.get(key) or "").strip():
            raise WorkerError(f"配置缺少字段: {key}")
    if not re.fullmatch(r"[A-Za-z0-9._-]+@[A-Za-z0-9.:-]+", cfg["server"]):
        raise WorkerError("server 格式不安全")
    if not re.fullmatch(r"/[A-Za-z0-9._/-]+", cfg["remote_bot_dir"]):
        raise WorkerError("remote_bot_dir 格式不安全")
    return cfg


def subprocess_env(clear_proxy: bool = False) -> dict:
    env = os.environ.copy()
    env["PYTHONIOENCODING"] = "utf-8"
    if clear_proxy:
        for key in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
            env.pop(key, None)
            env.pop(key.lower(), None)
    return env


def run_process(
    args: list[str],
    cwd: Path | None = None,
    timeout: int = 600,
    input_text: str | None = None,
    clear_proxy: bool = False,
    env_extra: dict[str, str] | None = None,
    remove_env_keys=None,
) -> subprocess.CompletedProcess:
    env = subprocess_env(clear_proxy)
    for key in remove_env_keys or ():
        env.pop(str(key), None)
    if env_extra:
        env.update(env_extra)
    prepared_args = list(args)
    if os.name == "nt" and prepared_args:
        # npm/codex 同时安装无扩展名 shim 和 .cmd；CreateProcess 直接命中前者会
        # 报 WinError 5，因此使用 PATHEXT 解析后的实际可执行入口。
        resolved = shutil.which(prepared_args[0])
        if resolved:
            prepared_args[0] = resolved
    return subprocess.run(
        prepared_args,
        cwd=str(cwd) if cwd else None,
        input=input_text,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        env=env,
        creationflags=WINDOWS_NO_WINDOW,
    )


def require_success(result: subprocess.CompletedProcess, label: str) -> str:
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "未知错误").strip()
        raise WorkerError(f"{label}失败（退出码 {result.returncode}）：{detail[-2000:]}")
    return result.stdout


class AgentWorker:
    def __init__(self, cfg: dict):
        self.cfg = cfg
        self.repo = Path(cfg["repository_root"]).resolve()
        self.jobs_root = Path(cfg["jobs_root"]).resolve()
        self.logs_root = Path(cfg["logs_root"]).resolve()
        self.jobs_root.mkdir(parents=True, exist_ok=True)
        self.logs_root.mkdir(parents=True, exist_ok=True)
        configured_id = str(cfg.get("worker_id") or "").strip()
        raw_id = configured_id or f"{socket.gethostname()}-{os.getpid()}"
        self.worker_id = re.sub(r"[^A-Za-z0-9._-]", "-", raw_id)[:80]
        self.log_file = self.logs_root / "agent-worker.log"

    def log(self, message: str) -> None:
        line = f"{datetime.now().isoformat(timespec='seconds')} {message}"
        print(line, flush=True)
        with self.log_file.open("a", encoding="utf-8") as file:
            file.write(line + "\n")

    def bridge(self, command: str, payload: dict | None = None) -> dict:
        if command not in ("claim", "ask", "complete", "release", "status"):
            raise WorkerError(f"非法桥接命令: {command}")
        suffix = command
        if command == "claim":
            lease = max(600, int(self.cfg.get("lease_seconds", 7200)))
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
                self.log(
                    f"服务器桥接 {command} 遇到 Windows 子进程初始化失败，"
                    f"正在重试（{attempt}/3）"
                )
                time.sleep(1)
                continue
            break
        assert result is not None
        require_success(result, f"服务器桥接 {command}")
        for line in reversed(result.stdout.splitlines()):
            if line.startswith(BRIDGE_PREFIX):
                data = json.loads(line[len(BRIDGE_PREFIX):])
                if not data.get("ok"):
                    raise WorkerError(str(data.get("error") or "服务器桥接拒绝请求"))
                return data
        raise WorkerError("服务器桥接未返回结构化结果")

    def sync_origin(self) -> str:
        """同步 GitHub main；直连失败时从已部署测试服获取只读 bundle。"""
        git_proxy = str(self.cfg.get("git_proxy") or "").strip()
        result = run_process(
            [
                "git", "-c", f"http.proxy={git_proxy}",
                "-c", f"https.proxy={git_proxy}",
                "fetch", "origin", "main:refs/remotes/origin/main",
            ],
            cwd=self.repo,
            timeout=120,
            clear_proxy=True,
        )
        if result.returncode != 0:
            self.log("GitHub 直连同步失败，改从测试服获取 main bundle")
            bundle = self.logs_root / "server-main.bundle"
            with bundle.open("wb") as output:
                proc = subprocess.run(
                    [
                        "ssh", "-o", "BatchMode=yes", self.cfg["server"],
                        "git -C /opt/grandumi-test bundle create - main",
                    ],
                    stdout=output,
                    stderr=subprocess.PIPE,
                    timeout=120,
                    creationflags=WINDOWS_NO_WINDOW,
                )
            if proc.returncode != 0:
                detail = proc.stderr.decode("utf-8", errors="replace")
                raise WorkerError(f"测试服 bundle 获取失败：{detail[-1500:]}")
            fetch = run_process(
                ["git", "fetch", str(bundle), "main:refs/remotes/origin/main"],
                cwd=self.repo,
                timeout=120,
            )
            require_success(fetch, "导入测试服 main bundle")
        head = run_process(
            ["git", "rev-parse", "refs/remotes/origin/main"], cwd=self.repo
        )
        return require_success(head, "读取 origin/main").strip()

    def create_worktree(self, job: dict, base: str) -> tuple[Path, str]:
        stamp = datetime.now().strftime("%Y%m%d%H%M%S")
        branch = f"codex/bug-{int(job['id'])}-{stamp}"
        path = self.jobs_root / f"bug-{int(job['id'])}-{stamp}"
        if path.exists():
            raise WorkerError(f"任务目录意外存在: {path}")
        result = run_process(
            ["git", "worktree", "add", "-b", branch, str(path), base],
            cwd=self.repo,
            timeout=120,
        )
        require_success(result, "创建隔离工作区")
        return path, branch

    def run_codex(
        self,
        worktree: Path,
        sandbox: str,
        schema_name: str,
        prompt: str,
        timeout: int,
    ) -> tuple[dict, list[dict]]:
        schema = worktree / "qq-bug-bot" / "schemas" / schema_name
        args = [
            resolve_codex_command(str(self.cfg.get("codex_command") or "codex")),
            "--ask-for-approval", "never",
            "exec", "--ephemeral", "--json",
            "--sandbox", sandbox,
            "--output-schema", str(schema),
            "-C", str(worktree),
            prompt,
        ]
        model = str(self.cfg.get("model") or "").strip()
        if model:
            args[4:4] = ["--model", model]
        env_extra = {}
        shared_modules = str(self.cfg.get("shared_node_modules_path") or "").strip()
        if shared_modules:
            modules_path = Path(shared_modules).resolve()
            env_extra["NODE_PATH"] = str(modules_path)
            env_extra["PATH"] = str(modules_path / ".bin") + os.pathsep + os.environ.get("PATH", "")
        codex_proxy = str(self.cfg.get("codex_proxy") or "").strip()
        if codex_proxy:
            env_extra["HTTP_PROXY"] = codex_proxy
            env_extra["HTTPS_PROXY"] = codex_proxy
        worktree_marker = worktree / ".git"
        marker_before = worktree_marker.read_bytes() if worktree_marker.is_file() else b""
        config_path = self.repo / ".git" / "config"
        config_before = config_path.read_bytes() if config_path.is_file() else b""
        head_before = require_success(
            run_process(["git", "rev-parse", "HEAD"], cwd=worktree),
            "读取 Codex 执行前提交",
        ).strip()
        result = run_process(args, cwd=worktree, timeout=timeout, env_extra=env_extra)
        marker_after = worktree_marker.read_bytes() if worktree_marker.is_file() else b""
        config_after = config_path.read_bytes() if config_path.is_file() else b""
        head_after = require_success(
            run_process(["git", "rev-parse", "HEAD"], cwd=worktree),
            "读取 Codex 执行后提交",
        ).strip()
        if (
            marker_before != marker_after
            or config_before != config_after
            or head_before != head_after
        ):
            raise WorkerError("Codex 运行期间 Git 元数据发生变化，已拒绝继续")
        events: list[dict] = []
        for line in result.stdout.splitlines():
            try:
                value = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(value, dict):
                events.append(value)
        if result.returncode != 0:
            detail = (result.stderr or result.stdout).strip()
            raise WorkerError(f"Codex 执行失败（{result.returncode}）：{detail[-3000:]}")
        messages = [
            event.get("item", {}).get("text")
            for event in events
            if event.get("type") == "item.completed"
            and event.get("item", {}).get("type") == "agent_message"
        ]
        messages = [message for message in messages if isinstance(message, str)]
        if not messages:
            raise WorkerError("Codex 没有返回最终结构化消息")
        try:
            return json.loads(messages[-1]), events
        except json.JSONDecodeError as exc:
            raise WorkerError(f"Codex 最终消息不是 JSON: {exc}") from exc

    @staticmethod
    def git_lines(worktree: Path, args: list[str]) -> list[str]:
        result = run_process(["git", *args], cwd=worktree, timeout=60)
        output = require_success(result, "读取 Git 改动")
        return [line for line in output.splitlines() if line.strip()]

    def changed_files(self, worktree: Path, committed_base: str | None = None) -> list[str]:
        if committed_base:
            lines = self.git_lines(
                worktree,
                ["-c", "core.quotepath=false", "diff", "--name-only", f"{committed_base}..HEAD"],
            )
            return sorted(set(line.replace("\\", "/") for line in lines))
        lines = self.git_lines(
            worktree,
            [
                "-c", "core.quotepath=false", "status", "--porcelain=v1",
                "--untracked-files=all",
            ],
        )
        paths = []
        for line in lines:
            path = line[3:]
            if " -> " in path:
                path = path.split(" -> ", 1)[1]
            paths.append(path.strip('"').replace("\\", "/"))
        return sorted(set(paths))

    def validate_changes(
        self,
        worktree: Path,
        base_ref: str = "HEAD",
        committed: bool = False,
    ) -> tuple[list[str], list[str]]:
        files = self.changed_files(worktree, base_ref if committed else None)
        if not files:
            raise WorkerError("Agent 未产生任何代码改动")
        max_files = int(self.cfg.get("max_changed_files", 30))
        if len(files) > max_files:
            raise WorkerError(f"改动文件数 {len(files)} 超过上限 {max_files}")
        if committed:
            statuses = self.git_lines(
                worktree,
                ["-c", "core.quotepath=false", "diff", "--name-status", f"{base_ref}..HEAD"],
            )
        else:
            statuses = self.git_lines(
                worktree,
                [
                    "-c", "core.quotepath=false", "status", "--porcelain=v1",
                    "--untracked-files=all",
                ],
            )
        if any(
            (line.split("\t", 1)[0] if committed else line[:2]).strip().startswith(("D", "R"))
            for line in statuses
        ):
            raise WorkerError("自动修复不允许删除或重命名文件")
        for name in files:
            lowered = name.lower()
            basename = Path(lowered).name
            if (
                name in agent_protocol.BLOCKED_EXACT
                or lowered.startswith(agent_protocol.BLOCKED_PREFIXES)
                or basename in agent_protocol.BLOCKED_BASENAMES
                or lowered.endswith(agent_protocol.BLOCKED_SUFFIXES)
            ):
                raise WorkerError(f"改动触及禁止路径: {name}")
            if not lowered.endswith(agent_protocol.ALLOWED_SUFFIXES):
                raise WorkerError(f"改动文件类型不在白名单: {name}")
            full = worktree / Path(name)
            if full.exists() and (full.is_symlink() or full.stat().st_size > 1_500_000):
                raise WorkerError(f"改动文件为链接或体积过大: {name}")
        if not any(
            name.startswith("changelog-cache/pending/") and name.endswith(".md")
            for name in files
        ):
            raise WorkerError("缺少 changelog-cache/pending 更新记录")
        if any(name.startswith("服务端WebSocket/") for name in files) and not any(
            name.startswith("服务端WebSocket.Tests/") for name in files
        ):
            raise WorkerError("后端修复没有对应的回归测试")

        # 让新增文件以 intent-to-add 进入 diff，确保行数和二进制门禁覆盖它们。
        intent = run_process(
            ["git", "add", "--intent-to-add", "--", *files],
            cwd=worktree,
            timeout=120,
        )
        require_success(intent, "登记新增文件以供审计")

        range_arg = f"{base_ref}..HEAD" if committed else "HEAD"
        numstat = self.git_lines(worktree, ["diff", "--numstat", range_arg])
        total = 0
        for line in numstat:
            parts = line.split("\t", 2)
            if len(parts) < 3 or parts[0] == "-" or parts[1] == "-":
                raise WorkerError(f"检测到二进制或不可审计改动: {line}")
            total += int(parts[0]) + int(parts[1])
        max_lines = int(self.cfg.get("max_diff_lines", 1500))
        if total > max_lines:
            raise WorkerError(f"改动行数 {total} 超过上限 {max_lines}")
        return files, self.required_tests(files)

    @staticmethod
    def required_tests(files: list[str]) -> list[str]:
        tests = []
        if any(
            name.startswith("服务端WebSocket/")
            or name.startswith("服务端WebSocket.Tests/")
            for name in files
        ):
            tests.append("dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj")
        if any(name.startswith("opcgpro-web/") for name in files):
            tests.extend(
                f"node {name}"
                for name in files
                if name.startswith("opcgpro-web/") and name.endswith(".test.mjs")
            )
            tests.append("npm.cmd --prefix opcgpro-web run build")
        if not tests:
            tests.append("git diff --check")
        return tests

    @staticmethod
    def diff_fingerprint(worktree: Path, base_ref: str = "HEAD") -> str:
        result = run_process(
            ["git", "diff", "--binary", base_ref], cwd=worktree, timeout=60
        )
        data = require_success(result, "生成审查指纹")
        return hashlib.sha256(data.encode("utf-8", errors="replace")).hexdigest()

    def create_review_worktree(
        self,
        worktree: Path,
        feedback_id: int,
        base_ref: str = "HEAD",
    ) -> Path:
        """把待审 diff 复制到可丢弃 worktree，避免复核污染原修复。"""
        committed = base_ref != "HEAD"
        files = self.changed_files(
            worktree, committed_base=base_ref if committed else None
        )
        if not files:
            raise WorkerError("复核前未找到待审改动")
        range_arg = f"{base_ref}..HEAD" if committed else "HEAD"
        patch = require_success(
            run_process(
                ["git", "diff", "--binary", range_arg],
                cwd=worktree,
                timeout=120,
            ),
            "生成复核副本补丁",
        )
        if not patch.strip():
            raise WorkerError("待审改动无法生成复核补丁")
        base = require_success(
            run_process(["git", "rev-parse", base_ref], cwd=worktree),
            "读取复核基线",
        ).strip()
        stamp = datetime.now().strftime("%Y%m%d%H%M%S")
        reviewtree = self.jobs_root / (
            f"review-{feedback_id}-{stamp}-{uuid4().hex[:8]}"
        )
        add = run_process(
            ["git", "worktree", "add", "--detach", str(reviewtree), base],
            cwd=self.repo,
            timeout=120,
        )
        require_success(add, "创建隔离复核工作区")
        try:
            apply = run_process(
                ["git", "apply", "--whitespace=nowarn", "-"],
                cwd=reviewtree,
                timeout=120,
                input_text=patch,
            )
            require_success(apply, "复制待审改动")
            intent = run_process(
                ["git", "add", "--intent-to-add", "--", *files],
                cwd=reviewtree,
                timeout=120,
            )
            require_success(intent, "登记复核副本新文件")
            return reviewtree
        except Exception:
            self.cleanup_review_worktree(reviewtree)
            raise

    def cleanup_review_worktree(self, reviewtree: Path) -> None:
        result = run_process(
            ["git", "worktree", "remove", "--force", str(reviewtree)],
            cwd=self.repo,
            timeout=120,
        )
        if result.returncode != 0:
            self.log(
                f"复核工作区清理失败，已保留 {reviewtree}: "
                f"{result.stderr.strip()}"
            )

    @staticmethod
    def verify_review_events(
        review: dict, events: list[dict], required_tests: list[str]
    ) -> None:
        reported = {
            str(item.get("command") or "").strip(): bool(item.get("passed"))
            for item in review.get("tests", [])
            if isinstance(item, dict)
        }
        executed = []
        for event in events:
            item = event.get("item", {})
            if item.get("type") == "command_execution" and event.get("type") == "item.completed":
                command = str(item.get("command") or "")
                exit_code = item.get("exit_code")
                executed.append((command, exit_code))
        for command in required_tests:
            if reported.get(command) is not True:
                raise WorkerError(f"复核结果未确认测试通过: {command}")
            matches = [entry for entry in executed if command in entry[0]]
            if not matches or not any(code == 0 for _, code in matches):
                raise WorkerError(f"Codex 事件中没有成功执行指定测试: {command}")

    def review(
        self,
        worktree: Path,
        job: dict,
        triage: dict,
        required_tests: list[str],
        base_ref: str = "HEAD",
    ) -> dict:
        reviewtree = self.create_review_worktree(
            worktree, int(job["id"]), base_ref
        )
        try:
            before = self.diff_fingerprint(reviewtree)
            review, events = self.run_codex(
                reviewtree,
                "workspace-write",
                "review.schema.json",
                agent_protocol.build_review_prompt(job, triage, required_tests),
                int(self.cfg.get("review_timeout_seconds", 3600)),
            )
            after = self.diff_fingerprint(reviewtree)
            if before != after:
                isolated = dict(review)
                issues = [
                    str(issue) for issue in isolated.get("issues", [])
                ]
                issues.append(
                    "独立复核修改了隔离副本；该修改已丢弃，"
                    "请修复 Agent 在原工作区根据复核意见完成修订"
                )
                isolated["approved"] = False
                isolated["issues"] = issues
                raise ReviewRejected(isolated)
            if not review.get("approved") or review.get("risk_level") == "high":
                raise ReviewRejected(review)
            self.verify_review_events(review, events, required_tests)
            return review
        finally:
            self.cleanup_review_worktree(reviewtree)

    def validate_review_and_test(
        self,
        worktree: Path,
        job: dict,
        triage: dict,
    ) -> tuple[list[str], list[str]]:
        """在同一工作区内完成门禁、复核和一次有界修订。"""
        max_revisions = max(0, int(self.cfg.get("max_review_revisions", 1)))
        revision_count = 0
        while True:
            files, tests = self.validate_changes(worktree)
            self.prepare_test_environment(tests)
            try:
                self.review(worktree, job, triage, tests)
            except ReviewRejected as exc:
                if revision_count >= max_revisions:
                    raise
                revision_count += 1
                self.log(
                    f"反馈 #{int(job['id'])} 独立复核未通过，"
                    f"在当前工作区自动修订（{revision_count}/{max_revisions}）"
                )
                revision, _ = self.run_codex(
                    worktree,
                    "workspace-write",
                    "fix.schema.json",
                    agent_protocol.build_revision_prompt(job, triage, exc.review),
                    int(self.cfg.get("fix_timeout_seconds", 7200)),
                )
                if revision.get("status") != "fixed":
                    raise WorkerError(
                        "复核修订未完成: "
                        + str(revision.get("summary") or "Agent 返回 unable")
                    )
                continue
            self.run_required_tests(worktree, tests)
            return files, tests

    def prepare_test_environment(self, required_tests: list[str]) -> None:
        if "npm.cmd --prefix opcgpro-web run build" in required_tests:
            shared = str(self.cfg.get("shared_node_modules_path") or "").strip()
            if not shared or not (Path(shared) / ".bin" / "next.cmd").exists():
                raise WorkerError("前端共享 node_modules 未准备完成，无法安全运行构建")

    def run_required_tests(self, worktree: Path, required_tests: list[str]) -> None:
        """由可信工作器以固定参数复跑门禁测试，不执行模型生成的命令。"""
        commands = {
            "dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj": [
                "dotnet", "test", "服务端WebSocket.Tests/GrandUMIServer.Tests.csproj",
            ],
            "npm.cmd --prefix opcgpro-web run build": [
                "npm.cmd", "--prefix", "opcgpro-web", "run", "build",
            ],
            "git diff --check": ["git", "diff", "--check"],
        }
        env_extra = {}
        shared = str(self.cfg.get("shared_node_modules_path") or "").strip()
        if shared:
            modules_path = Path(shared).resolve()
            env_extra["NODE_PATH"] = str(modules_path)
            env_extra["PATH"] = (
                str(modules_path / ".bin")
                + os.pathsep
                + os.environ.get("PATH", "")
            )
        for command in required_tests:
            args = commands.get(command)
            if args is None and command.startswith("node "):
                test_path = command[5:]
                if (
                    re.fullmatch(r"opcgpro-web/[A-Za-z0-9_./-]+\.test\.mjs", test_path)
                    and ".." not in Path(test_path).parts
                ):
                    args = ["node", test_path]
            if args is None:
                raise WorkerError(f"没有可信测试映射: {command}")
            result = run_process(
                args,
                cwd=worktree,
                timeout=int(self.cfg.get("test_timeout_seconds", 3600)),
                env_extra=env_extra,
            )
            require_success(result, f"可信测试 {command}")

    def commit_changes(self, worktree: Path, files: list[str], feedback_id: int) -> str:
        add = run_process(["git", "add", "--", *files], cwd=worktree, timeout=120)
        require_success(add, "暂存任务文件")
        commit = run_process(
            ["git", "commit", "-m", f"fix: 自动修复反馈 #{feedback_id}"],
            cwd=worktree,
            timeout=120,
        )
        require_success(commit, "提交自动修复")
        head = run_process(["git", "rev-parse", "HEAD"], cwd=worktree)
        return require_success(head, "读取修复提交").strip()

    def cleanup(self, worktree: Path, branch: str, merged: bool) -> None:
        result = run_process(
            ["git", "worktree", "remove", "--force", str(worktree)],
            cwd=self.repo,
            timeout=120,
        )
        if result.returncode != 0:
            self.log(f"工作区清理失败，已保留 {worktree}: {result.stderr.strip()}")
            return
        delete_flag = "-d" if merged else "-D"
        run_process(["git", "branch", delete_flag, branch], cwd=self.repo, timeout=60)

    def ask_owner(self, job: dict, question: str, summary: str) -> None:
        self.bridge(
            "ask",
            {
                "feedback_id": int(job["id"]),
                "claim_token": job["agent_claim_token"],
                "question": question,
                "summary": summary,
            },
        )

    def complete(
        self, job: dict, state: str, summary: str, commit: str = ""
    ) -> None:
        self.bridge(
            "complete",
            {
                "feedback_id": int(job["id"]),
                "claim_token": job["agent_claim_token"],
                "state": state,
                "summary": summary,
                "commit": commit,
                "result_url": (
                    "https://test.grand-umi.com/" if state == "fixed" else ""
                ),
            },
        )

    def release(self, job: dict, summary: str) -> None:
        self.bridge(
            "release",
            {
                "feedback_id": int(job["id"]),
                "claim_token": job["agent_claim_token"],
                "summary": summary[:1800],
            },
        )

    def process_job(self, job: dict) -> None:
        feedback_id = int(job["id"])
        self.log(f"开始处理反馈 #{feedback_id}")
        base = self.sync_origin()
        worktree, branch = self.create_worktree(job, base)
        merged = False
        retained_for_review = False
        try:
            triage, _ = self.run_codex(
                worktree,
                "read-only",
                "triage.schema.json",
                agent_protocol.build_triage_prompt(job),
                int(self.cfg.get("triage_timeout_seconds", 1800)),
            )
            self.log(
                f"反馈 #{feedback_id} 分诊完成："
                f"classification={triage.get('classification')}，"
                f"resolution={triage.get('resolution')}，"
                f"confidence={triage.get('confidence')}"
            )
            owner_answered = bool(str(job.get("agent_answer") or "").strip())
            can_fix = (
                triage.get("resolution") == "fix"
                and triage.get("risk_level") != "high"
                and (
                    owner_answered
                    or (
                        triage.get("classification") == "confirmed_bug"
                        and int(triage.get("confidence", 0)) >= 85
                    )
                )
            )
            if triage.get("resolution") == "reject":
                self.complete(job, "rejected", str(triage.get("player_summary") or "已确认不处理"))
                return
            if not can_fix:
                question = str(triage.get("owner_question") or "").strip()
                if not question:
                    question = (
                        "Agent 无法在现有证据下确认这是明确 Bug。"
                        "请说明预期行为，以及是否需要继续修改。"
                    )
                if owner_answered:
                    self.complete(
                        job,
                        "manual",
                        str(triage.get("reasoning_summary") or question)[:1800],
                    )
                    return
                self.ask_owner(
                    job, question, str(triage.get("reasoning_summary") or "等待确认")
                )
                return

            fix, _ = self.run_codex(
                worktree,
                "workspace-write",
                "fix.schema.json",
                agent_protocol.build_fix_prompt(job, triage),
                int(self.cfg.get("fix_timeout_seconds", 7200)),
            )
            if fix.get("status") != "fixed":
                summary = str(fix.get("summary") or "自动修复未完成")
                if owner_answered:
                    self.complete(job, "manual", summary[:1800])
                    return
                self.ask_owner(
                    job,
                    "Agent 已确认问题，但无法在安全边界内可靠修复。"
                    "请补充复现方式或指定处理方向。",
                    summary,
                )
                return
            files, tests = self.validate_review_and_test(worktree, job, triage)
            reviewed_commit = self.commit_changes(worktree, files, feedback_id)

            latest = self.sync_origin()
            if latest != base:
                rebase = run_process(["git", "rebase", latest], cwd=worktree, timeout=600)
                if rebase.returncode != 0:
                    run_process(["git", "rebase", "--abort"], cwd=worktree, timeout=60)
                    raise WorkerError("远端 main 已变化且自动变基冲突")
                files, tests = self.validate_changes(
                    worktree, base_ref=latest, committed=True
                )
                self.prepare_test_environment(tests)
                self.review(worktree, job, triage, tests, base_ref=latest)
                self.run_required_tests(worktree, tests)
                reviewed_commit = require_success(
                    run_process(["git", "rev-parse", "HEAD"], cwd=worktree),
                    "读取变基后待审提交",
                ).strip()
            summary = str(fix.get("summary") or "问题已修复")[:1800]
            retained_for_review = True
            self.complete(
                job,
                "manual",
                (
                    f"{summary}；已在隔离分支 {branch} 完成提交并通过门禁，"
                    "普通 Bug 工作器不具备合并或部署能力，等待可信管理员复核发布。"
                )[:1800],
                reviewed_commit,
            )
            self.log(
                f"反馈 #{feedback_id} 已完成待审提交：{reviewed_commit}，"
                f"保留 {worktree}；未合并、未部署"
            )
        except Exception as exc:
            detail = str(exc)
            transient = (
                isinstance(exc, subprocess.TimeoutExpired)
                or "Codex 执行失败" in detail
                or "Codex 没有返回" in detail
                or "模型" in detail and "连接" in detail
            )
            max_transient_attempts = max(
                1, int(self.cfg.get("max_transient_attempts", 3))
            )
            if (
                transient
                and int(job.get("agent_attempts") or 0) < max_transient_attempts
            ):
                self.log(f"反馈 #{feedback_id} 遇到瞬时模型故障，将重新排队：{detail}")
                try:
                    self.release(job, detail)
                except Exception as bridge_exc:
                    self.log(f"反馈 #{feedback_id} 释放租约失败：{bridge_exc}")
                return
            final_state = "failed" if transient else "manual"
            self.log(
                f"反馈 #{feedback_id} 自动处理终止，"
                f"状态={final_state}：{detail}"
            )
            try:
                self.complete(job, final_state, str(exc)[:1800])
            except Exception as bridge_exc:
                self.log(f"反馈 #{feedback_id} 状态回写失败：{bridge_exc}")
        finally:
            if retained_for_review:
                self.log(f"待审工作区已保留：{worktree}（分支 {branch}）")
            else:
                self.cleanup(worktree, branch, merged)

    def self_check(self) -> None:
        for name in ("git", "ssh", "powershell"):
            if shutil.which(name) is None:
                raise WorkerError(f"未找到命令: {name}")
        resolve_codex_command(str(self.cfg.get("codex_command") or "codex"))
        if not (self.repo / ".git").exists():
            raise WorkerError(f"repository_root 不是独立 Git 仓库: {self.repo}")
        self.bridge("status")
        self.sync_origin()
        smoke_prompt = (
            "不要运行命令或修改文件。仅按 Schema 输出：classification=uncertain，"
            "confidence=50，resolution=ask_owner，risk_level=low，"
            "其余字符串字段填写‘自检’，evidence 为一个字符串数组。"
        )
        result, _ = self.run_codex(
            self.repo,
            "read-only",
            "triage.schema.json",
            smoke_prompt,
            int(self.cfg.get("self_check_timeout_seconds", 300)),
        )
        if result.get("classification") != "uncertain":
            raise WorkerError("Codex 结构化自检返回了意外结果")
        self.log("自检通过")

    def run_once(self) -> bool:
        data = self.bridge("claim")
        job = data.get("job")
        if not job:
            return False
        self.process_job(job)
        return True

    def run_forever(self) -> None:
        interval = max(5, int(self.cfg.get("poll_seconds", 30)))
        self.log(f"工作器启动：{self.worker_id}")
        while True:
            try:
                worked = self.run_once()
                if worked:
                    continue
            except Exception as exc:
                self.log(f"工作循环异常：{exc}")
            time.sleep(interval)


def main() -> int:
    parser = argparse.ArgumentParser(description="GrandUMI Bug Agent 工作器")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--self-check", action="store_true")
    args = parser.parse_args()
    try:
        worker = AgentWorker(load_config(args.config.resolve()))
        if args.self_check:
            worker.self_check()
        elif args.once:
            worker.run_once()
        else:
            worker.self_check()
            worker.run_forever()
        return 0
    except (OSError, ValueError, WorkerError, subprocess.TimeoutExpired) as exc:
        print(f"[错误] {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
