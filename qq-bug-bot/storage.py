# -*- coding: utf-8 -*-
"""本地 SQLite 存储:把每一条群内 bug 反馈持久化到 feedback.db。

设计原则:写本地一定成功(不依赖网络),GitHub Issue 编号建好后再回填。
"""

import os
import json
import re
import sqlite3
import time
from datetime import datetime
from datetime import timedelta
from uuid import uuid4

# 默认沿用本地目录；容器部署时通过环境变量把数据库放进持久化卷。
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.environ.get("BUG_BOT_DB_PATH", os.path.join(BASE_DIR, "feedback.db"))

AGENT_QUEUE_STATES = ("queued", "owner_answered")
AGENT_TERMINAL_STATES = ("fixed", "rejected", "manual", "failed")
CHAT_QUEUE_STATES = ("queued", "claimed")
CHAT_TERMINAL_STATES = ("completed", "failed")
GROUP_PERSONALITIES = ("hancock", "nami", "robin")
PERSONALITIES = (*GROUP_PERSONALITIES, "jinbe")
DEFAULT_PERSONALITY = "hancock"
DEFAULT_ASSISTANT_ID = "primary"
_ASSISTANT_ID_RE = re.compile(r"^[a-z0-9][a-z0-9_-]{0,31}$")
MEMBER_VERIFICATION_ACTIVE_STATES = (
    "approval_pending",
    "awaiting_join",
    "awaiting_prompt",
    "pending",
    "checking_inviter",
    "reminding",
)
# kicked 仅用于识别旧版本已经完成的历史记录；新版本不会再产生该状态。
MEMBER_VERIFICATION_TERMINAL_STATES = ("verified", "kicked", "left", "cancelled")
MEMBER_VERIFICATION_REMINDER_MAX_ATTEMPTS = 5
MEMBER_VERIFICATION_REMINDER_RETRY_BASE_SECONDS = 5
MEMBER_VERIFICATION_REMINDER_RETRY_MAX_SECONDS = 300
ABUSE_MODERATION_DURATION_SECONDS = 86400
ABUSE_MODERATION_BARRIER_STATES = (
    "reserved",
    "confirmed",
    "unknown",
    "already_muted",
)
_MODERATION_RULE_ID_RE = re.compile(r"^[a-z][a-z0-9_]{0,63}$")
_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def init_db() -> None:
    """初始化数据库与表结构(幂等,可重复调用)。"""
    os.makedirs(os.path.dirname(os.path.abspath(DB_PATH)), exist_ok=True)
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS feedback (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                qq         TEXT    NOT NULL,   -- 上报者 QQ 号
                nickname   TEXT,               -- 群昵称/网名
                group_id   TEXT    NOT NULL,   -- 来源群号
                content    TEXT    NOT NULL,   -- 反馈正文(已去掉指令前缀)
                issue_no   INTEGER,            -- 对应的 GitHub Issue 编号,建失败为 NULL
                created_at TEXT    NOT NULL,   -- ISO 时间
                status     TEXT    NOT NULL DEFAULT 'open',  -- open(待修)/fixed(已修)/wontfix(非bug)
                fix_note   TEXT,               -- 修复备注(报告里展示)
                agent_state TEXT NOT NULL DEFAULT 'none',
                agent_question TEXT,
                agent_question_sent_at TEXT,
                agent_answer TEXT,
                agent_summary TEXT,
                agent_commit TEXT,
                agent_result_url TEXT,
                agent_claim_token TEXT,
                agent_worker_id TEXT,
                agent_claimed_at TEXT,
                agent_attempts INTEGER NOT NULL DEFAULT 0,
                agent_updated_at TEXT,
                agent_reply_sent_at TEXT
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL DEFAULT 'chat',
                qq TEXT NOT NULL,
                nickname TEXT,
                group_id TEXT NOT NULL,
                content TEXT NOT NULL,
                state TEXT NOT NULL DEFAULT 'queued',
                reply TEXT,
                error TEXT,
                claim_token TEXT,
                worker_id TEXT,
                claimed_at TEXT,
                attempts INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                reply_sent_at TEXT,
                feedback_id INTEGER,
                continued_at TEXT,
                media_json TEXT NOT NULL DEFAULT '[]',
                personality TEXT NOT NULL DEFAULT 'hancock',
                assistant_id TEXT NOT NULL DEFAULT 'primary',
                source_message_key TEXT,
                source_auth TEXT,
                context_text TEXT NOT NULL DEFAULT ''
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS group_settings (
                group_id TEXT PRIMARY KEY,
                personality TEXT NOT NULL DEFAULT 'hancock',
                updated_by TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS member_verifications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                group_id TEXT NOT NULL,
                newcomer_qq TEXT NOT NULL,
                nickname TEXT,
                join_event_time INTEGER NOT NULL,
                state TEXT NOT NULL DEFAULT 'awaiting_prompt',
                inviter_qq TEXT,
                candidate_qq TEXT,
                response_message_key TEXT,
                response_event_time INTEGER,
                prompt_sent_at INTEGER,
                deadline_at INTEGER,
                invalid_attempts INTEGER NOT NULL DEFAULT 0,
                prompt_attempts INTEGER NOT NULL DEFAULT 0,
                reminder_attempts INTEGER NOT NULL DEFAULT 0,
                reminder_sent_at INTEGER,
                check_attempts INTEGER NOT NULL DEFAULT 0,
                kick_attempts INTEGER NOT NULL DEFAULT 0,
                claim_token TEXT,
                claim_kind TEXT,
                claimed_at INTEGER,
                next_attempt_at INTEGER,
                last_error TEXT,
                verified_at INTEGER,
                kick_requested_at INTEGER,
                kicked_at INTEGER,
                ended_at INTEGER,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                UNIQUE(group_id, newcomer_qq, join_event_time)
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS member_verification_responses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                verification_id INTEGER NOT NULL,
                group_id TEXT NOT NULL,
                newcomer_qq TEXT NOT NULL,
                message_key TEXT NOT NULL,
                candidate_qq TEXT NOT NULL,
                event_time INTEGER NOT NULL,
                received_at INTEGER NOT NULL,
                result TEXT NOT NULL,
                detail TEXT,
                updated_at INTEGER NOT NULL,
                UNIQUE(verification_id, message_key)
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS bot_runtime_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at INTEGER NOT NULL
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS qq_whitelist_sync_runs (
                operation_key TEXT PRIMARY KEY,
                scheduled_hour INTEGER NOT NULL,
                group_id TEXT NOT NULL,
                group_name TEXT NOT NULL,
                source_set_key TEXT,
                source_groups_json TEXT,
                snapshot_sha256 TEXT,
                snapshot_members_json TEXT,
                client_instance_id TEXT NOT NULL,
                state TEXT NOT NULL,
                version INTEGER,
                member_count INTEGER,
                notification_owner INTEGER NOT NULL DEFAULT 0,
                notification_message TEXT,
                notification_attempts INTEGER NOT NULL DEFAULT 0,
                notification_acked_at INTEGER,
                failure_reported_at INTEGER,
                last_error TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                UNIQUE(group_id, scheduled_hour)
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS qq_whitelist_sync_notifications (
                operation_key TEXT NOT NULL,
                group_id TEXT NOT NULL,
                state TEXT NOT NULL,
                notification_message TEXT NOT NULL,
                notification_attempts INTEGER NOT NULL DEFAULT 0,
                sender_process_id TEXT,
                sent_at INTEGER,
                last_error TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                PRIMARY KEY(operation_key, group_id),
                FOREIGN KEY(operation_key) REFERENCES qq_whitelist_sync_runs(operation_key)
                    ON UPDATE CASCADE ON DELETE CASCADE
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS abuse_moderation_actions (
                event_key TEXT PRIMARY KEY,
                group_id TEXT NOT NULL,
                offender_qq TEXT NOT NULL,
                source_message_id TEXT NOT NULL,
                rule_id TEXT NOT NULL,
                content_sha256 TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                member_role TEXT NOT NULL,
                state TEXT NOT NULL,
                action_token TEXT,
                related_event_key TEXT,
                suppression_until INTEGER,
                action_started_at INTEGER,
                completed_at INTEGER,
                last_error TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            )
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_messages_queue
            ON chat_messages(state, created_at, id)
            """
        )
        # 旧版会把到期会话推进到最终检查或踢人租约。启动时必须前向恢复为
        # 可无限期回答的 pending，并清除所有可能继续执行旧动作的租约与截止时间。
        migration_now = int(time.time())
        conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   deadline_at = NULL,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   kick_requested_at = NULL,
                   ended_at = NULL,
                   last_error = '已取消旧版超时移出流程，可继续回答邀请人 QQ',
                   updated_at = ?
             WHERE state IN ('checking_timeout', 'kicking')
            """,
            (migration_now,),
        )
        # 幂等重建活动索引，让审批、入群、回答和提醒全链路始终只有一个活动会话。
        conn.execute("DROP INDEX IF EXISTS idx_member_verifications_active")
        conn.execute(
            """
            CREATE UNIQUE INDEX idx_member_verifications_active
            ON member_verifications(group_id, newcomer_qq)
            WHERE state IN (
                'approval_pending', 'awaiting_join',
                'awaiting_prompt', 'pending', 'checking_inviter',
                'reminding'
            )
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_member_verifications_jobs
            ON member_verifications(state, deadline_at, claimed_at, id)
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_qq_whitelist_sync_recovery
            ON qq_whitelist_sync_runs(group_id, scheduled_hour, state)
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_abuse_moderation_barrier
            ON abuse_moderation_actions(
                group_id, offender_qq, state, suppression_until
            )
            """
        )
        # 幂等迁移:给老库补上新列(缺了才加)
        cols = {row[1] for row in conn.execute("PRAGMA table_info(feedback)")}
        if "status" not in cols:
            conn.execute("ALTER TABLE feedback ADD COLUMN status TEXT NOT NULL DEFAULT 'open'")
        if "fix_note" not in cols:
            conn.execute("ALTER TABLE feedback ADD COLUMN fix_note TEXT")
        if "dup_of" not in cols:
            # 重复指向:某条是另一条(主条目)的重复时,记主条目 id;为空表示它自己就是主条目
            conn.execute("ALTER TABLE feedback ADD COLUMN dup_of INTEGER")
        migrations = {
            "agent_state": "TEXT NOT NULL DEFAULT 'none'",
            "agent_question": "TEXT",
            "agent_question_sent_at": "TEXT",
            "agent_answer": "TEXT",
            "agent_summary": "TEXT",
            "agent_commit": "TEXT",
            "agent_result_url": "TEXT",
            "agent_claim_token": "TEXT",
            "agent_worker_id": "TEXT",
            "agent_claimed_at": "TEXT",
            "agent_attempts": "INTEGER NOT NULL DEFAULT 0",
            "agent_updated_at": "TEXT",
            "agent_reply_sent_at": "TEXT",
        }
        for name, declaration in migrations.items():
            if name not in cols:
                conn.execute(
                    f"ALTER TABLE feedback ADD COLUMN {name} {declaration}"
                )
        chat_cols = {
            row[1] for row in conn.execute("PRAGMA table_info(chat_messages)")
        }
        if "kind" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages "
                "ADD COLUMN kind TEXT NOT NULL DEFAULT 'chat'"
            )
        if "feedback_id" not in chat_cols:
            conn.execute("ALTER TABLE chat_messages ADD COLUMN feedback_id INTEGER")
        if "continued_at" not in chat_cols:
            conn.execute("ALTER TABLE chat_messages ADD COLUMN continued_at TEXT")
        if "media_json" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages "
                "ADD COLUMN media_json TEXT NOT NULL DEFAULT '[]'"
            )
        if "personality" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages "
                "ADD COLUMN personality TEXT NOT NULL DEFAULT 'hancock'"
            )
        if "assistant_id" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages "
                "ADD COLUMN assistant_id TEXT NOT NULL DEFAULT 'primary'"
            )
        if "source_message_key" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages ADD COLUMN source_message_key TEXT"
            )
        if "source_auth" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages ADD COLUMN source_auth TEXT"
            )
        if "context_text" not in chat_cols:
            conn.execute(
                "ALTER TABLE chat_messages "
                "ADD COLUMN context_text TEXT NOT NULL DEFAULT ''"
            )
        conn.execute(
            """
            UPDATE chat_messages
               SET assistant_id = 'primary'
             WHERE assistant_id IS NULL OR TRIM(assistant_id) = ''
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_messages_delivery
            ON chat_messages(assistant_id, state, reply_sent_at, updated_at, id)
            """
        )
        conn.execute(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS idx_chat_messages_admin_source
            ON chat_messages(assistant_id, source_message_key)
            WHERE kind = 'admin_agent' AND source_message_key IS NOT NULL
            """
        )
        verification_cols = {
            row[1] for row in conn.execute("PRAGMA table_info(member_verifications)")
        }
        if "next_attempt_at" not in verification_cols:
            conn.execute(
                "ALTER TABLE member_verifications ADD COLUMN next_attempt_at INTEGER"
            )
        if "reminder_attempts" not in verification_cols:
            conn.execute(
                "ALTER TABLE member_verifications "
                "ADD COLUMN reminder_attempts INTEGER NOT NULL DEFAULT 0"
            )
        if "reminder_sent_at" not in verification_cols:
            conn.execute(
                "ALTER TABLE member_verifications ADD COLUMN reminder_sent_at INTEGER"
            )
        sync_cols = {
            row[1] for row in conn.execute("PRAGMA table_info(qq_whitelist_sync_runs)")
        }
        if "failure_reported_at" not in sync_cols:
            conn.execute(
                "ALTER TABLE qq_whitelist_sync_runs "
                "ADD COLUMN failure_reported_at INTEGER"
            )
        for column, declaration in {
            "source_set_key": "TEXT",
            "source_groups_json": "TEXT",
            "snapshot_sha256": "TEXT",
            "snapshot_members_json": "TEXT",
        }.items():
            if column not in sync_cols:
                conn.execute(
                    f"ALTER TABLE qq_whitelist_sync_runs ADD COLUMN {column} {declaration}"
                )
        conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
               SET source_set_key = group_id
             WHERE source_set_key IS NULL OR TRIM(source_set_key) = ''
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_qq_whitelist_sync_source_recovery
            ON qq_whitelist_sync_runs(source_set_key, scheduled_hour, state)
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_qq_whitelist_sync_notifications_state
            ON qq_whitelist_sync_notifications(operation_key, state, group_id)
            """
        )
        notification_cols = {
            row[1]
            for row in conn.execute(
                "PRAGMA table_info(qq_whitelist_sync_notifications)"
            )
        }
        if "sender_process_id" not in notification_cols:
            conn.execute(
                "ALTER TABLE qq_whitelist_sync_notifications "
                "ADD COLUMN sender_process_id TEXT"
            )
        conn.commit()


def _validate_abuse_moderation_identity(value, label: str) -> str:
    text = str(value or "").strip()
    if not re.fullmatch(r"[1-9]\d{4,11}", text):
        raise ValueError(f"{label}无效")
    return text


def get_abuse_moderation_action(event_key: str):
    """读取处罚审计记录；原始群消息正文不会写入此表。"""
    key = str(event_key or "").strip()
    if not key:
        return None
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            "SELECT * FROM abuse_moderation_actions WHERE event_key = ?",
            (key,),
        ).fetchone()
        return dict(row) if row else None


def reserve_abuse_moderation_action(
    event_key: str,
    group_id: str,
    offender_qq: str,
    source_message_id: str,
    rule_id: str,
    content_sha256: str,
    member_role: str,
    observed_mute_until=0,
    now=None,
):
    """原子预占一次处罚，并阻止重复消息或已有处罚窗口被再次延长。

    ``reserved`` 在外部动作前持久化，本身就代表“不能安全重试”。即使进程
    在下发动作前后崩溃，重放也只会读取该记录，不会再次调用 OneBot。
    """
    key = str(event_key or "").strip()
    if not key or len(key) > 220:
        raise ValueError("处罚事件键无效")
    selected_group = _validate_abuse_moderation_identity(group_id, "群号")
    selected_offender = _validate_abuse_moderation_identity(offender_qq, "成员 QQ")
    message_id = str(source_message_id or "").strip()
    if not message_id or len(message_id) > 100:
        raise ValueError("OneBot 消息号无效")
    selected_rule = str(rule_id or "").strip()
    if not _MODERATION_RULE_ID_RE.fullmatch(selected_rule):
        raise ValueError("辱骂判定规则无效")
    selected_hash = str(content_sha256 or "").strip().lower()
    if not _SHA256_RE.fullmatch(selected_hash):
        raise ValueError("消息摘要无效")
    role = str(member_role or "").strip().lower()
    if role != "member":
        raise ValueError("只有普通群成员可以进入处罚状态机")
    now_value = int(time.time() if now is None else now)
    mute_until = max(0, int(observed_mute_until or 0))

    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        existing = conn.execute(
            "SELECT * FROM abuse_moderation_actions WHERE event_key = ?",
            (key,),
        ).fetchone()
        if existing:
            conn.commit()
            result = dict(existing)
            result["acquired"] = False
            result["reason"] = "duplicate_event"
            return result

        common_values = (
            key,
            selected_group,
            selected_offender,
            message_id,
            selected_rule,
            selected_hash,
            ABUSE_MODERATION_DURATION_SECONDS,
            role,
        )
        if mute_until > now_value:
            conn.execute(
                """
                INSERT INTO abuse_moderation_actions (
                    event_key, group_id, offender_qq, source_message_id,
                    rule_id, content_sha256, duration_seconds, member_role,
                    state, suppression_until, completed_at, created_at, updated_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'already_muted', ?, ?, ?, ?)
                """,
                (*common_values, mute_until, now_value, now_value, now_value),
            )
            conn.commit()
            result = get_abuse_moderation_action(key)
            result["acquired"] = False
            result["reason"] = "already_muted"
            return result

        placeholders = ", ".join("?" for _ in ABUSE_MODERATION_BARRIER_STATES)
        active = conn.execute(
            f"""
            SELECT event_key, state, suppression_until
              FROM abuse_moderation_actions
             WHERE group_id = ? AND offender_qq = ?
               AND state IN ({placeholders})
               AND suppression_until > ?
             ORDER BY suppression_until DESC, created_at DESC
             LIMIT 1
            """,
            (
                selected_group,
                selected_offender,
                *ABUSE_MODERATION_BARRIER_STATES,
                now_value,
            ),
        ).fetchone()
        if active:
            conn.execute(
                """
                INSERT INTO abuse_moderation_actions (
                    event_key, group_id, offender_qq, source_message_id,
                    rule_id, content_sha256, duration_seconds, member_role,
                    state, related_event_key, suppression_until,
                    completed_at, created_at, updated_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'suppressed', ?, ?, ?, ?, ?)
                """,
                (
                    *common_values,
                    active["event_key"],
                    active["suppression_until"],
                    now_value,
                    now_value,
                    now_value,
                ),
            )
            conn.commit()
            result = get_abuse_moderation_action(key)
            result["acquired"] = False
            result["reason"] = f"active_{active['state']}"
            return result

        token = uuid4().hex
        suppression_until = now_value + ABUSE_MODERATION_DURATION_SECONDS
        conn.execute(
            """
            INSERT INTO abuse_moderation_actions (
                event_key, group_id, offender_qq, source_message_id,
                rule_id, content_sha256, duration_seconds, member_role,
                state, action_token, suppression_until, action_started_at,
                created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'reserved', ?, ?, ?, ?, ?)
            """,
            (
                *common_values,
                token,
                suppression_until,
                now_value,
                now_value,
                now_value,
            ),
        )
        conn.commit()
        result = get_abuse_moderation_action(key)
        result["acquired"] = True
        result["reason"] = "reserved"
        return result


def finish_abuse_moderation_action(
    event_key: str,
    action_token: str,
    state: str,
    error: str = "",
    now=None,
) -> bool:
    """用预占令牌一次性落稳 OneBot 结果，迟到或重复完成不会改写终态。"""
    selected_state = str(state or "").strip().lower()
    if selected_state not in ("confirmed", "unknown", "rejected"):
        raise ValueError("处罚完成状态无效")
    key = str(event_key or "").strip()
    token = str(action_token or "").strip()
    if not key or not token:
        return False
    now_value = int(time.time() if now is None else now)
    detail = str(error or "").strip()[:1000] or None
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute("BEGIN IMMEDIATE")
        if selected_state == "confirmed":
            suppression_sql = "suppression_until = ?"
            suppression_params = (now_value + ABUSE_MODERATION_DURATION_SECONDS,)
        elif selected_state == "rejected":
            suppression_sql = "suppression_until = NULL"
            suppression_params = ()
        else:
            suppression_sql = "suppression_until = suppression_until"
            suppression_params = ()
        cur = conn.execute(
            f"""
            UPDATE abuse_moderation_actions
               SET state = ?,
                   action_token = NULL,
                   {suppression_sql},
                   completed_at = ?,
                   last_error = ?,
                   updated_at = ?
             WHERE event_key = ? AND action_token = ? AND state = 'reserved'
            """,
            (
                selected_state,
                *suppression_params,
                now_value,
                detail,
                now_value,
                key,
                token,
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def get_or_create_qq_whitelist_sync_instance_id(now=None) -> str:
    """返回持久化实例标识；容器重启后仍由同一实例领取通知。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            "SELECT value FROM bot_runtime_state WHERE key = 'qq_whitelist_sync_instance_id'"
        ).fetchone()
        if row:
            return str(row[0])
        instance_id = str(uuid4())
        conn.execute(
            """
            INSERT INTO bot_runtime_state(key, value, updated_at)
            VALUES('qq_whitelist_sync_instance_id', ?, ?)
            """,
            (instance_id, current),
        )
        conn.commit()
        return instance_id


def prepare_qq_whitelist_sync(
    operation_key: str,
    scheduled_hour: int,
    group_id: str,
    group_name: str,
    client_instance_id: str,
    now=None,
):
    """原子登记计划任务；同一群同一计划时间只能得到同一条本地任务。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE group_id = ? AND scheduled_hour = ?
            """,
            (str(group_id), int(scheduled_hour)),
        ).fetchone()
        if row:
            return dict(row)
        conn.execute(
            """
            INSERT INTO qq_whitelist_sync_runs(
                operation_key, scheduled_hour, group_id, group_name,
                client_instance_id, state, created_at, updated_at)
            VALUES(?, ?, ?, ?, ?, 'started', ?, ?)
            """,
            (
                operation_key,
                int(scheduled_hour),
                str(group_id),
                group_name,
                client_instance_id,
                current,
                current,
            ),
        )
        row = conn.execute(
            "SELECT * FROM qq_whitelist_sync_runs WHERE operation_key = ?",
            (operation_key,),
        ).fetchone()
        conn.commit()
        return dict(row)


def prepare_qq_whitelist_sync_slot(
    slot_key: str,
    scheduled_hour: int,
    source_set_key: str,
    primary_group_id: str,
    client_instance_id: str,
    now=None,
):
    """原子登记双群计划槽；同一数据源集合、同一时隙只能有一条任务。"""
    current = int(time.time() if now is None else now)
    normalized_source = str(source_set_key or "").strip()
    if not normalized_source:
        raise ValueError("QQ 白名单同步数据源集合不能为空")
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND scheduled_hour = ?
            """,
            (normalized_source, int(scheduled_hour)),
        ).fetchone()
        if row:
            return dict(row)
        # 旧版以主群 + 时隙做唯一键。若切换到 v2 时同一时隙已经存在 v1 记录，
        # 本时隙宁可安全跳过，也不能因唯一键冲突崩溃或覆盖旧任务。
        legacy = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE group_id = ? AND scheduled_hour = ?
            """,
            (str(primary_group_id), int(scheduled_hour)),
        ).fetchone()
        if legacy:
            return dict(legacy)
        conn.execute(
            """
            INSERT INTO qq_whitelist_sync_runs(
                operation_key, scheduled_hour, group_id, group_name,
                source_set_key, client_instance_id, state, created_at, updated_at)
            VALUES(?, ?, ?, '双群并集', ?, ?, 'started', ?, ?)
            """,
            (
                str(slot_key),
                int(scheduled_hour),
                str(primary_group_id),
                normalized_source,
                str(client_instance_id),
                current,
                current,
            ),
        )
        row = conn.execute(
            "SELECT * FROM qq_whitelist_sync_runs WHERE operation_key = ?",
            (str(slot_key),),
        ).fetchone()
        conn.commit()
        return dict(row)


def bind_qq_whitelist_sync_snapshot(
    source_set_key: str,
    scheduled_hour: int,
    operation_key: str,
    source_groups_json: str,
    snapshot_sha256: str,
    snapshot_members_json: str,
    now=None,
):
    """把计划槽原子绑定到首份完整双群快照；竞争者只能复用，不能覆盖。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND scheduled_hour = ?
            """,
            (str(source_set_key), int(scheduled_hour)),
        ).fetchone()
        if not row:
            conn.rollback()
            return None
        if row["snapshot_sha256"] is None:
            if row["state"] != "started":
                conn.rollback()
                return dict(row)
            conn.execute(
                """
                UPDATE qq_whitelist_sync_runs
                   SET operation_key = ?, source_groups_json = ?, snapshot_sha256 = ?,
                       snapshot_members_json = ?, updated_at = ?
                 WHERE source_set_key = ? AND scheduled_hour = ?
                   AND snapshot_sha256 IS NULL AND state = 'started'
                """,
                (
                    str(operation_key),
                    str(source_groups_json),
                    str(snapshot_sha256),
                    str(snapshot_members_json),
                    current,
                    str(source_set_key),
                    int(scheduled_hour),
                ),
            )
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND scheduled_hour = ?
            """,
            (str(source_set_key), int(scheduled_hour)),
        ).fetchone()
        conn.commit()
        return dict(row) if row else None


def bind_qq_whitelist_sync_failure_key(
    source_set_key: str,
    scheduled_hour: int,
    operation_key: str,
    now=None,
):
    """完整快照尚未形成时，为失败审计绑定不含部分成员的确定性键。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
               SET operation_key = ?, updated_at = ?
             WHERE source_set_key = ? AND scheduled_hour = ?
               AND snapshot_sha256 IS NULL AND state = 'started'
            """,
            (
                str(operation_key),
                current,
                str(source_set_key),
                int(scheduled_hour),
            ),
        )
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND scheduled_hour = ?
            """,
            (str(source_set_key), int(scheduled_hour)),
        ).fetchone()
        conn.commit()
        return dict(row) if row else None


def get_qq_whitelist_sync(operation_key: str):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            "SELECT * FROM qq_whitelist_sync_runs WHERE operation_key = ?",
            (operation_key,),
        ).fetchone()
        return dict(row) if row else None


def get_qq_whitelist_sync_for_hour(group_id: str, scheduled_hour: int):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE group_id = ? AND scheduled_hour = ?
            """,
            (str(group_id), int(scheduled_hour)),
        ).fetchone()
        return dict(row) if row else None


def get_qq_whitelist_sync_for_slot(source_set_key: str, scheduled_hour: int):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND scheduled_hour = ?
            """,
            (str(source_set_key), int(scheduled_hour)),
        ).fetchone()
        return dict(row) if row else None


def get_last_qq_whitelist_sync_member_count(group_id: str):
    with sqlite3.connect(DB_PATH) as conn:
        row = conn.execute(
            """
            SELECT member_count FROM qq_whitelist_sync_runs
            WHERE group_id = ? AND version IS NOT NULL
            ORDER BY scheduled_hour DESC LIMIT 1
            """,
            (str(group_id),),
        ).fetchone()
        return int(row[0]) if row and row[0] is not None else None


def get_last_qq_whitelist_sync_source_member_count(source_set_key: str):
    with sqlite3.connect(DB_PATH) as conn:
        row = conn.execute(
            """
            SELECT member_count FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND version IS NOT NULL
            ORDER BY scheduled_hour DESC LIMIT 1
            """,
            (str(source_set_key),),
        ).fetchone()
        return int(row[0]) if row and row[0] is not None else None


def mark_qq_whitelist_sync_committed(
    operation_key: str,
    version: int,
    member_count: int,
    notification_owner: bool,
    notification_message: str,
    notification_group_ids=None,
    now=None,
) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            "SELECT version, member_count FROM qq_whitelist_sync_runs WHERE operation_key = ?",
            (operation_key,),
        ).fetchone()
        if not row:
            return False
        if row[0] is not None and (int(row[0]), int(row[1])) != (
            int(version),
            int(member_count),
        ):
            raise RuntimeError("同一本地计划任务出现互相冲突的已提交版本")
        state = "committed" if notification_owner else "suppressed"
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = ?, version = ?, member_count = ?, notification_owner = ?,
                notification_message = ?, failure_reported_at = NULL,
                last_error = NULL, updated_at = ?
            WHERE operation_key = ?
              AND state NOT IN ('notified', 'notification_uncertain', 'expired')
            """,
            (
                state,
                int(version),
                int(member_count),
                1 if notification_owner else 0,
                notification_message,
                current,
                operation_key,
            ),
        ).rowcount
        if changed == 1 and notification_group_ids is not None:
            expected_groups = tuple(
                sorted({str(value).strip() for value in notification_group_ids})
            )
            if not expected_groups:
                raise RuntimeError("已提交的双群白名单同步缺少通知目标")
            existing_groups = tuple(
                row[0]
                for row in conn.execute(
                    """
                    SELECT group_id FROM qq_whitelist_sync_notifications
                    WHERE operation_key = ? ORDER BY group_id
                    """,
                    (operation_key,),
                )
            )
            if existing_groups and existing_groups != expected_groups:
                raise RuntimeError("同一本地计划任务出现互相冲突的通知群集合")
            notification_state = "pending" if notification_owner else "suppressed"
            for group_id in expected_groups:
                conn.execute(
                    """
                    INSERT OR IGNORE INTO qq_whitelist_sync_notifications(
                        operation_key, group_id, state, notification_message,
                        created_at, updated_at)
                    VALUES(?, ?, ?, ?, ?, ?)
                    """,
                    (
                        operation_key,
                        group_id,
                        notification_state,
                        notification_message,
                        current,
                        current,
                    ),
                )
        conn.commit()
        return True


def list_qq_whitelist_sync_notifications(operation_key: str):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        rows = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_notifications
            WHERE operation_key = ? ORDER BY group_id
            """,
            (str(operation_key),),
        ).fetchall()
        return [dict(row) for row in rows]


def recover_inflight_qq_whitelist_sync_notifications(
    operation_key: str, current_process_id: str, error: str, now=None
) -> int:
    """重启后把仅有发送意图、没有明确结果的群逐个冻结为未知。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_notifications
               SET state = 'uncertain', last_error = ?, updated_at = ?
             WHERE operation_key = ? AND state = 'sending'
               AND (sender_process_id IS NULL OR sender_process_id <> ?)
            """,
            (
                str(error)[:1000],
                current,
                str(operation_key),
                str(current_process_id),
            ),
        ).rowcount
        conn.commit()
        return changed


def claim_qq_whitelist_sync_group_notification(
    operation_key: str, group_id: str, sender_process_id: str, now=None
):
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_notifications
               SET state = 'sending',
                   notification_attempts = notification_attempts + 1,
                   sender_process_id = ?,
                   updated_at = ?
             WHERE operation_key = ? AND group_id = ? AND state = 'pending'
            """,
            (
                str(sender_process_id),
                current,
                str(operation_key),
                str(group_id),
            ),
        ).rowcount
        if changed == 1:
            conn.execute(
                """
                UPDATE qq_whitelist_sync_runs
                   SET notification_attempts = notification_attempts + 1,
                       updated_at = ?
                 WHERE operation_key = ?
                """,
                (current, str(operation_key)),
            )
        row = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_notifications
            WHERE operation_key = ? AND group_id = ?
            """,
            (str(operation_key), str(group_id)),
        ).fetchone()
        conn.commit()
        return dict(row) if changed == 1 and row else None


def release_qq_whitelist_sync_group_notification(
    operation_key: str, group_id: str, error: str, now=None
) -> bool:
    """仅在 OneBot 明确拒绝时释放单个群，其他群状态不受影响。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_notifications
               SET state = 'pending', sender_process_id = NULL,
                   last_error = ?, updated_at = ?
             WHERE operation_key = ? AND group_id = ? AND state = 'sending'
            """,
            (str(error)[:1000], current, str(operation_key), str(group_id)),
        ).rowcount
        conn.commit()
        return changed == 1


def mark_qq_whitelist_sync_group_notification_uncertain(
    operation_key: str, group_id: str, error: str, now=None
) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_notifications
               SET state = 'uncertain', sender_process_id = NULL,
                   last_error = ?, updated_at = ?
             WHERE operation_key = ? AND group_id = ? AND state = 'sending'
            """,
            (str(error)[:1000], current, str(operation_key), str(group_id)),
        ).rowcount
        conn.commit()
        return changed == 1


def complete_qq_whitelist_sync_group_notification(
    operation_key: str, group_id: str, now=None
) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_notifications
               SET state = 'sent', sent_at = COALESCE(sent_at, ?),
                   sender_process_id = NULL, last_error = NULL, updated_at = ?
             WHERE operation_key = ? AND group_id = ? AND state = 'sending'
            """,
            (current, current, str(operation_key), str(group_id)),
        ).rowcount
        conn.commit()
        return changed == 1


def refresh_qq_whitelist_sync_notification_state(operation_key: str, now=None):
    """按两个群的独立结果汇总运行状态，但从不把未知状态重新变为待发送。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        states = [
            row[0]
            for row in conn.execute(
                """
                SELECT state FROM qq_whitelist_sync_notifications
                WHERE operation_key = ? ORDER BY group_id
                """,
                (str(operation_key),),
            )
        ]
        if not states:
            conn.rollback()
            return None
        if all(state == "sent" for state in states):
            aggregate = "notified"
        elif all(state == "suppressed" for state in states):
            aggregate = "suppressed"
        elif any(state in {"pending", "sending"} for state in states):
            aggregate = "committed"
        else:
            aggregate = "notification_uncertain"
        conn.execute(
            """
            UPDATE qq_whitelist_sync_runs SET state = ?, updated_at = ?
            WHERE operation_key = ?
              AND state IN ('committed', 'notified', 'notification_uncertain', 'suppressed')
            """,
            (aggregate, current, str(operation_key)),
        )
        row = conn.execute(
            "SELECT * FROM qq_whitelist_sync_runs WHERE operation_key = ?",
            (str(operation_key),),
        ).fetchone()
        conn.commit()
        return dict(row) if row else None


def record_qq_whitelist_sync_error(operation_key: str, error: str, now=None) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET last_error = ?, updated_at = ?
            WHERE operation_key = ? AND state = 'started'
            """,
            (str(error)[:1000], current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def fail_qq_whitelist_sync(operation_key: str, error: str, now=None) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = 'failed', last_error = ?, updated_at = ?
            WHERE operation_key = ? AND state = 'started'
            """,
            (str(error)[:1000], current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def list_unreported_qq_whitelist_sync_failures(group_id: str, limit=24):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        rows = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE group_id = ? AND state = 'failed' AND failure_reported_at IS NULL
            ORDER BY scheduled_hour ASC
            LIMIT ?
            """,
            (str(group_id), max(1, min(int(limit), 168))),
        ).fetchall()
        return [dict(row) for row in rows]


def list_unreported_qq_whitelist_sync_source_failures(
    source_set_key: str, limit=24
):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        rows = conn.execute(
            """
            SELECT * FROM qq_whitelist_sync_runs
            WHERE source_set_key = ? AND state = 'failed'
              AND failure_reported_at IS NULL
            ORDER BY scheduled_hour ASC
            LIMIT ?
            """,
            (str(source_set_key), max(1, min(int(limit), 168))),
        ).fetchall()
        return [dict(row) for row in rows]


def mark_qq_whitelist_sync_failure_reported(operation_key: str, now=None) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET failure_reported_at = COALESCE(failure_reported_at, ?), updated_at = ?
            WHERE operation_key = ? AND state = 'failed'
            """,
            (current, current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def claim_qq_whitelist_sync_notification(operation_key: str, now=None):
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = 'notifying', notification_attempts = notification_attempts + 1,
                updated_at = ?
            WHERE operation_key = ? AND state = 'committed'
              AND notification_owner = 1 AND notification_acked_at IS NULL
            """,
            (current, operation_key),
        ).rowcount
        row = conn.execute(
            "SELECT * FROM qq_whitelist_sync_runs WHERE operation_key = ?",
            (operation_key,),
        ).fetchone()
        conn.commit()
        return dict(row) if changed == 1 and row else None


def release_qq_whitelist_sync_notification(
    operation_key: str, error: str, now=None
) -> bool:
    """仅在 OneBot 明确返回失败时释放，允许有限重试。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = 'committed', last_error = ?, updated_at = ?
            WHERE operation_key = ? AND state = 'notifying'
            """,
            (str(error)[:1000], current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def mark_qq_whitelist_sync_notification_uncertain(
    operation_key: str, error: str, now=None
) -> bool:
    """发送结果不确定时坚持至多一次，不自动重发可能已送达的群消息。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = 'notification_uncertain', last_error = ?, updated_at = ?
            WHERE operation_key = ? AND state = 'notifying'
            """,
            (str(error)[:1000], current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def complete_qq_whitelist_sync_notification(operation_key: str, now=None) -> bool:
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = 'notified', last_error = NULL, updated_at = ?
            WHERE operation_key = ? AND state = 'notifying'
            """,
            (current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def acknowledge_qq_whitelist_sync_notification(operation_key: str, acked_at, now=None):
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET notification_acked_at = COALESCE(notification_acked_at, ?), updated_at = ?
            WHERE operation_key = ? AND state = 'notified'
            """,
            (int(acked_at or current), current, operation_key),
        ).rowcount
        conn.commit()
        return changed == 1


def expire_old_qq_whitelist_sync_runs(group_id: str, current_hour: int, now=None) -> int:
    """过期计划任务绝不补同步或补群通知；只保留记录供审计。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
            SET state = 'expired', updated_at = ?
            WHERE group_id = ? AND scheduled_hour < ?
              AND state IN ('started', 'committed', 'notifying')
            """,
            (current, str(group_id), int(current_hour)),
        ).rowcount
        conn.commit()
        return changed


def expire_old_qq_whitelist_sync_source_runs(
    source_set_key: str, current_hour: int, now=None
) -> int:
    """过期双群任务不补写权威库；逐群待发通知也一并停止。"""
    current = int(time.time() if now is None else now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute("BEGIN IMMEDIATE")
        rows = [
            row[0]
            for row in conn.execute(
                """
                SELECT operation_key FROM qq_whitelist_sync_runs
                WHERE source_set_key = ? AND scheduled_hour < ?
                  AND state IN ('started', 'committed')
                """,
                (str(source_set_key), int(current_hour)),
            )
        ]
        changed = conn.execute(
            """
            UPDATE qq_whitelist_sync_runs
               SET state = 'expired', updated_at = ?
             WHERE source_set_key = ? AND scheduled_hour < ?
               AND state IN ('started', 'committed')
            """,
            (current, str(source_set_key), int(current_hour)),
        ).rowcount
        for operation_key in rows:
            conn.execute(
                """
                UPDATE qq_whitelist_sync_notifications
                   SET state = CASE WHEN state = 'sending' THEN 'uncertain' ELSE 'expired' END,
                       last_error = COALESCE(last_error, '通知窗口已过期'),
                       updated_at = ?
                 WHERE operation_key = ? AND state IN ('pending', 'sending')
                """,
                (current, operation_key),
            )
        conn.commit()
        return changed


def _verification_now(value=None) -> int:
    return int(time.time() if value is None else value)


def _verification_dict(row):
    return dict(row) if row else None


def get_member_verification(verification_id: int):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?",
            (int(verification_id),),
        ).fetchone()
        return _verification_dict(row)


def get_active_member_verification(group_id: str, newcomer_qq: str):
    """读取某位群成员当前唯一的验证会话。"""
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN (
                   'approval_pending', 'awaiting_join',
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'reminding'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (str(group_id), str(newcomer_qq)),
        ).fetchone()
        return _verification_dict(row)


def prepare_member_verification_approval(
    group_id: str,
    newcomer_qq: str,
    inviter_qq: str,
    request_event_time: int,
    authorization_seconds: int,
    now=None,
):
    """在外部审批动作前持久化可信申请，避免成功响应与入群通知之间丢会话。"""
    group_id = str(group_id)
    newcomer_qq = str(newcomer_qq)
    inviter_qq = str(inviter_qq)
    event_value = _verification_now(request_event_time)
    now_value = _verification_now(now)
    expires_at = now_value + max(60, int(authorization_seconds))
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        conn.execute(
            """
            UPDATE member_verifications
               SET state = 'cancelled',
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   last_error = '加群审批登记授权已过期',
                   ended_at = ?,
                   updated_at = ?
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN ('approval_pending', 'awaiting_join')
               AND deadline_at IS NOT NULL
               AND deadline_at < ?
            """,
            (now_value, now_value, group_id, newcomer_qq, now_value),
        )
        active = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN (
                   'approval_pending', 'awaiting_join',
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'reminding'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (group_id, newcomer_qq),
        ).fetchone()
        if active:
            if active["state"] == "approval_pending":
                conn.execute(
                    """
                    UPDATE member_verifications
                       SET inviter_qq = ?,
                           candidate_qq = ?,
                           deadline_at = ?,
                           last_error = NULL,
                           updated_at = ?
                     WHERE id = ? AND state = 'approval_pending'
                    """,
                    (
                        inviter_qq,
                        inviter_qq,
                        expires_at,
                        now_value,
                        active["id"],
                    ),
                )
                active = conn.execute(
                    "SELECT * FROM member_verifications WHERE id = ?",
                    (active["id"],),
                ).fetchone()
            conn.commit()
            result = dict(active)
            result["created"] = False
            result["reason"] = "active"
            return result

        cur = conn.execute(
            """
            INSERT INTO member_verifications (
                group_id, newcomer_qq, nickname, join_event_time, state,
                inviter_qq, candidate_qq, deadline_at, created_at, updated_at
            ) VALUES (?, ?, '', ?, 'approval_pending', ?, ?, ?, ?, ?)
            """,
            (
                group_id,
                newcomer_qq,
                event_value,
                inviter_qq,
                inviter_qq,
                expires_at,
                now_value,
                now_value,
            ),
        )
        row = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (cur.lastrowid,)
        ).fetchone()
        conn.commit()
        result = dict(row)
        result["created"] = True
        result["reason"] = "approval_prepared"
        return result


def complete_member_verification_approval(verification_id: int, now=None) -> bool:
    """OneBot 明确同意申请后，将预备记录推进到等待真实入群通知。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'awaiting_join',
                   last_error = NULL,
                   updated_at = ?
             WHERE id = ? AND state = 'approval_pending'
            """,
            (now_value, int(verification_id)),
        )
        conn.commit()
        return cur.rowcount == 1


def record_member_verification_approval_failure(
    verification_id: int, detail: str, now=None
) -> bool:
    """审批失败或响应不确定时只记错误，不把申请提升为可回答会话。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET last_error = ?, updated_at = ?
             WHERE id = ? AND state = 'approval_pending'
            """,
            (
                str(detail or "加群审批动作失败")[:1000],
                now_value,
                int(verification_id),
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def activate_member_verification_from_reply(
    group_id: str,
    newcomer_qq: str,
    reminder_seconds: int,
    event_time=None,
    now=None,
):
    """入群通知丢失时，以真实群消息恢复审批后登记；过期授权绝不复活。"""
    now_value = _verification_now(now)
    event_value = _verification_now(now_value if event_time is None else event_time)
    deadline = now_value + max(1, int(reminder_seconds))
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state = 'awaiting_join'
             ORDER BY id DESC
             LIMIT 1
            """,
            (str(group_id), str(newcomer_qq)),
        ).fetchone()
        if not row:
            conn.rollback()
            return None
        if row["deadline_at"] is None or now_value > int(row["deadline_at"]):
            conn.execute(
                """
                UPDATE member_verifications
                   SET state = 'cancelled',
                       last_error = '加群审批登记授权已过期',
                       ended_at = ?,
                       updated_at = ?
                 WHERE id = ? AND state = 'awaiting_join'
                """,
                (now_value, now_value, row["id"]),
            )
            conn.commit()
            return None
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   join_event_time = ?,
                   candidate_qq = NULL,
                   response_message_key = NULL,
                   response_event_time = NULL,
                   prompt_sent_at = ?,
                   deadline_at = ?,
                   reminder_attempts = 0,
                   reminder_sent_at = NULL,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   last_error = '入群通知缺失，已由真实群消息恢复登记会话',
                   updated_at = ?
             WHERE id = ?
               AND state = 'awaiting_join'
            """,
            (event_value, now_value, deadline, now_value, row["id"]),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return None
        updated = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return dict(updated)


def get_member_verification_responses(verification_id: int):
    """测试和审计使用：按收到顺序读取回答核查记录。"""
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        rows = conn.execute(
            """
            SELECT * FROM member_verification_responses
             WHERE verification_id = ?
             ORDER BY id
            """,
            (int(verification_id),),
        ).fetchall()
        return [dict(row) for row in rows]


def has_member_verification_response(
    group_id: str, newcomer_qq: str, message_key: str
) -> bool:
    """识别已持久化的 OneBot 回放，完成会话后也不得落入普通聊天兜底。"""
    with sqlite3.connect(DB_PATH) as conn:
        row = conn.execute(
            """
            SELECT 1 FROM member_verification_responses
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND message_key = ?
             LIMIT 1
            """,
            (str(group_id), str(newcomer_qq), str(message_key)),
        ).fetchone()
        return row is not None


def _complete_member_verification_from_join(
    conn,
    active,
    nickname: str,
    join_event_time: int,
    inviter_qq: str,
    now_value: int,
    reason: str,
):
    """在调用方持有写事务时，把真实入群与可信邀请人原子合并成终态。"""
    response_key = str(active["response_message_key"] or "")
    cur = conn.execute(
        """
        UPDATE member_verifications
           SET state = 'verified',
               nickname = ?,
               join_event_time = ?,
               inviter_qq = ?,
               candidate_qq = ?,
               deadline_at = NULL,
               claim_token = NULL,
               claim_kind = NULL,
               claimed_at = NULL,
               next_attempt_at = NULL,
               last_error = NULL,
               verified_at = ?,
               ended_at = ?,
               updated_at = ?
         WHERE id = ?
           AND state IN (
               'approval_pending', 'awaiting_join',
               'awaiting_prompt', 'pending', 'checking_inviter',
               'reminding'
           )
        """,
        (
            str(nickname or ""),
            int(join_event_time),
            str(inviter_qq),
            str(inviter_qq),
            int(now_value),
            int(now_value),
            int(now_value),
            active["id"],
        ),
    )
    if cur.rowcount != 1:
        return None
    if response_key:
        conn.execute(
            """
            UPDATE member_verification_responses
               SET result = 'superseded',
                   detail = '真实入群通知已提供可信邀请人记录',
                   updated_at = ?
             WHERE verification_id = ? AND message_key = ?
               AND result IN ('checking', 'retrying')
            """,
            (int(now_value), active["id"], response_key),
        )
    completed = conn.execute(
        "SELECT * FROM member_verifications WHERE id = ?", (active["id"],)
    ).fetchone()
    result = dict(completed)
    result["created"] = True
    result["reason"] = str(reason)
    return result


def start_member_verification(
    group_id: str,
    newcomer_qq: str,
    nickname: str,
    join_event_time: int,
    now=None,
    verified_inviter_qq: str | None = None,
):
    """幂等登记真实入群；已有可信邀请人时直接完成，否则创建回答会话。"""
    group_id = str(group_id)
    newcomer_qq = str(newcomer_qq)
    join_event_time = _verification_now(join_event_time)
    now_value = _verification_now(now)
    notice_inviter = str(verified_inviter_qq or "").strip()
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        conn.execute(
            """
            UPDATE member_verifications
               SET state = 'cancelled',
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   last_error = '加群审批登记授权已过期',
                   ended_at = ?,
                   updated_at = ?
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN ('approval_pending', 'awaiting_join')
               AND deadline_at IS NOT NULL
               AND deadline_at < ?
            """,
            (now_value, now_value, group_id, newcomer_qq, now_value),
        )
        active = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN (
                   'approval_pending', 'awaiting_join',
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'reminding'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (group_id, newcomer_qq),
        ).fetchone()
        if active:
            if active["state"] in ("approval_pending", "awaiting_join"):
                replay = conn.execute(
                    """
                    SELECT * FROM member_verifications
                     WHERE group_id = ?
                       AND newcomer_qq = ?
                       AND join_event_time = ?
                       AND id <> ?
                     ORDER BY id DESC
                     LIMIT 1
                    """,
                    (group_id, newcomer_qq, join_event_time, active["id"]),
                ).fetchone()
                if replay:
                    conn.execute(
                        """
                        UPDATE member_verifications
                           SET state = 'cancelled',
                               last_error = '同一入群通知已有持久会话',
                               ended_at = ?,
                               updated_at = ?
                         WHERE id = ?
                           AND state IN ('approval_pending', 'awaiting_join')
                        """,
                        (now_value, now_value, active["id"]),
                    )
                    conn.commit()
                    result = dict(replay)
                    result["created"] = False
                    result["reason"] = "replayed_notice"
                    return result
                approved_inviter = str(active["inviter_qq"] or "")
                final_inviter = approved_inviter or notice_inviter
                if final_inviter:
                    completed = _complete_member_verification_from_join(
                        conn,
                        active,
                        nickname,
                        join_event_time,
                        final_inviter,
                        now_value,
                        (
                            "approved_join_verified"
                            if approved_inviter
                            else "direct_invite_verified"
                        ),
                    )
                    if not completed:
                        conn.rollback()
                        result = dict(active)
                        result["created"] = False
                        result["reason"] = "activation_conflict"
                        return result
                    conn.commit()
                    return completed
                cur = conn.execute(
                    """
                    UPDATE member_verifications
                       SET state = 'awaiting_prompt',
                           nickname = ?,
                           join_event_time = ?,
                           inviter_qq = ?,
                           candidate_qq = ?,
                           response_message_key = NULL,
                           response_event_time = NULL,
                           prompt_sent_at = NULL,
                           deadline_at = NULL,
                           reminder_attempts = 0,
                           reminder_sent_at = NULL,
                           claim_token = NULL,
                           claim_kind = NULL,
                           claimed_at = NULL,
                           next_attempt_at = NULL,
                           last_error = NULL,
                           updated_at = ?
                     WHERE id = ?
                       AND state IN ('approval_pending', 'awaiting_join')
                    """,
                    (
                        str(nickname or ""),
                        join_event_time,
                        (
                            str(active["inviter_qq"])
                            if active["state"] == "awaiting_join"
                            and active["inviter_qq"] not in (None, "")
                            else None
                        ),
                        (
                            str(active["inviter_qq"])
                            if active["state"] == "awaiting_join"
                            and active["inviter_qq"] not in (None, "")
                            else None
                        ),
                        now_value,
                        active["id"],
                    ),
                )
                if cur.rowcount != 1:
                    conn.rollback()
                    result = dict(active)
                    result["created"] = False
                    result["reason"] = "activation_conflict"
                    return result
                activated = conn.execute(
                    "SELECT * FROM member_verifications WHERE id = ?",
                    (active["id"],),
                ).fetchone()
                conn.commit()
                result = dict(activated)
                result["created"] = True
                result["reason"] = (
                    "approved_join"
                    if active["state"] == "awaiting_join"
                    else "joined_after_unconfirmed_approval"
                )
                return result
            stored_inviter = str(active["inviter_qq"] or "")
            final_inviter = stored_inviter or notice_inviter
            if final_inviter:
                completed = _complete_member_verification_from_join(
                    conn,
                    active,
                    nickname,
                    join_event_time,
                    final_inviter,
                    now_value,
                    (
                        "approved_join_verified"
                        if stored_inviter
                        else "direct_invite_verified"
                    ),
                )
                if not completed:
                    conn.rollback()
                    result = dict(active)
                    result["created"] = False
                    result["reason"] = "activation_conflict"
                    return result
                conn.commit()
                return completed
            conn.commit()
            result = dict(active)
            result["created"] = False
            result["reason"] = "active"
            return result

        latest = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ? AND newcomer_qq = ?
             ORDER BY join_event_time DESC, id DESC
             LIMIT 1
            """,
            (group_id, newcomer_qq),
        ).fetchone()
        if latest and int(latest["join_event_time"]) >= join_event_time:
            conn.commit()
            result = dict(latest)
            result["created"] = False
            result["reason"] = "replayed_notice"
            return result

        if notice_inviter:
            cur = conn.execute(
                """
                INSERT INTO member_verifications (
                    group_id, newcomer_qq, nickname, join_event_time, state,
                    inviter_qq, candidate_qq, verified_at, ended_at,
                    created_at, updated_at
                ) VALUES (?, ?, ?, ?, 'verified', ?, ?, ?, ?, ?, ?)
                """,
                (
                    group_id,
                    newcomer_qq,
                    str(nickname or ""),
                    join_event_time,
                    notice_inviter,
                    notice_inviter,
                    now_value,
                    now_value,
                    now_value,
                    now_value,
                ),
            )
        else:
            cur = conn.execute(
                """
                INSERT INTO member_verifications (
                    group_id, newcomer_qq, nickname, join_event_time, state,
                    created_at, updated_at
                ) VALUES (?, ?, ?, ?, 'awaiting_prompt', ?, ?)
                """,
                (
                    group_id,
                    newcomer_qq,
                    str(nickname or ""),
                    join_event_time,
                    now_value,
                    now_value,
                ),
            )
        row = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (cur.lastrowid,)
        ).fetchone()
        conn.commit()
        result = dict(row)
        result["created"] = True
        result["reason"] = (
            "direct_invite_verified" if notice_inviter else "created"
        )
        return result


def mark_member_verification_left(
    group_id: str, newcomer_qq: str, now=None, detail: str = "成员已离群"
) -> bool:
    """收到权威退群通知后结束活动会话，避免之后误踢。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'left',
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   last_error = ?,
                   ended_at = ?,
                   updated_at = ?
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN (
                   'approval_pending', 'awaiting_join',
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'reminding'
               )
            """,
            (
                str(detail or "成员已离群")[:1000],
                now_value,
                now_value,
                str(group_id),
                str(newcomer_qq),
            ),
        )
        conn.commit()
        return cur.rowcount > 0


def cancel_member_verifications_outside_groups(group_ids, now=None) -> int:
    """配置停用或移除目标群时取消遗留会话，防止以后重新启用时补踢。"""
    now_value = _verification_now(now)
    normalized = sorted({str(value) for value in (group_ids or [])})
    with sqlite3.connect(DB_PATH) as conn:
        args = [now_value, now_value]
        group_clause = ""
        if normalized:
            placeholders = ",".join("?" for _ in normalized)
            group_clause = f" AND group_id NOT IN ({placeholders})"
            args.extend(normalized)
        cur = conn.execute(
            f"""
            UPDATE member_verifications
               SET state = 'cancelled',
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   last_error = '目标群配置已停用，取消遗留验证',
                   ended_at = ?,
                   updated_at = ?
             WHERE state IN (
                   'approval_pending', 'awaiting_join',
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'reminding'
               )
               {group_clause}
            """,
            tuple(args),
        )
        conn.commit()
        return cur.rowcount


def claim_member_verification_prompt(
    verification_id: int | None = None,
    now=None,
    lease_seconds: int = 30,
    group_ids=None,
):
    """领取一条尚未成功发出提示的会话；租约过期后可由重启实例恢复。"""
    now_value = _verification_now(now)
    stale = now_value - max(5, int(lease_seconds))
    token = uuid4().hex
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        args = [now_value, stale]
        group_clause = ""
        if group_ids is not None:
            normalized_groups = sorted({str(value) for value in group_ids})
            if not normalized_groups:
                conn.rollback()
                return None
            placeholders = ",".join("?" for _ in normalized_groups)
            group_clause = f" AND group_id IN ({placeholders})"
            args.extend(normalized_groups)
        id_clause = ""
        if verification_id is not None:
            id_clause = " AND id = ?"
            args.append(int(verification_id))
        row = conn.execute(
            f"""
            SELECT * FROM member_verifications
             WHERE state = 'awaiting_prompt'
               AND COALESCE(next_attempt_at, 0) <= ?
               AND (claim_token IS NULL OR claimed_at IS NULL OR claimed_at <= ?)
               {group_clause}
               {id_clause}
             ORDER BY created_at, id
             LIMIT 1
            """,
            tuple(args),
        ).fetchone()
        if not row:
            conn.rollback()
            return None
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET claim_token = ?,
                   claim_kind = 'prompt',
                   claimed_at = ?,
                   next_attempt_at = NULL,
                   prompt_attempts = prompt_attempts + 1,
                   updated_at = ?
             WHERE id = ?
               AND state = 'awaiting_prompt'
               AND (claim_token IS NULL OR claimed_at IS NULL OR claimed_at <= ?)
            """,
            (token, now_value, now_value, row["id"], stale),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return None
        claimed = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return dict(claimed)


def complete_member_verification_prompt(
    verification_id: int,
    claim_token: str,
    reminder_seconds: int,
    sent_at=None,
) -> bool:
    """只在 OneBot 确认首次提示发送成功后启动一次提醒计时。"""
    sent_value = _verification_now(sent_at)
    reminder_value = max(1, int(reminder_seconds))
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   prompt_sent_at = ?,
                   deadline_at = ?,
                   reminder_attempts = 0,
                   reminder_sent_at = NULL,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   last_error = NULL,
                   updated_at = ?
             WHERE id = ?
               AND state = 'awaiting_prompt'
               AND claim_kind = 'prompt'
               AND claim_token = ?
            """,
            (
                sent_value,
                sent_value + reminder_value,
                sent_value,
                int(verification_id),
                str(claim_token),
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def release_member_verification_claim(
    verification_id: int,
    claim_token: str,
    claim_kind: str,
    error: str,
    now=None,
    retry_delay_seconds: int = 5,
) -> bool:
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = ?,
                   last_error = ?,
                   updated_at = ?
             WHERE id = ? AND claim_token = ? AND claim_kind = ?
            """,
            (
                now_value + max(1, int(retry_delay_seconds)),
                str(error or "未知错误")[:1000],
                now_value,
                int(verification_id),
                str(claim_token),
                str(claim_kind),
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def begin_member_inviter_check(
    group_id: str,
    newcomer_qq: str,
    candidate_qq: str,
    message_key: str,
    event_time: int,
    received_at=None,
):
    """原子接收一条回答，并抢占可能并发的提醒租约。"""
    now_value = _verification_now(received_at)
    event_value = _verification_now(event_time)
    token = uuid4().hex
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN (
                   'awaiting_prompt', 'pending', 'checking_inviter', 'reminding'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (str(group_id), str(newcomer_qq)),
        ).fetchone()
        if not row:
            conn.rollback()
            return {"status": "no_session"}
        if row["state"] == "checking_inviter":
            conn.rollback()
            return {"status": "busy", "verification": dict(row)}
        try:
            conn.execute(
                """
                INSERT INTO member_verification_responses (
                    verification_id, group_id, newcomer_qq, message_key,
                    candidate_qq, event_time, received_at, result, updated_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, 'checking', ?)
                """,
                (
                    row["id"],
                    str(group_id),
                    str(newcomer_qq),
                    str(message_key),
                    str(candidate_qq),
                    event_value,
                    now_value,
                    now_value,
                ),
            )
        except sqlite3.IntegrityError:
            conn.rollback()
            return {"status": "duplicate", "verification": dict(row)}
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'checking_inviter',
                   candidate_qq = ?,
                   response_message_key = ?,
                   response_event_time = ?,
                   claim_token = ?,
                   claim_kind = 'inviter',
                   claimed_at = ?,
                   next_attempt_at = NULL,
                   check_attempts = check_attempts + 1,
                   last_error = NULL,
                   updated_at = ?
             WHERE id = ?
               AND state IN ('awaiting_prompt', 'pending', 'reminding')
            """,
            (
                str(candidate_qq),
                str(message_key),
                event_value,
                token,
                now_value,
                now_value,
                row["id"],
            ),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return {"status": "conflict"}
        claimed = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return {"status": "claimed", "verification": dict(claimed)}


def claim_pending_member_inviter_check(
    now=None, lease_seconds: int = 30, group_ids=None
):
    """恢复 API 失败、断线或进程中止时留下的邀请人核查。"""
    now_value = _verification_now(now)
    stale = now_value - max(5, int(lease_seconds))
    token = uuid4().hex
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        args = [now_value, stale]
        group_clause = ""
        if group_ids is not None:
            normalized_groups = sorted({str(value) for value in group_ids})
            if not normalized_groups:
                conn.rollback()
                return None
            placeholders = ",".join("?" for _ in normalized_groups)
            group_clause = f" AND group_id IN ({placeholders})"
            args.extend(normalized_groups)
        row = conn.execute(
            f"""
            SELECT * FROM member_verifications
             WHERE state = 'checking_inviter'
               AND candidate_qq IS NOT NULL
               AND COALESCE(next_attempt_at, 0) <= ?
               AND (claim_token IS NULL OR claimed_at IS NULL OR claimed_at <= ?)
               {group_clause}
             ORDER BY updated_at, id
             LIMIT 1
            """,
            tuple(args),
        ).fetchone()
        if not row:
            conn.rollback()
            return None
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET claim_token = ?,
                   claim_kind = 'inviter',
                   claimed_at = ?,
                   next_attempt_at = NULL,
                   check_attempts = check_attempts + 1,
                   updated_at = ?
             WHERE id = ?
               AND state = 'checking_inviter'
               AND (claim_token IS NULL OR claimed_at IS NULL OR claimed_at <= ?)
            """,
            (token, now_value, now_value, row["id"], stale),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return None
        claimed = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return dict(claimed)


def defer_member_inviter_check(
    verification_id: int,
    claim_token: str,
    error: str,
    now=None,
    retry_delay_seconds: int = 5,
) -> bool:
    """权威成员接口失败时保留玩家答案，不通过也不触发自动移出。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT response_message_key FROM member_verifications
             WHERE id = ?
               AND state = 'checking_inviter'
               AND claim_kind = 'inviter'
               AND claim_token = ?
            """,
            (int(verification_id), str(claim_token)),
        ).fetchone()
        if not row:
            conn.rollback()
            return False
        detail = str(error or "成员接口暂时不可用")[:1000]
        conn.execute(
            """
            UPDATE member_verifications
               SET claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = ?,
                   last_error = ?,
                   updated_at = ?
             WHERE id = ?
            """,
            (
                now_value + max(1, int(retry_delay_seconds)),
                detail,
                now_value,
                int(verification_id),
            ),
        )
        conn.execute(
            """
            UPDATE member_verification_responses
               SET result = 'retrying', detail = ?, updated_at = ?
             WHERE verification_id = ? AND message_key = ?
            """,
            (
                detail,
                now_value,
                int(verification_id),
                str(row["response_message_key"] or ""),
            ),
        )
        conn.commit()
        return True


def complete_member_inviter_check(
    verification_id: int, claim_token: str, inviter_qq: str, now=None
) -> bool:
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT response_message_key FROM member_verifications
             WHERE id = ?
               AND state = 'checking_inviter'
               AND claim_kind = 'inviter'
               AND claim_token = ?
            """,
            (int(verification_id), str(claim_token)),
        ).fetchone()
        if not row:
            conn.rollback()
            return False
        conn.execute(
            """
            UPDATE member_verifications
               SET state = 'verified',
                   inviter_qq = ?,
                   candidate_qq = ?,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   last_error = NULL,
                   verified_at = ?,
                   ended_at = ?,
                   updated_at = ?
             WHERE id = ?
            """,
            (
                str(inviter_qq),
                str(inviter_qq),
                now_value,
                now_value,
                now_value,
                int(verification_id),
            ),
        )
        conn.execute(
            """
            UPDATE member_verification_responses
               SET result = 'verified', detail = NULL, updated_at = ?
             WHERE verification_id = ? AND message_key = ?
            """,
            (
                now_value,
                int(verification_id),
                str(row["response_message_key"] or ""),
            ),
        )
        conn.commit()
        return True


def reject_member_inviter_check(
    verification_id: int, claim_token: str, detail: str, now=None
):
    """候选 QQ 不在群时回到待回答状态；已过期则交给超时任务。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE id = ?
               AND state = 'checking_inviter'
               AND claim_kind = 'inviter'
               AND claim_token = ?
            """,
            (int(verification_id), str(claim_token)),
        ).fetchone()
        if not row:
            conn.rollback()
            return None
        next_state = "pending" if row["prompt_sent_at"] is not None else "awaiting_prompt"
        conn.execute(
            """
            UPDATE member_verifications
               SET state = ?,
                   invalid_attempts = invalid_attempts + 1,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   last_error = NULL,
                   updated_at = ?
             WHERE id = ?
            """,
            (next_state, now_value, int(verification_id)),
        )
        conn.execute(
            """
            UPDATE member_verification_responses
               SET result = 'not_member', detail = ?, updated_at = ?
             WHERE verification_id = ? AND message_key = ?
            """,
            (
                str(detail or "邀请人不在群内")[:1000],
                now_value,
                int(verification_id),
                str(row["response_message_key"] or ""),
            ),
        )
        updated = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?",
            (int(verification_id),),
        ).fetchone()
        conn.commit()
        result = dict(updated)
        # 截止时间只决定一次提醒何时发送，不再限制玩家回答。
        result["can_retry"] = True
        return result


def claim_due_member_verification_reminder(
    now=None,
    lease_seconds: int = 30,
    group_ids=None,
    max_attempts: int = MEMBER_VERIFICATION_REMINDER_MAX_ATTEMPTS,
):
    """原子领取到期提醒；崩溃租约最多恢复有限次数，不会无限刷屏。"""
    now_value = _verification_now(now)
    stale = now_value - max(5, int(lease_seconds))
    attempt_limit = max(1, int(max_attempts))
    token = uuid4().hex
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        # 若实例连续在提醒租约内崩溃，达到上限后直接停止提醒并保留回答资格。
        conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   deadline_at = NULL,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   last_error = '提醒恢复次数已达上限，停止提醒但仍可回答邀请人 QQ',
                   updated_at = ?
             WHERE state = 'reminding'
               AND reminder_attempts >= ?
               AND (claimed_at IS NULL OR claimed_at <= ?)
            """,
            (now_value, attempt_limit, stale),
        )
        group_clause = ""
        args = []
        if group_ids is not None:
            normalized_groups = sorted({str(value) for value in group_ids})
            if not normalized_groups:
                conn.rollback()
                return None
            placeholders = ",".join("?" for _ in normalized_groups)
            group_clause = f"group_id IN ({placeholders}) AND "
            args.extend(normalized_groups)
        args.extend(
            (now_value, now_value, attempt_limit, attempt_limit, stale)
        )
        row = conn.execute(
            f"""
            SELECT * FROM member_verifications
             WHERE {group_clause}((
                    state = 'pending'
                AND deadline_at IS NOT NULL
                AND reminder_sent_at IS NULL
                AND deadline_at <= ?
                AND COALESCE(next_attempt_at, 0) <= ?
                AND reminder_attempts < ?
             ) OR (
                    state = 'reminding'
                AND reminder_sent_at IS NULL
                AND reminder_attempts < ?
                AND (claimed_at IS NULL OR claimed_at <= ?)
             ))
             ORDER BY COALESCE(deadline_at, updated_at), id
             LIMIT 1
            """,
            tuple(args),
        ).fetchone()
        if not row:
            # 可能已经把达到崩溃恢复上限的提醒前向收敛为无限期 pending。
            conn.commit()
            return None
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'reminding',
                   claim_token = ?,
                   claim_kind = 'reminder',
                   claimed_at = ?,
                   next_attempt_at = NULL,
                   reminder_attempts = reminder_attempts + 1,
                   updated_at = ?
             WHERE id = ?
               AND (
                    (state = 'pending' AND deadline_at IS NOT NULL
                     AND reminder_sent_at IS NULL
                     AND deadline_at <= ? AND COALESCE(next_attempt_at, 0) <= ?
                     AND reminder_attempts < ?)
                 OR (state = 'reminding'
                     AND reminder_sent_at IS NULL
                     AND reminder_attempts < ?
                     AND (claimed_at IS NULL OR claimed_at <= ?))
               )
            """,
            (
                token,
                now_value,
                now_value,
                row["id"],
                now_value,
                now_value,
                attempt_limit,
                attempt_limit,
                stale,
            ),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return None
        claimed = conn.execute(
            "SELECT * FROM member_verifications WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return dict(claimed)


def complete_member_verification_reminder(
    verification_id: int, claim_token: str, now=None
) -> bool:
    """提醒确认送达后清除截止时间，使会话可无限期回答且不再提醒。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   deadline_at = NULL,
                   reminder_sent_at = ?,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = NULL,
                   last_error = NULL,
                   updated_at = ?
             WHERE id = ?
               AND state = 'reminding'
               AND claim_kind = 'reminder'
               AND claim_token = ?
            """,
            (
                now_value,
                now_value,
                int(verification_id),
                str(claim_token),
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def release_member_verification_reminder(
    verification_id: int,
    claim_token: str,
    error: str,
    now=None,
    max_attempts: int = MEMBER_VERIFICATION_REMINDER_MAX_ATTEMPTS,
    retry_base_seconds: int = MEMBER_VERIFICATION_REMINDER_RETRY_BASE_SECONDS,
    retry_max_seconds: int = MEMBER_VERIFICATION_REMINDER_RETRY_MAX_SECONDS,
) -> bool:
    """提醒失败时指数退避；到达上限便停止提醒，但会话仍可继续回答。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT reminder_attempts FROM member_verifications
             WHERE id = ?
               AND state = 'reminding'
               AND claim_token = ?
               AND claim_kind = 'reminder'
            """,
            (int(verification_id), str(claim_token)),
        ).fetchone()
        if not row:
            conn.rollback()
            return False
        attempts = int(row["reminder_attempts"] or 0)
        exhausted = attempts >= max(1, int(max_attempts))
        retry_delay = min(
            max(1, int(retry_max_seconds)),
            max(1, int(retry_base_seconds))
            * (2 ** min(30, max(0, attempts - 1))),
        )
        detail = str(error or "提醒发送失败")[:1000]
        if exhausted:
            detail += "；已停止提醒，玩家仍可继续回答邀请人 QQ"
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   deadline_at = CASE WHEN ? = 1 THEN NULL ELSE deadline_at END,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = ?,
                   last_error = ?,
                   updated_at = ?
             WHERE id = ?
               AND state = 'reminding'
               AND claim_token = ?
               AND claim_kind = 'reminder'
            """,
            (
                int(exhausted),
                None if exhausted else now_value + retry_delay,
                detail,
                now_value,
                int(verification_id),
                str(claim_token),
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def complete_member_verification_absent(
    verification_id: int, claim_token: str, now=None
) -> bool:
    """核查邀请人答案时确认新人已离群，安全结束会话。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'left',
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   last_error = '权威成员列表确认成员已离群',
                   ended_at = ?,
                   updated_at = ?
             WHERE id = ?
               AND state = 'checking_inviter'
               AND claim_token = ?
            """,
            (now_value, now_value, int(verification_id), str(claim_token)),
        )
        conn.commit()
        return cur.rowcount == 1


def normalize_personality(value: str | None) -> str:
    personality = str(value or DEFAULT_PERSONALITY).strip().lower()
    return personality if personality in PERSONALITIES else DEFAULT_PERSONALITY


def get_group_personality(group_id: str) -> str:
    with sqlite3.connect(DB_PATH) as conn:
        row = conn.execute(
            "SELECT personality FROM group_settings WHERE group_id = ?",
            (str(group_id),),
        ).fetchone()
        return normalize_personality(row[0] if row else None)


def set_group_personality(
    group_id: str, personality: str, updated_by: str
) -> str:
    requested = str(personality or "").strip().lower()
    if requested not in GROUP_PERSONALITIES:
        raise ValueError(f"不支持的性格: {personality}")
    selected = requested
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute(
            """
            INSERT INTO group_settings
                (group_id, personality, updated_by, updated_at)
            VALUES (?, ?, ?, ?)
            ON CONFLICT(group_id) DO UPDATE SET
                personality = excluded.personality,
                updated_by = excluded.updated_by,
                updated_at = excluded.updated_at
            """,
            (str(group_id), selected, str(updated_by), now_text),
        )
        conn.commit()
    return selected


def add_chat_message(
    qq: str,
    nickname: str,
    group_id: str,
    content: str,
    kind: str = "chat",
    media=None,
    personality: str = DEFAULT_PERSONALITY,
    assistant_id: str = DEFAULT_ASSISTANT_ID,
    source_message_key: str | None = None,
    source_auth: str | None = None,
    context_text: str = "",
):
    """新增聊天请求并返回编号；管理员消息重放时返回 ``None``。"""
    if kind not in ("chat", "bug_intake", "admin_agent"):
        raise ValueError(f"不支持的消息类型: {kind}")
    selected_assistant = str(assistant_id or "").strip().lower()
    if not _ASSISTANT_ID_RE.fullmatch(selected_assistant):
        raise ValueError(f"不支持的助理标识: {assistant_id}")
    source_key = str(source_message_key or "").strip() or None
    source_authorization = str(source_auth or "").strip() or None
    embedded_context = str(context_text or "")
    if source_key is not None:
        if kind != "admin_agent":
            raise ValueError("只有管理员 Agent 消息可以设置来源消息键")
        if len(source_key) > 200:
            raise ValueError("来源消息键过长")
    if source_authorization is not None and kind != "admin_agent":
        raise ValueError("只有管理员 Agent 消息可以设置来源授权")
    if len(source_authorization or "") > 80:
        raise ValueError("来源授权标识过长")
    if embedded_context and kind != "admin_agent":
        raise ValueError("只有管理员 Agent 消息可以设置引用上下文")
    if len(embedded_context) > 12000:
        raise ValueError("管理员 Agent 引用上下文超过 12000 字")
    now_text = datetime.now().isoformat(timespec="seconds")
    media_json = json.dumps(media or [], ensure_ascii=False)
    selected_personality = normalize_personality(personality)
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute("BEGIN IMMEDIATE")
        if source_key is not None:
            existing = conn.execute(
                """
                SELECT id FROM chat_messages
                 WHERE kind = 'admin_agent'
                   AND assistant_id = ?
                   AND source_message_key = ?
                """,
                (selected_assistant, source_key),
            ).fetchone()
            if existing:
                conn.commit()
                return None
        cur = conn.execute(
            """
            INSERT INTO chat_messages
                (kind, qq, nickname, group_id, content, media_json,
                 personality, assistant_id, source_message_key, source_auth,
                 context_text, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                kind, str(qq), nickname or "", str(group_id), content,
                media_json, selected_personality,
                selected_assistant, source_key, source_authorization,
                embedded_context,
                now_text, now_text,
            ),
        )
        conn.commit()
        return cur.lastrowid


def chat_request_status(qq: str, group_id: str) -> dict:
    """返回用户在当前群的排队数和最近请求时间，用于限流。"""
    with sqlite3.connect(DB_PATH) as conn:
        row = conn.execute(
            """
            SELECT
                SUM(CASE WHEN state IN ('queued', 'claimed') THEN 1 ELSE 0 END),
                MAX(created_at)
              FROM chat_messages
             WHERE qq = ? AND group_id = ?
            """,
            (str(qq), str(group_id)),
        ).fetchone()
        return {
            "pending": int(row[0] or 0),
            "last_created_at": row[1] if row else None,
        }


def add_bug_followup(
    qq: str,
    nickname: str,
    group_id: str,
    content: str,
    media=None,
    max_age_seconds: int = 1800,
    personality: str = DEFAULT_PERSONALITY,
):
    """把玩家对最近一次 Bug 追问的下一条消息合并后重新检查。"""
    content = str(content or "").strip()
    if not content and not media:
        return None
    now = datetime.now()
    now_text = now.isoformat(timespec="seconds")
    since_text = (
        now - timedelta(seconds=max(60, max_age_seconds))
    ).isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        previous = conn.execute(
            """
            SELECT * FROM chat_messages
             WHERE qq = ?
               AND group_id = ?
               AND kind = 'bug_intake'
               AND state = 'completed'
               AND feedback_id IS NULL
               AND COALESCE(reply, '') <> ''
               AND reply_sent_at IS NOT NULL
               AND continued_at IS NULL
               AND updated_at >= ?
             ORDER BY id DESC
             LIMIT 1
            """,
            (str(qq), str(group_id), since_text),
        ).fetchone()
        if not previous:
            conn.rollback()
            return None
        combined = (
            f"之前描述：{previous['content']}\n"
            f"玩家补充：{content or '（附有图片，请结合图片内容判断）'}"
        )
        combined_media = []
        try:
            combined_media.extend(json.loads(previous["media_json"] or "[]"))
        except (json.JSONDecodeError, TypeError):
            pass
        combined_media.extend(media or [])
        combined_media = combined_media[-4:]
        selected_personality = normalize_personality(personality)
        cur = conn.execute(
            """
            INSERT INTO chat_messages
                (kind, qq, nickname, group_id, content, media_json,
                 personality, created_at, updated_at)
            VALUES ('bug_intake', ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                str(qq), nickname or "", str(group_id), combined,
                json.dumps(combined_media, ensure_ascii=False),
                selected_personality,
                now_text, now_text,
            ),
        )
        conn.execute(
            "UPDATE chat_messages SET continued_at = ? WHERE id = ?",
            (now_text, previous["id"]),
        )
        conn.commit()
        return cur.lastrowid


def has_pending_bug_followup(
    qq: str,
    group_id: str,
    max_age_seconds: int = 1800,
) -> bool:
    """玩家是否正在等待补充最近一条 Bug 描述。"""
    since_text = (
        datetime.now() - timedelta(seconds=max(60, max_age_seconds))
    ).isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        row = conn.execute(
            """
            SELECT 1 FROM chat_messages
             WHERE qq = ?
               AND group_id = ?
               AND kind = 'bug_intake'
               AND state = 'completed'
               AND feedback_id IS NULL
               AND COALESCE(reply, '') <> ''
               AND reply_sent_at IS NOT NULL
               AND continued_at IS NULL
               AND updated_at >= ?
             LIMIT 1
            """,
            (str(qq), str(group_id), since_text),
        ).fetchone()
        return row is not None


def claim_chat_job(
    worker_id: str,
    lease_seconds: int = 600,
    kinds: tuple[str, ...] = ("chat", "bug_intake"),
):
    """原子领取一条聊天请求，并回收超时租约。"""
    now = datetime.now()
    now_text = now.isoformat(timespec="seconds")
    stale_text = (now - timedelta(seconds=max(60, lease_seconds))).isoformat(
        timespec="seconds"
    )
    token = uuid4().hex
    allowed_kinds = tuple(
        kind for kind in kinds
        if kind in ("chat", "bug_intake", "admin_agent")
    )
    if not allowed_kinds:
        raise ValueError("聊天任务类型过滤器为空")
    placeholders = ",".join("?" for _ in allowed_kinds)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        conn.execute(
            f"""
            UPDATE chat_messages
               SET state = 'queued',
                   claim_token = NULL,
                   worker_id = NULL,
                   claimed_at = NULL,
                   updated_at = ?
             WHERE state = 'claimed'
               AND kind IN ({placeholders})
               AND claimed_at IS NOT NULL
               AND claimed_at < ?
            """,
            (now_text, *allowed_kinds, stale_text),
        )
        row = conn.execute(
            f"""
            SELECT * FROM chat_messages
             WHERE state = 'queued'
               AND kind IN ({placeholders})
             ORDER BY created_at, id
             LIMIT 1
            """,
            allowed_kinds,
        ).fetchone()
        if not row:
            conn.commit()
            return None
        cur = conn.execute(
            """
            UPDATE chat_messages
               SET state = 'claimed',
                   claim_token = ?,
                   worker_id = ?,
                   claimed_at = ?,
                   attempts = attempts + 1,
                   updated_at = ?
             WHERE id = ? AND state = 'queued'
            """,
            (token, worker_id, now_text, now_text, row["id"]),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return None
        claimed = conn.execute(
            "SELECT * FROM chat_messages WHERE id = ?", (row["id"],)
        ).fetchone()
        history = conn.execute(
            """
            SELECT nickname, content, reply
              FROM chat_messages
             WHERE group_id = ?
               AND kind = ?
               AND assistant_id = ?
               AND state = 'completed'
               AND id < ?
             ORDER BY id DESC
             LIMIT 6
            """,
            (
                str(row["group_id"]),
                str(row["kind"]),
                str(row["assistant_id"] or DEFAULT_ASSISTANT_ID),
                row["id"],
            ),
        ).fetchall()
        conn.commit()
        result = dict(claimed)
        try:
            result["media"] = json.loads(result.get("media_json") or "[]")
        except (json.JSONDecodeError, TypeError):
            result["media"] = []
        result["history"] = [dict(item) for item in reversed(history)]
        return result


def get_chat_message(chat_id: int):
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            "SELECT * FROM chat_messages WHERE id = ?", (chat_id,)
        ).fetchone()
        if not row:
            return None
        result = dict(row)
        try:
            result["media"] = json.loads(result.get("media_json") or "[]")
        except (json.JSONDecodeError, TypeError):
            result["media"] = []
        return result


def complete_chat_job(
    chat_id: int,
    claim_token: str,
    reply: str,
) -> bool:
    """写入聊天回复；仅持有有效租约的工作器可完成。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE chat_messages
               SET state = 'completed',
                   reply = ?,
                   error = NULL,
                   claim_token = NULL,
                   worker_id = NULL,
                   claimed_at = NULL,
                   updated_at = ?,
                   reply_sent_at = NULL
             WHERE id = ?
               AND kind IN ('chat', 'admin_agent')
               AND state = 'claimed'
               AND claim_token = ?
            """,
            (reply, now_text, chat_id, claim_token),
        )
        conn.commit()
        return cur.rowcount == 1


def complete_bug_intake_job(
    chat_id: int,
    claim_token: str,
    decision: str,
    cleaned_description: str,
    reply: str,
    _agent_enabled: bool,
):
    """完成 Bug 描述检查；合格时回复记录编号，但永不进入自动修复队列。"""
    if decision not in ("record", "clarify", "ignore"):
        raise ValueError("Bug 检查结论必须是 record、clarify 或 ignore")
    cleaned_description = str(cleaned_description or "").strip()
    reply = str(reply or "").strip()
    if decision == "record" and not cleaned_description:
        raise ValueError("合格 Bug 缺少清理后的问题描述")
    if decision == "clarify" and not reply:
        raise ValueError("需补充的 Bug 缺少追问内容")

    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM chat_messages
             WHERE id = ?
               AND kind = 'bug_intake'
               AND state = 'claimed'
               AND claim_token = ?
            """,
            (chat_id, claim_token),
        ).fetchone()
        if not row:
            conn.rollback()
            return None

        feedback_id = None
        if decision == "record":
            cur = conn.execute(
                """
                INSERT INTO feedback
                    (qq, nickname, group_id, content, created_at,
                     agent_state, agent_updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    str(row["qq"]), row["nickname"] or "",
                    str(row["group_id"]), cleaned_description, now_text,
                    "none", now_text,
                ),
            )
            feedback_id = cur.lastrowid

        outgoing_reply = reply if decision == "clarify" else ""
        if decision == "record":
            praise = {
                "hancock": "描述得很清楚，做得不错。",
                "nami": "描述得很清楚，帮大忙了。",
                "robin": "线索整理得很清楚，很可靠。",
            }[normalize_personality(row["personality"])]
            outgoing_reply = f"Bug #{feedback_id} 已记录。{praise}"

        conn.execute(
            """
            UPDATE chat_messages
               SET state = 'completed',
                   reply = ?,
                   error = NULL,
                   claim_token = NULL,
                   worker_id = NULL,
                   claimed_at = NULL,
                   updated_at = ?,
                   reply_sent_at = ?,
                   feedback_id = ?
             WHERE id = ?
            """,
            (
                outgoing_reply,
                now_text,
                None if outgoing_reply else now_text,
                feedback_id,
                chat_id,
            ),
        )
        conn.commit()
        return {
            "decision": decision,
            "feedback_id": feedback_id,
            "content": cleaned_description,
            "qq": str(row["qq"]),
            "nickname": row["nickname"] or "",
            "group_id": str(row["group_id"]),
        }


def release_chat_job(
    chat_id: int,
    claim_token: str,
    error: str,
    max_attempts: int = 3,
) -> bool:
    """瞬时故障时重新排队；超过尝试次数后向群里返回失败。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE chat_messages
               SET state = CASE WHEN attempts >= ? THEN 'failed' ELSE 'queued' END,
                   error = ?,
                   claim_token = NULL,
                   worker_id = NULL,
                   claimed_at = NULL,
                   updated_at = ?,
                   reply_sent_at = NULL
             WHERE id = ?
               AND state = 'claimed'
               AND claim_token = ?
            """,
            (max(1, max_attempts), error, now_text, chat_id, claim_token),
        )
        conn.commit()
        return cur.rowcount == 1


def reject_unauthorized_admin_job(
    chat_id: int,
    claim_token: str,
    error: str,
) -> bool:
    """隔离来源凭据无效的管理员行，且不向该行声明的 QQ 发送回执。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE chat_messages
               SET state = 'failed',
                   error = ?,
                   claim_token = NULL,
                   worker_id = NULL,
                   claimed_at = NULL,
                   updated_at = ?,
                   reply_sent_at = ?
             WHERE id = ?
               AND kind = 'admin_agent'
               AND state = 'claimed'
               AND claim_token = ?
            """,
            (str(error or "来源校验失败")[:1000], now_text, now_text, chat_id, claim_token),
        )
        conn.commit()
        return cur.rowcount == 1


def get_chat_result_to_send(
    assistant_id: str | None = None,
    kinds: tuple[str, ...] | None = None,
):
    selected_assistant = None
    if assistant_id is not None:
        selected_assistant = str(assistant_id or "").strip().lower()
        if not _ASSISTANT_ID_RE.fullmatch(selected_assistant):
            raise ValueError(f"不支持的助理标识: {assistant_id}")
    selected_kinds = None
    if kinds is not None:
        selected_kinds = tuple(
            kind for kind in kinds
            if kind in ("chat", "bug_intake", "admin_agent")
        )
        if not selected_kinds or len(selected_kinds) != len(kinds):
            raise ValueError("聊天结果类型过滤器无效")
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        query = """
            SELECT * FROM chat_messages
             WHERE state IN ('completed', 'failed')
               AND reply_sent_at IS NULL
        """
        params = ()
        if selected_assistant is not None:
            query += " AND assistant_id = ?"
            params = (selected_assistant,)
        if selected_kinds is not None:
            placeholders = ",".join("?" for _ in selected_kinds)
            query += f" AND kind IN ({placeholders})"
            params += selected_kinds
        query += """
              ORDER BY updated_at, id
              LIMIT 1
        """
        row = conn.execute(query, params).fetchone()
        return dict(row) if row else None


def mark_chat_result_sent(chat_id: int) -> bool:
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE chat_messages
               SET reply_sent_at = ?, updated_at = ?
             WHERE id = ?
               AND state IN ('completed', 'failed')
               AND reply_sent_at IS NULL
            """,
            (now_text, now_text, chat_id),
        )
        conn.commit()
        return cur.rowcount == 1


def add_feedback(
    qq: str,
    nickname: str,
    group_id: str,
    content: str,
    agent_state: str = "none",
) -> int:
    """插入一条反馈,返回自增主键 id(即群里回执显示的编号)。"""
    created_at = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            "INSERT INTO feedback "
            "(qq, nickname, group_id, content, created_at, agent_state, agent_updated_at) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            (
                str(qq), nickname or "", str(group_id), content, created_at,
                agent_state, created_at,
            ),
        )
        conn.commit()
        return cur.lastrowid


def get_feedback(feedback_id: int):
    """读取单条反馈，供 Agent 桥接与通知流程使用。"""
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            "SELECT * FROM feedback WHERE id = ?", (feedback_id,)
        ).fetchone()
        return dict(row) if row else None


def claim_agent_job(worker_id: str, lease_seconds: int = 3600):
    """原子领取一条待分析任务，并回收超时租约。"""
    now = datetime.now()
    now_text = now.isoformat(timespec="seconds")
    stale_text = (now - timedelta(seconds=max(60, lease_seconds))).isoformat(
        timespec="seconds"
    )
    token = uuid4().hex
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        conn.execute(
            """
            UPDATE feedback
               SET agent_state = CASE
                       WHEN COALESCE(agent_answer, '') = '' THEN 'queued'
                       ELSE 'owner_answered'
                   END,
                   agent_claim_token = NULL,
                   agent_worker_id = NULL,
                   agent_claimed_at = NULL,
                   agent_updated_at = ?
             WHERE agent_state = 'claimed'
               AND agent_claimed_at IS NOT NULL
               AND agent_claimed_at < ?
            """,
            (now_text, stale_text),
        )
        row = conn.execute(
            """
            SELECT * FROM feedback
             WHERE agent_state IN ('queued', 'owner_answered')
             ORDER BY created_at, id
             LIMIT 1
            """
        ).fetchone()
        if not row:
            conn.commit()
            return None
        cur = conn.execute(
            """
            UPDATE feedback
               SET agent_state = 'claimed',
                   agent_claim_token = ?,
                   agent_worker_id = ?,
                   agent_claimed_at = ?,
                   agent_attempts = agent_attempts + 1,
                   agent_updated_at = ?
             WHERE id = ?
               AND agent_state IN ('queued', 'owner_answered')
            """,
            (token, worker_id, now_text, now_text, row["id"]),
        )
        if cur.rowcount != 1:
            conn.rollback()
            return None
        claimed = conn.execute(
            "SELECT * FROM feedback WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return dict(claimed)


def request_owner_question(
    feedback_id: int,
    claim_token: str,
    question: str,
    summary: str = "",
) -> bool:
    """把已领取任务转为等待管理员确认。问题由机器人串行发送。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE feedback
               SET agent_state = 'waiting_owner',
                   agent_question = ?,
                   agent_question_sent_at = NULL,
                   agent_answer = NULL,
                   agent_summary = ?,
                   agent_claim_token = NULL,
                   agent_worker_id = NULL,
                   agent_claimed_at = NULL,
                   agent_updated_at = ?
             WHERE id = ?
               AND agent_state = 'claimed'
               AND agent_claim_token = ?
            """,
            (question, summary, now_text, feedback_id, claim_token),
        )
        conn.commit()
        return cur.rowcount == 1


def release_agent_job(feedback_id: int, claim_token: str, summary: str = "") -> bool:
    """网络等瞬时故障时释放租约，保留管理员已给出的回答。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE feedback
               SET agent_state = CASE
                       WHEN COALESCE(agent_answer, '') = '' THEN 'queued'
                       ELSE 'owner_answered'
                   END,
                   agent_summary = ?,
                   agent_claim_token = NULL,
                   agent_worker_id = NULL,
                   agent_claimed_at = NULL,
                   agent_updated_at = ?
             WHERE id = ?
               AND agent_state = 'claimed'
               AND agent_claim_token = ?
            """,
            (summary, now_text, feedback_id, claim_token),
        )
        conn.commit()
        return cur.rowcount == 1


def get_owner_question_to_send():
    """全局一次只返回一个管理员问题，保证无编号的 #回复 不会串单。"""
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        active = conn.execute(
            """
            SELECT 1 FROM feedback
             WHERE agent_state = 'waiting_owner'
               AND agent_question_sent_at IS NOT NULL
               AND COALESCE(agent_answer, '') = ''
             LIMIT 1
            """
        ).fetchone()
        if active:
            return None
        row = conn.execute(
            """
            SELECT * FROM feedback
             WHERE agent_state = 'waiting_owner'
               AND agent_question_sent_at IS NULL
             ORDER BY created_at, id
             LIMIT 1
            """
        ).fetchone()
        return dict(row) if row else None


def mark_owner_question_sent(feedback_id: int) -> bool:
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE feedback
               SET agent_question_sent_at = ?, agent_updated_at = ?
             WHERE id = ?
               AND agent_state = 'waiting_owner'
               AND agent_question_sent_at IS NULL
            """,
            (now_text, now_text, feedback_id),
        )
        conn.commit()
        return cur.rowcount == 1


def answer_active_owner_question(group_id: str, answer: str):
    """回答当前群内唯一已发出的管理员问题，返回被回答的反馈。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        row = conn.execute(
            """
            SELECT * FROM feedback
             WHERE agent_state = 'waiting_owner'
               AND agent_question_sent_at IS NOT NULL
               AND COALESCE(agent_answer, '') = ''
             ORDER BY agent_question_sent_at, id
             LIMIT 1
            """
        ).fetchone()
        if not row or str(row["group_id"]) != str(group_id):
            conn.rollback()
            return None
        conn.execute(
            """
            UPDATE feedback
               SET agent_state = 'owner_answered',
                   agent_answer = ?,
                   agent_updated_at = ?
             WHERE id = ?
            """,
            (answer, now_text, row["id"]),
        )
        updated = conn.execute(
            "SELECT * FROM feedback WHERE id = ?", (row["id"],)
        ).fetchone()
        conn.commit()
        return dict(updated)


def complete_agent_job(
    feedback_id: int,
    claim_token: str,
    state: str,
    summary: str,
    commit: str = "",
    result_url: str = "",
) -> bool:
    """完成 Agent 任务；仅测试服验证成功的任务可写 fixed。"""
    if state not in AGENT_TERMINAL_STATES:
        raise ValueError(f"未知 Agent 终态: {state}")
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE feedback
               SET agent_state = ?,
                   agent_summary = ?,
                   agent_commit = ?,
                   agent_result_url = ?,
                   agent_claim_token = NULL,
                   agent_worker_id = NULL,
                   agent_claimed_at = NULL,
                   agent_updated_at = ?,
                   agent_reply_sent_at = NULL,
                   status = CASE
                       WHEN ? = 'fixed' THEN 'fixed'
                       WHEN ? = 'rejected' THEN 'wontfix'
                       ELSE status
                   END,
                   fix_note = CASE
                       WHEN ? IN ('fixed', 'rejected') THEN ?
                       ELSE fix_note
                   END
             WHERE id = ?
               AND agent_state = 'claimed'
               AND agent_claim_token = ?
            """,
            (
                state, summary, commit, result_url, now_text,
                state, state, state, summary, feedback_id, claim_token,
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def get_agent_result_to_send():
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            """
            SELECT * FROM feedback
             WHERE agent_state IN ('fixed', 'rejected', 'manual', 'failed')
               AND agent_reply_sent_at IS NULL
             ORDER BY agent_updated_at, id
             LIMIT 1
            """
        ).fetchone()
        return dict(row) if row else None


def mark_agent_result_sent(feedback_id: int) -> bool:
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE feedback
               SET agent_reply_sent_at = ?, agent_updated_at = ?
             WHERE id = ?
               AND agent_state IN ('fixed', 'rejected', 'manual', 'failed')
               AND agent_reply_sent_at IS NULL
            """,
            (now_text, now_text, feedback_id),
        )
        conn.commit()
        return cur.rowcount == 1


def set_issue_no(feedback_id: int, issue_no: int) -> None:
    """回填 GitHub Issue 编号。"""
    with sqlite3.connect(DB_PATH) as conn:
        conn.execute(
            "UPDATE feedback SET issue_no = ? WHERE id = ?",
            (issue_no, feedback_id),
        )
        conn.commit()


VALID_STATUS = ("open", "fixed", "wontfix")


def set_status(feedback_id: int, status: str, note: str | None = None) -> bool:
    """更新某条反馈的修复状态(及备注)。

    status: open(待修) / fixed(已修) / wontfix(非bug)。
    note 为 None 时保留原备注;传字符串则覆盖。
    返回是否命中了该 id。
    """
    status = status.lower()
    if status not in VALID_STATUS:
        raise ValueError(f"未知状态: {status}(应为 {'/'.join(VALID_STATUS)})")
    with sqlite3.connect(DB_PATH) as conn:
        if note is None:
            cur = conn.execute(
                "UPDATE feedback SET status = ? WHERE id = ?", (status, feedback_id)
            )
        else:
            cur = conn.execute(
                "UPDATE feedback SET status = ?, fix_note = ? WHERE id = ?",
                (status, note, feedback_id),
            )
        # 主条目状态变更时,带动挂在它名下的重复子条一起变(一次修复,全部闭环)
        conn.execute(
            "UPDATE feedback SET status = ? WHERE dup_of = ?", (status, feedback_id)
        )
        conn.commit()
        return cur.rowcount > 0


def set_dup(feedback_id: int, main_id: int) -> bool:
    """把 feedback_id 标记为 main_id(主条目)的重复,写入 dup_of。

    main_id 传 0 或等于自身 -> 取消重复标记(置空)。
    会顺着主条目自身的 dup_of 摸到最终根,避免链式指向;
    并拒绝自指与成环。返回是否命中了 feedback_id。
    """
    with sqlite3.connect(DB_PATH) as conn:
        if main_id in (0, feedback_id):
            cur = conn.execute(
                "UPDATE feedback SET dup_of = NULL WHERE id = ?", (feedback_id,)
            )
            conn.commit()
            return cur.rowcount > 0
        if conn.execute(
            "SELECT 1 FROM feedback WHERE id = ?", (main_id,)
        ).fetchone() is None:
            raise ValueError(f"主条目 #{main_id} 不存在")
        # 顺藤摸到根:若 main 本身是别人的重复,实际主条目取其根
        root, seen = main_id, {feedback_id}
        for _ in range(50):
            r = conn.execute(
                "SELECT dup_of FROM feedback WHERE id = ?", (root,)
            ).fetchone()
            if not r or r[0] is None:
                break
            if r[0] in seen:
                raise ValueError("检测到循环重复指向,已拒绝")
            seen.add(root)
            root = r[0]
        if root == feedback_id:
            raise ValueError("不能把条目标记为自身下游的重复")
        cur = conn.execute(
            "UPDATE feedback SET dup_of = ? WHERE id = ?", (root, feedback_id)
        )
        conn.commit()
        return cur.rowcount > 0
