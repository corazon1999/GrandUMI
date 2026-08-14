# -*- coding: utf-8 -*-
"""本地 SQLite 存储:把每一条群内 bug 反馈持久化到 feedback.db。

设计原则:写本地一定成功(不依赖网络),GitHub Issue 编号建好后再回填。
"""

import os
import sqlite3
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
                reply_sent_at TEXT
            )
            """
        )
        conn.execute(
            """
            CREATE INDEX IF NOT EXISTS idx_chat_messages_queue
            ON chat_messages(state, created_at, id)
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
        conn.commit()


def add_chat_message(
    qq: str,
    nickname: str,
    group_id: str,
    content: str,
) -> int:
    """新增一条群聊 Agent 请求并返回编号。"""
    now_text = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            """
            INSERT INTO chat_messages
                (qq, nickname, group_id, content, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                str(qq), nickname or "", str(group_id), content,
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


def claim_chat_job(worker_id: str, lease_seconds: int = 600):
    """原子领取一条聊天请求，并回收超时租约。"""
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
            UPDATE chat_messages
               SET state = 'queued',
                   claim_token = NULL,
                   worker_id = NULL,
                   claimed_at = NULL,
                   updated_at = ?
             WHERE state = 'claimed'
               AND claimed_at IS NOT NULL
               AND claimed_at < ?
            """,
            (now_text, stale_text),
        )
        row = conn.execute(
            """
            SELECT * FROM chat_messages
             WHERE state = 'queued'
             ORDER BY created_at, id
             LIMIT 1
            """
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
               AND state = 'completed'
               AND id < ?
             ORDER BY id DESC
             LIMIT 6
            """,
            (str(row["group_id"]), row["id"]),
        ).fetchall()
        conn.commit()
        result = dict(claimed)
        result["history"] = [dict(item) for item in reversed(history)]
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
               AND state = 'claimed'
               AND claim_token = ?
            """,
            (reply, now_text, chat_id, claim_token),
        )
        conn.commit()
        return cur.rowcount == 1


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
