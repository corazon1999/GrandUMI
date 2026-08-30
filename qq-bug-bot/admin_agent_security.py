# -*- coding: utf-8 -*-
"""管理员 Agent 的能力隔离、任务签名、双人批准与命令白名单。

QQ/NapCat 消息只能进入 ``qq_admin_intake`` 草案能力，绝不成为命令参数或
Codex 工具提示。需要执行的任务必须由独立可信入口签名；高风险动作还必须由
两名不同批准人分别签名，并在本地 SQLite 中原子消费，防止重放。
"""

from __future__ import annotations

import hashlib
import hmac
import json
import sqlite3
import time
from pathlib import Path


class SecurityPolicyError(RuntimeError):
    """任务、签名、权限或批准不满足安全策略。"""


ROLE_CAPABILITIES = {
    "chat_worker": frozenset({"reply"}),
    "qq_admin_intake": frozenset({"task_intake"}),
    "bug_worker": frozenset({"repository_read", "workspace_edit", "test"}),
    "trusted_operator": frozenset({
        "repository_read", "workspace_edit", "test", "deploy",
        "account_reset", "database_operation",
    }),
}

ACTION_CAPABILITIES = {
    "inspect_repository": "repository_read",
    "run_verification": "test",
    "apply_patch": "workspace_edit",
    "deploy_test": "deploy",
    "deploy_production": "deploy",
    "reset_account": "account_reset",
    "repair_database": "database_operation",
}

HIGH_RISK_ACTIONS = frozenset({
    "deploy_test", "deploy_production", "reset_account", "repair_database",
})
TRUSTED_HIGH_RISK_SOURCES = frozenset({"web_admin", "local_console"})
TASK_VERSION = 1
MAX_TASK_LIFETIME_SECONDS = 3600
HIGH_RISK_MAX_LIFETIME_SECONDS = 900


def _secret_bytes(secret: str | bytes) -> bytes:
    value = secret.encode("utf-8") if isinstance(secret, str) else bytes(secret)
    if len(value) < 32:
        raise SecurityPolicyError("签名密钥至少需要 32 字节。")
    return value


def _canonical(value) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def _task_material(task: dict) -> dict:
    return {
        "version": task.get("version"),
        "task_id": task.get("task_id"),
        "issued_at": task.get("issued_at"),
        "expires_at": task.get("expires_at"),
        "issuer": task.get("issuer"),
        "source": task.get("source"),
        "action": task.get("action"),
        "arguments": task.get("arguments"),
    }


def task_digest(task: dict) -> str:
    return hashlib.sha256(_canonical(_task_material(task))).hexdigest()


def sign_task(task: dict, issuer_secret: str | bytes) -> dict:
    """给结构化任务签名；调用方不得传入 QQ 原文作为 action 或命令。"""
    signed = dict(task)
    material = _task_material(signed)
    signed["signature"] = hmac.new(
        _secret_bytes(issuer_secret), _canonical(material), hashlib.sha256
    ).hexdigest()
    signed.setdefault("approvals", [])
    return signed


def sign_approval(
    task: dict,
    approver: str,
    approver_secret: str | bytes,
    issued_at: int | None = None,
) -> dict:
    actor = str(approver or "").strip()
    if not actor:
        raise SecurityPolicyError("批准人不能为空。")
    approval = {
        "approver": actor,
        "issued_at": int(time.time() if issued_at is None else issued_at),
        "task_digest": task_digest(task),
    }
    approval["signature"] = hmac.new(
        _secret_bytes(approver_secret), _canonical(approval), hashlib.sha256
    ).hexdigest()
    return approval


def require_capability(worker_role: str, capability: str) -> None:
    allowed = ROLE_CAPABILITIES.get(str(worker_role), frozenset())
    if capability not in allowed:
        raise SecurityPolicyError(
            f"工作器角色 {worker_role!r} 不具备能力 {capability!r}。"
        )


def _validate_arguments(action: str, arguments) -> dict:
    if not isinstance(arguments, dict):
        raise SecurityPolicyError("任务 arguments 必须是对象。")
    allowed_keys = {
        "inspect_repository": frozenset(),
        "run_verification": frozenset(),
        "apply_patch": frozenset({"patch_sha256"}),
        "deploy_test": frozenset({"verification_proof"}),
        "deploy_production": frozenset({"release_id"}),
        "reset_account": frozenset({"account"}),
        "repair_database": frozenset({"finding_id"}),
    }[action]
    if set(arguments) != allowed_keys:
        raise SecurityPolicyError("任务参数不符合动作白名单。")
    for key, value in arguments.items():
        if not isinstance(value, (str, int)) or isinstance(value, bool):
            raise SecurityPolicyError(f"任务参数 {key} 类型无效。")
        if isinstance(value, str) and (not value or len(value) > 256):
            raise SecurityPolicyError(f"任务参数 {key} 长度无效。")
    return dict(arguments)


def verify_task(
    task: dict,
    issuer_secret: str | bytes,
    approval_secrets: dict[str, str | bytes],
    worker_role: str,
    now: int | None = None,
) -> dict:
    """验证签名、时效、能力和双人批准，返回规范化任务。"""
    if not isinstance(task, dict):
        raise SecurityPolicyError("任务必须是对象。")
    current = int(time.time() if now is None else now)
    if task.get("version") != TASK_VERSION:
        raise SecurityPolicyError("任务版本无效。")
    task_id = str(task.get("task_id") or "").strip()
    issuer = str(task.get("issuer") or "").strip()
    source = str(task.get("source") or "").strip()
    action = str(task.get("action") or "").strip()
    if not task_id or len(task_id) > 120 or not issuer or not source:
        raise SecurityPolicyError("任务身份字段无效。")
    capability = ACTION_CAPABILITIES.get(action)
    if capability is None:
        raise SecurityPolicyError("任务动作不在白名单。")
    require_capability(worker_role, capability)
    arguments = _validate_arguments(action, task.get("arguments"))
    issued_at = int(task.get("issued_at") or 0)
    expires_at = int(task.get("expires_at") or 0)
    maximum = (
        HIGH_RISK_MAX_LIFETIME_SECONDS
        if action in HIGH_RISK_ACTIONS
        else MAX_TASK_LIFETIME_SECONDS
    )
    if issued_at > current + 30 or expires_at < current:
        raise SecurityPolicyError("任务尚未生效或已经过期。")
    if expires_at <= issued_at or expires_at - issued_at > maximum:
        raise SecurityPolicyError("任务有效期超过安全上限。")
    expected = hmac.new(
        _secret_bytes(issuer_secret), _canonical(_task_material(task)), hashlib.sha256
    ).hexdigest()
    if not hmac.compare_digest(expected, str(task.get("signature") or "")):
        raise SecurityPolicyError("任务签名无效。")

    if action in HIGH_RISK_ACTIONS:
        if source not in TRUSTED_HIGH_RISK_SOURCES:
            raise SecurityPolicyError("高风险任务不能由 QQ/NapCat 通道发起。")
        verified_approvers = set()
        digest = task_digest(task)
        for approval in task.get("approvals") or []:
            if not isinstance(approval, dict):
                continue
            approver = str(approval.get("approver") or "").strip()
            secret = approval_secrets.get(approver)
            material = {
                "approver": approver,
                "issued_at": approval.get("issued_at"),
                "task_digest": approval.get("task_digest"),
            }
            if secret is None or material["task_digest"] != digest:
                continue
            approval_time = int(material["issued_at"] or 0)
            if approval_time < issued_at or approval_time > current + 30:
                continue
            expected_approval = hmac.new(
                _secret_bytes(secret), _canonical(material), hashlib.sha256
            ).hexdigest()
            if hmac.compare_digest(
                expected_approval, str(approval.get("signature") or "")
            ):
                verified_approvers.add(approver)
        if len(verified_approvers) < 2:
            raise SecurityPolicyError("高风险任务需要两名不同批准人签名。")

    normalized = _task_material(task)
    normalized["arguments"] = arguments
    normalized["task_digest"] = task_digest(task)
    return normalized


def consume_task_once(database_path: str | Path, task: dict, now: int | None = None) -> bool:
    """原子消费任务。首次返回 True，同内容重放返回 False，篡改重放拒绝。"""
    path = Path(database_path).resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    digest = task_digest(task)
    task_id = str(task.get("task_id") or "").strip()
    if not task_id:
        raise SecurityPolicyError("任务 ID 不能为空。")
    with sqlite3.connect(path) as connection:
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=FULL")
        connection.execute(
            """
            CREATE TABLE IF NOT EXISTS consumed_admin_tasks (
                task_id TEXT PRIMARY KEY,
                task_digest TEXT NOT NULL,
                consumed_at INTEGER NOT NULL
            )
            """
        )
        connection.execute("BEGIN IMMEDIATE")
        existing = connection.execute(
            "SELECT task_digest FROM consumed_admin_tasks WHERE task_id = ?",
            (task_id,),
        ).fetchone()
        if existing:
            if not hmac.compare_digest(str(existing[0]), digest):
                raise SecurityPolicyError("同一任务 ID 已用于不同内容。")
            return False
        connection.execute(
            "INSERT INTO consumed_admin_tasks(task_id, task_digest, consumed_at) VALUES(?,?,?)",
            (task_id, digest, int(time.time() if now is None else now)),
        )
        connection.commit()
        return True


def build_allowed_command(action: str, repository_root: str | Path) -> list[str]:
    """仅从动作枚举构造固定 argv；任何 QQ 文本都不会进入返回值。"""
    root = Path(repository_root).resolve()
    if action == "inspect_repository":
        return ["git", "status", "--short"]
    if action == "run_verification":
        return [
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", str(root / "verify.ps1"),
        ]
    if action == "deploy_test":
        return [
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", str(root / "deploy-test.ps1"),
        ]
    raise SecurityPolicyError("该动作不允许通过本机命令执行器运行。")


def render_qq_admin_intake_reply(message: str) -> str:
    """QQ 管理消息的纯函数回复；不启动模型、Shell、部署或数据库工具。"""
    text = str(message or "")[:3000]
    high_risk_words = (
        "部署", "发布", "上线", "回滚", "重置密码", "改密码",
        "删库", "数据库", "执行命令", "powershell", "cmd", "shell",
    )
    if any(word.casefold() in text.casefold() for word in high_risk_words):
        return (
            "该请求属于高风险操作，QQ/NapCat 通道无执行权限。"
            "请到已认证的网页管理工作台发起，并完成一次性确认或双人批准。"
        )
    return (
        "这条消息已按未授权任务草案处理；QQ 管理通道不会运行命令、修改仓库或部署。"
        "需要执行时，请在可信管理工作台创建带签名的结构化任务。"
    )
