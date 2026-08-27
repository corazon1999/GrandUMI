# -*- coding: utf-8 -*-
"""本地 SQLite 存储:把每一条群内 bug 反馈持久化到 feedback.db。

设计原则:写本地一定成功(不依赖网络),GitHub Issue 编号建好后再回填。
"""

import os
import json
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
PERSONALITIES = ("hancock", "nami", "robin")
DEFAULT_PERSONALITY = "hancock"
MEMBER_VERIFICATION_ACTIVE_STATES = (
    "awaiting_prompt",
    "pending",
    "checking_inviter",
    "checking_timeout",
    "kicking",
)
MEMBER_VERIFICATION_TERMINAL_STATES = ("verified", "kicked", "left", "cancelled")


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
                personality TEXT NOT NULL DEFAULT 'hancock'
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
            CREATE INDEX IF NOT EXISTS idx_chat_messages_queue
            ON chat_messages(state, created_at, id)
            """
        )
        conn.execute(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS idx_member_verifications_active
            ON member_verifications(group_id, newcomer_qq)
            WHERE state IN (
                'awaiting_prompt', 'pending', 'checking_inviter',
                'checking_timeout', 'kicking'
            )
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_member_verifications_jobs
            ON member_verifications(state, deadline_at, claimed_at, id)
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
        verification_cols = {
            row[1] for row in conn.execute("PRAGMA table_info(member_verifications)")
        }
        if "next_attempt_at" not in verification_cols:
            conn.execute(
                "ALTER TABLE member_verifications ADD COLUMN next_attempt_at INTEGER"
            )
        conn.commit()


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
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'checking_timeout', 'kicking'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (str(group_id), str(newcomer_qq)),
        ).fetchone()
        return _verification_dict(row)


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


def start_member_verification(
    group_id: str,
    newcomer_qq: str,
    nickname: str,
    join_event_time: int,
    now=None,
):
    """幂等创建入群验证；重复通知不会重置当前验证窗口。"""
    group_id = str(group_id)
    newcomer_qq = str(newcomer_qq)
    join_event_time = _verification_now(join_event_time)
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
        active = conn.execute(
            """
            SELECT * FROM member_verifications
             WHERE group_id = ?
               AND newcomer_qq = ?
               AND state IN (
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'checking_timeout', 'kicking'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (group_id, newcomer_qq),
        ).fetchone()
        if active:
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
        result["reason"] = "created"
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
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'checking_timeout', 'kicking'
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
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'checking_timeout', 'kicking'
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
    timeout_seconds: int,
    sent_at=None,
) -> bool:
    """只在 OneBot 确认提示发送成功后启动倒计时。"""
    sent_value = _verification_now(sent_at)
    timeout_value = max(1, int(timeout_seconds))
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'pending',
                   prompt_sent_at = ?,
                   deadline_at = ?,
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
                sent_value + timeout_value,
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
    """原子接收一条回答，并使超时任务失效；返回 status 与已领取会话。"""
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
                   'awaiting_prompt', 'pending', 'checking_inviter',
                   'checking_timeout', 'kicking'
               )
             ORDER BY id DESC
             LIMIT 1
            """,
            (str(group_id), str(newcomer_qq)),
        ).fetchone()
        if not row:
            conn.rollback()
            return {"status": "no_session"}
        if row["state"] == "kicking":
            conn.rollback()
            return {"status": "expired", "verification": dict(row)}
        if row["state"] == "checking_inviter":
            conn.rollback()
            return {"status": "busy", "verification": dict(row)}
        deadline = row["deadline_at"]
        if deadline is not None and now_value > int(deadline):
            conn.rollback()
            return {"status": "expired", "verification": dict(row)}
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
               AND state IN ('awaiting_prompt', 'pending', 'checking_timeout')
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
    """权威成员接口失败时保留玩家答案，不通过也不进入超时踢人。"""
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
        result["can_retry"] = (
            result["deadline_at"] is None
            or now_value <= int(result["deadline_at"])
        )
        return result


def claim_due_member_verification_timeout(
    now=None, lease_seconds: int = 30, group_ids=None
):
    """领取到期会话；也恢复中断的最终检查/踢人动作。"""
    now_value = _verification_now(now)
    stale = now_value - max(5, int(lease_seconds))
    token = uuid4().hex
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("BEGIN IMMEDIATE")
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
        args.extend((now_value, now_value, stale))
        row = conn.execute(
            f"""
            SELECT * FROM member_verifications
             WHERE {group_clause}((
                    state = 'pending'
                AND deadline_at IS NOT NULL
                AND deadline_at <= ?
                AND COALESCE(next_attempt_at, 0) <= ?
             ) OR (
                    state IN ('checking_timeout', 'kicking')
                AND (claimed_at IS NULL OR claimed_at <= ?)
             ))
             ORDER BY COALESCE(deadline_at, updated_at), id
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
               SET state = 'checking_timeout',
                   claim_token = ?,
                   claim_kind = 'timeout',
                   claimed_at = ?,
                   next_attempt_at = NULL,
                   updated_at = ?
             WHERE id = ?
               AND (
                    (state = 'pending' AND deadline_at IS NOT NULL
                     AND deadline_at <= ? AND COALESCE(next_attempt_at, 0) <= ?)
                 OR (state IN ('checking_timeout', 'kicking')
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


def authorize_member_verification_kick(
    verification_id: int, claim_token: str, now=None
) -> bool:
    """踢人前最后一次原子核验会话仍处于同一个超时租约。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'kicking',
                   claim_kind = 'kick',
                   claimed_at = ?,
                   next_attempt_at = NULL,
                   kick_attempts = kick_attempts + 1,
                   kick_requested_at = COALESCE(kick_requested_at, ?),
                   updated_at = ?
             WHERE id = ?
               AND state = 'checking_timeout'
               AND claim_kind = 'timeout'
               AND claim_token = ?
            """,
            (
                now_value,
                now_value,
                now_value,
                int(verification_id),
                str(claim_token),
            ),
        )
        conn.commit()
        return cur.rowcount == 1


def release_member_verification_timeout(
    verification_id: int,
    claim_token: str,
    error: str,
    now=None,
    retry_delay_seconds: int = 5,
) -> bool:
    """最终成员查询或踢人失败时回到到期队列，不把失败当成功。"""
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = CASE
                       WHEN prompt_sent_at IS NULL THEN 'awaiting_prompt'
                       ELSE 'pending'
                   END,
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   next_attempt_at = ?,
                   last_error = ?,
                   updated_at = ?
             WHERE id = ?
               AND state IN ('checking_timeout', 'kicking')
               AND claim_token = ?
               AND claim_kind IN ('timeout', 'kick')
            """,
            (
                now_value + max(1, int(retry_delay_seconds)),
                str(error or "超时处理失败")[:1000],
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
    """权威成员列表确认新人已不在群时安全结束，不再调用踢人。"""
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
               AND state IN ('checking_inviter', 'checking_timeout', 'kicking')
               AND claim_token = ?
            """,
            (now_value, now_value, int(verification_id), str(claim_token)),
        )
        conn.commit()
        return cur.rowcount == 1


def complete_member_verification_kick(
    verification_id: int, claim_token: str, now=None
) -> bool:
    now_value = _verification_now(now)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            UPDATE member_verifications
               SET state = 'kicked',
                   claim_token = NULL,
                   claim_kind = NULL,
                   claimed_at = NULL,
                   last_error = NULL,
                   kicked_at = ?,
                   ended_at = ?,
                   updated_at = ?
             WHERE id = ?
               AND state = 'kicking'
               AND claim_kind = 'kick'
               AND claim_token = ?
            """,
            (
                now_value,
                now_value,
                now_value,
                int(verification_id),
                str(claim_token),
            ),
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
    selected = normalize_personality(personality)
    if selected != str(personality or "").strip().lower():
        raise ValueError(f"不支持的性格: {personality}")
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
) -> int:
    """新增一条聊天或 Bug 描述检查请求并返回编号。"""
    if kind not in ("chat", "bug_intake", "admin_agent"):
        raise ValueError(f"不支持的消息类型: {kind}")
    now_text = datetime.now().isoformat(timespec="seconds")
    media_json = json.dumps(media or [], ensure_ascii=False)
    selected_personality = normalize_personality(personality)
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            INSERT INTO chat_messages
                (kind, qq, nickname, group_id, content, media_json,
                 personality, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                kind, str(qq), nickname or "", str(group_id), content,
                media_json, selected_personality,
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
               AND state = 'completed'
               AND id < ?
             ORDER BY id DESC
             LIMIT 6
            """,
            (str(row["group_id"]), str(row["kind"]), row["id"]),
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


def get_chat_result_to_send():
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        row = conn.execute(
            """
            SELECT * FROM chat_messages
             WHERE state IN ('completed', 'failed')
               AND reply_sent_at IS NULL
             ORDER BY updated_at, id
             LIMIT 1
            """
        ).fetchone()
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
