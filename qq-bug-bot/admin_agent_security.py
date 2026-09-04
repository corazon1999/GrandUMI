# -*- coding: utf-8 -*-
"""管理员 Agent 的来源校验、敏感信息保护与可信工作台任务签名。

管理员能力有两条彼此独立的信任路径：

* QQ 路径只接受服务器已经用 OneBot 原始 ``user_id``、顶层结构化 ``at`` 和
  ``message_id`` 核验并持久化的唯一管理员任务；本模块会在任务进入 Codex 前
  再校验该服务端凭据。
* 可信管理工作台仍使用结构化任务签名、能力白名单和高风险双人批准。

QQ 正文、图片、引用和合并转发始终只是任务数据，不能充当身份或扩大权限。
"""

from __future__ import annotations

import hashlib
import hmac
import json
import re
import sqlite3
import time
from pathlib import Path


class SecurityPolicyError(RuntimeError):
    """任务、签名、权限或批准不满足安全策略。"""


QQ_ADMIN_OWNER = "651846226"
QQ_ADMIN_SOURCE_AUTH = "onebot_owner_at_v1"
QQ_ADMIN_MODEL = "gpt-5.6-sol"
QQ_ADMIN_REASONING_EFFORT = "high"
QQ_ADMIN_ALLOWED_GROUPS = frozenset({"297542853", "524996856"})
QQ_ADMIN_ASSISTANT_SELF_IDS = {
    "primary": "3215228879",
    "s-eagle": "3430685803",
    "s-shark": "184689168",
}
_QQ_ADMIN_SOURCE_RE = re.compile(
    r"^onebot:([1-9]\d{0,11}):([1-9]\d{0,11}):([-A-Za-z0-9_:]{1,100})$"
)
_CLAIM_TOKEN_RE = re.compile(r"^[0-9a-f]{32}$")
_SENSITIVE_REPLY_PATTERNS = (
    re.compile(r"(?i)\b(?:sk|sess|ghp|github_pat)-?[A-Za-z0-9_\-]{16,}\b"),
    re.compile(r"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{16,}"),
    re.compile(
        r"(?i)(?:api[_ -]?key|access[_ -]?token|refresh[_ -]?token|id[_ -]?token|"
        r"client[_ -]?secret|secret[_ -]?key|password|cookie|authorization)"
        r"['\"]?\s*[:=]\s*['\"]?[^\s'\",}]{8,}"
    ),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
)


def validate_qq_admin_job(job: dict) -> dict:
    """在本机执行前校验服务端持久化的管理员来源凭据。

    这里不从正文推断身份；旧任务、手工插入行和缺少消息号的任务均失败关闭。
    """
    if not isinstance(job, dict):
        raise SecurityPolicyError("管理员任务必须是对象。")
    if str(job.get("kind") or "") != "admin_agent":
        raise SecurityPolicyError("任务类型不是管理员 Agent。")
    if str(job.get("qq") or "") != QQ_ADMIN_OWNER:
        raise SecurityPolicyError("管理员任务的原始发送者不是唯一管理员。")
    if str(job.get("source_auth") or "") != QQ_ADMIN_SOURCE_AUTH:
        raise SecurityPolicyError("管理员任务缺少服务端 OneBot 授权凭据。")

    assistant_id = str(job.get("assistant_id") or "").strip().lower()
    expected_self_id = QQ_ADMIN_ASSISTANT_SELF_IDS.get(assistant_id)
    if not expected_self_id:
        raise SecurityPolicyError("管理员任务的来源助理不在固定名单中。")
    group_id = str(job.get("group_id") or "").strip()
    if group_id not in QQ_ADMIN_ALLOWED_GROUPS:
        raise SecurityPolicyError("管理员任务不来自两个固定授权群。")
    source_key = str(job.get("source_message_key") or "").strip()
    match = _QQ_ADMIN_SOURCE_RE.fullmatch(source_key)
    if not match:
        raise SecurityPolicyError("管理员任务缺少有效的 OneBot 消息来源键。")
    if match.group(1) != expected_self_id or match.group(2) != group_id:
        raise SecurityPolicyError("管理员任务的助理账号或群号与来源键不一致。")

    try:
        chat_id = int(job.get("id"))
        attempts = int(job.get("attempts"))
    except (TypeError, ValueError) as exc:
        raise SecurityPolicyError("管理员任务的队列状态字段无效。") from exc
    if isinstance(job.get("id"), bool) or chat_id <= 0 or attempts <= 0:
        raise SecurityPolicyError("管理员任务的队列状态字段无效。")
    if str(job.get("state") or "") != "claimed":
        raise SecurityPolicyError("管理员任务尚未通过原子租约领取。")
    if not _CLAIM_TOKEN_RE.fullmatch(str(job.get("claim_token") or "")):
        raise SecurityPolicyError("管理员任务缺少有效租约令牌。")

    content = job.get("content")
    context = job.get("context_text")
    if not isinstance(content, str) or not content.strip() or len(content) > 3000:
        raise SecurityPolicyError("管理员任务正文无效。")
    if context is not None and (
        not isinstance(context, str) or len(context) > 12000
    ):
        raise SecurityPolicyError("管理员任务的引用上下文无效。")
    media = job.get("media") or []
    if not isinstance(media, list) or len(media) > 8:
        raise SecurityPolicyError("管理员任务的图片元数据无效。")
    for item in media:
        if not isinstance(item, dict) or str(item.get("source") or "direct") not in (
            "direct", "forward"
        ):
            raise SecurityPolicyError("管理员任务的图片来源无效。")
    return dict(job)


def contains_sensitive_reply(text: str) -> bool:
    """保守拦截不应回传到 QQ 群的常见凭据格式。"""
    value = str(text or "")
    return any(pattern.search(value) for pattern in _SENSITIVE_REPLY_PATTERNS)


def safe_qq_admin_reply(text: str) -> str:
    """校验管理员群回复；疑似含凭据时整段阻断，避免部分脱敏遗漏。"""
    value = str(text or "").strip()
    if not value or len(value) > 500:
        raise ValueError("管理员 Agent 返回的 reply 长度无效。")
    if contains_sensitive_reply(value):
        return (
            "任务已处理，但回复中检测到疑似密钥或凭据，已阻止发送到 QQ 群。"
            "请重新提问，并要求只返回不含凭据的脱敏摘要。"
        )
    return value


def redact_sensitive_text(text: str) -> str:
    """对写入常驻日志或队列错误字段的文本做凭据级保守脱敏。"""
    value = str(text or "")
    for pattern in _SENSITIVE_REPLY_PATTERNS:
        value = pattern.sub("[已脱敏凭据]", value)
    return value


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
