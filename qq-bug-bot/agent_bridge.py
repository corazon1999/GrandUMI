# -*- coding: utf-8 -*-
"""服务器侧 Agent 队列桥。

本机工作器仅通过 SSH 调用本文件，不接触服务器 GitHub Token 或 SQLite 文件。
除 claim/status 外的参数一律从标准输入读取 JSON，避免命令行注入与敏感内容泄漏。
"""

import argparse
import json
import os
import re
import sys

import github_issue
import media_pipeline
import storage

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.environ.get(
    "BUG_BOT_CONFIG_PATH", os.path.join(BASE_DIR, "config.json")
)
MAX_QUESTION_LENGTH = 3000
MAX_SUMMARY_LENGTH = 2000
MAX_CHAT_REPLY_LENGTH = 500
MAX_BUG_DESCRIPTION_LENGTH = 3000


def emit(payload: dict) -> None:
    print("AGENT_BRIDGE_JSON=" + json.dumps(payload, ensure_ascii=False))


def read_payload() -> dict:
    try:
        value = json.load(sys.stdin)
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise ValueError(f"标准输入不是有效 JSON: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError("请求体必须是 JSON 对象")
    return value


def load_config() -> dict:
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as file:
            value = json.load(file)
    except (OSError, json.JSONDecodeError):
        return {}
    return value if isinstance(value, dict) else {}


def load_repo() -> str:
    return str(
        load_config().get("github_repo") or "corazon1999/GrandUMI"
    )


def require_text(payload: dict, key: str, limit: int) -> str:
    value = str(payload.get(key) or "").strip()
    if not value:
        raise ValueError(f"缺少字段: {key}")
    if len(value) > limit:
        raise ValueError(f"字段 {key} 超过 {limit} 字")
    return value


def command_claim(args) -> None:
    job = storage.claim_agent_job(args.worker_id, args.lease_seconds)
    emit({"ok": True, "job": job})


def command_chat_claim(args) -> None:
    job = storage.claim_chat_job(
        args.worker_id,
        args.lease_seconds,
        ("chat", "bug_intake"),
    )
    emit({"ok": True, "job": job})


def command_admin_claim(args) -> None:
    job = storage.claim_chat_job(
        args.worker_id,
        args.lease_seconds,
        ("admin_agent",),
    )
    emit({"ok": True, "job": job})


def command_chat_complete() -> None:
    payload = read_payload()
    chat_id = int(payload.get("chat_id"))
    token = require_text(payload, "claim_token", 128)
    reply = require_text(payload, "reply", MAX_CHAT_REPLY_LENGTH)
    row = storage.get_chat_message(chat_id)
    if not storage.complete_chat_job(chat_id, token, reply):
        raise ValueError("聊天任务租约已失效，未写入回复")
    media_pipeline.cleanup_media((row or {}).get("media"))
    emit({"ok": True, "chat_id": chat_id})


def command_bug_intake_complete() -> None:
    payload = read_payload()
    chat_id = int(payload.get("chat_id"))
    token = require_text(payload, "claim_token", 128)
    decision = str(payload.get("decision") or "").strip()
    description = str(payload.get("cleaned_description") or "").strip()
    reply = str(payload.get("reply") or "").strip()
    if len(description) > MAX_BUG_DESCRIPTION_LENGTH:
        raise ValueError("cleaned_description 超过 3000 字")
    if len(reply) > MAX_CHAT_REPLY_LENGTH:
        raise ValueError("reply 超过 500 字")
    cfg = load_config()
    row = storage.get_chat_message(chat_id)
    result = storage.complete_bug_intake_job(
        chat_id,
        token,
        decision,
        description,
        reply,
        bool(cfg.get("agent_enabled", False)),
    )
    if not result:
        raise ValueError("Bug 描述检查任务租约已失效，未写入结果")

    feedback_id = result.get("feedback_id")
    if feedback_id and cfg.get("create_issue", True):
        title = description[:30] + ("…" if len(description) > 30 else "")
        body = (
            f"**来自 QQ 群反馈 #{feedback_id}**\n\n"
            f"- 上报人: {result['nickname']} (QQ: {result['qq']})\n"
            f"- 来源群: {result['group_id']}\n\n"
            f"## 问题描述\n\n{description}\n"
            f"\n<!-- grandumi-agent-job:v1 feedback_id={feedback_id} -->\n"
        )
        issue = github_issue.create_issue(
            str(cfg.get("github_repo") or "corazon1999/GrandUMI"),
            f"[反馈] {title}",
            body,
        )
        if issue:
            issue_no, _ = issue
            storage.set_issue_no(feedback_id, issue_no)
    if decision != "clarify":
        media_pipeline.cleanup_media((row or {}).get("media"))
    emit(
        {
            "ok": True,
            "chat_id": chat_id,
            "decision": decision,
            "feedback_id": feedback_id,
        }
    )


def command_chat_release() -> None:
    payload = read_payload()
    chat_id = int(payload.get("chat_id"))
    token = require_text(payload, "claim_token", 128)
    error = str(payload.get("error") or "Agent 暂时不可用").strip()[:1000]
    max_attempts = max(1, min(10, int(payload.get("max_attempts") or 3)))
    if not storage.release_chat_job(chat_id, token, error, max_attempts):
        raise ValueError("聊天任务租约已失效，未释放任务")
    row = storage.get_chat_message(chat_id)
    if row and row.get("state") == "failed":
        media_pipeline.cleanup_media(row.get("media"))
    emit({"ok": True, "chat_id": chat_id})


def command_ask() -> None:
    payload = read_payload()
    feedback_id = int(payload.get("feedback_id"))
    token = require_text(payload, "claim_token", 128)
    question = require_text(payload, "question", MAX_QUESTION_LENGTH)
    summary = str(payload.get("summary") or "").strip()[:MAX_SUMMARY_LENGTH]
    if not storage.request_owner_question(
        feedback_id, token, question, summary
    ):
        raise ValueError("任务租约已失效，未写入管理员问题")
    row = storage.get_feedback(feedback_id)
    issue_no = row.get("issue_no") if row else None
    if issue_no:
        github_issue.add_comment(
            load_repo(), issue_no,
            "## Agent 等待管理员确认\n\n" + question,
        )
    emit({"ok": True, "feedback_id": feedback_id})


def command_complete() -> None:
    payload = read_payload()
    feedback_id = int(payload.get("feedback_id"))
    token = require_text(payload, "claim_token", 128)
    state = str(payload.get("state") or "").strip()
    summary = require_text(payload, "summary", MAX_SUMMARY_LENGTH)
    commit = str(payload.get("commit") or "").strip()
    result_url = str(payload.get("result_url") or "").strip()
    if commit and not re.fullmatch(r"[0-9a-f]{7,40}", commit):
        raise ValueError("commit 格式无效")
    if result_url and not result_url.startswith("https://test.grand-umi.com"):
        raise ValueError("result_url 只能指向 GrandUMI 测试服")
    if not storage.complete_agent_job(
        feedback_id, token, state, summary, commit, result_url
    ):
        raise ValueError("任务租约已失效，未写入完成结果")

    row = storage.get_feedback(feedback_id)
    issue_no = row.get("issue_no") if row else None
    if issue_no:
        labels = {
            "fixed": "✅ 已自动修复并部署测试服",
            "rejected": "ℹ️ 已确认不处理",
            "manual": "⚠️ 自动修复已转人工",
            "failed": "❌ 自动修复失败",
        }
        detail = f"## {labels.get(state, state)}\n\n{summary}"
        if commit:
            detail += f"\n\n提交：`{commit}`"
        if result_url:
            detail += f"\n\n测试地址：{result_url}"
        github_issue.add_comment(load_repo(), issue_no, detail)
        if state in ("fixed", "rejected"):
            github_issue.close_issue(load_repo(), issue_no)
    emit({"ok": True, "feedback_id": feedback_id, "state": state})


def command_release() -> None:
    payload = read_payload()
    feedback_id = int(payload.get("feedback_id"))
    token = require_text(payload, "claim_token", 128)
    summary = str(payload.get("summary") or "").strip()[:MAX_SUMMARY_LENGTH]
    if not storage.release_agent_job(feedback_id, token, summary):
        raise ValueError("任务租约已失效，未释放任务")
    emit({"ok": True, "feedback_id": feedback_id})


def command_status() -> None:
    emit({"ok": True, "database": storage.DB_PATH})


def main() -> int:
    parser = argparse.ArgumentParser(description="GrandUMI Agent 队列桥")
    sub = parser.add_subparsers(dest="command", required=True)
    claim = sub.add_parser("claim")
    claim.add_argument("--worker-id", required=True)
    claim.add_argument("--lease-seconds", type=int, default=3600)
    chat_claim = sub.add_parser("chat-claim")
    chat_claim.add_argument("--worker-id", required=True)
    chat_claim.add_argument("--lease-seconds", type=int, default=600)
    admin_claim = sub.add_parser("admin-claim")
    admin_claim.add_argument("--worker-id", required=True)
    admin_claim.add_argument("--lease-seconds", type=int, default=7200)
    sub.add_parser("ask")
    sub.add_parser("complete")
    sub.add_parser("release")
    sub.add_parser("chat-complete")
    sub.add_parser("bug-intake-complete")
    sub.add_parser("chat-release")
    sub.add_parser("status")
    args = parser.parse_args()
    storage.init_db()
    try:
        if args.command == "claim":
            command_claim(args)
        elif args.command == "chat-claim":
            command_chat_claim(args)
        elif args.command == "admin-claim":
            command_admin_claim(args)
        elif args.command == "chat-complete":
            command_chat_complete()
        elif args.command == "bug-intake-complete":
            command_bug_intake_complete()
        elif args.command == "chat-release":
            command_chat_release()
        elif args.command == "ask":
            command_ask()
        elif args.command == "complete":
            command_complete()
        elif args.command == "release":
            command_release()
        else:
            command_status()
        return 0
    except (ValueError, TypeError) as exc:
        emit({"ok": False, "error": str(exc)})
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
