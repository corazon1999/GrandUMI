# -*- coding: utf-8 -*-
"""本地 SQLite 存储:把每一条群内 bug 反馈持久化到 feedback.db。

设计原则:写本地一定成功(不依赖网络),GitHub Issue 编号建好后再回填。
"""

import os
import sqlite3
from datetime import datetime

# 数据库文件固定放在本模块同目录下的 feedback.db
DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "feedback.db")


def init_db() -> None:
    """初始化数据库与表结构(幂等,可重复调用)。"""
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
                fix_note   TEXT                -- 修复备注(报告里展示)
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
        conn.commit()


def add_feedback(qq: str, nickname: str, group_id: str, content: str) -> int:
    """插入一条反馈,返回自增主键 id(即群里回执显示的编号)。"""
    created_at = datetime.now().isoformat(timespec="seconds")
    with sqlite3.connect(DB_PATH) as conn:
        cur = conn.execute(
            "INSERT INTO feedback (qq, nickname, group_id, content, created_at) "
            "VALUES (?, ?, ?, ?, ?)",
            (str(qq), nickname or "", str(group_id), content, created_at),
        )
        conn.commit()
        return cur.lastrowid


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
