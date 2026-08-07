# -*- coding: utf-8 -*-

import os
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import storage


class AgentStorageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.old_path = storage.DB_PATH
        storage.DB_PATH = os.path.join(self.temp.name, "feedback.db")
        storage.init_db()

    def tearDown(self):
        storage.DB_PATH = self.old_path
        self.temp.cleanup()

    def test完整状态机与幂等回执(self):
        feedback_id = storage.add_feedback(
            "123", "玩家", "456", "卡牌效果没有发动", agent_state="queued"
        )
        job = storage.claim_agent_job("worker-a", 3600)
        self.assertEqual(feedback_id, job["id"])
        token = job["agent_claim_token"]

        self.assertTrue(
            storage.request_owner_question(
                feedback_id, token, "正确行为应当是什么？", "规则存在歧义"
            )
        )
        question = storage.get_owner_question_to_send()
        self.assertEqual(feedback_id, question["id"])
        self.assertTrue(storage.mark_owner_question_sent(feedback_id))
        self.assertIsNone(storage.answer_active_owner_question("999", "错误群回复"))

        answered = storage.answer_active_owner_question("456", "这是 Bug，应当发动")
        self.assertEqual(feedback_id, answered["id"])
        second_job = storage.claim_agent_job("worker-a", 3600)
        self.assertEqual("这是 Bug，应当发动", second_job["agent_answer"])
        self.assertTrue(
            storage.complete_agent_job(
                feedback_id,
                second_job["agent_claim_token"],
                "fixed",
                "已修复卡牌效果",
                "abcdef1",
                "https://test.grand-umi.com/",
            )
        )
        result = storage.get_agent_result_to_send()
        self.assertEqual("fixed", result["agent_state"])
        self.assertEqual("fixed", result["status"])
        self.assertTrue(storage.mark_agent_result_sent(feedback_id))
        self.assertFalse(storage.mark_agent_result_sent(feedback_id))
        self.assertIsNone(storage.get_agent_result_to_send())

    def test管理员问题全局串行(self):
        first = storage.add_feedback("1", "甲", "10", "问题一", "queued")
        second = storage.add_feedback("2", "乙", "20", "问题二", "queued")
        job1 = storage.claim_agent_job("w")
        storage.request_owner_question(first, job1["agent_claim_token"], "确认一")
        job2 = storage.claim_agent_job("w")
        storage.request_owner_question(second, job2["agent_claim_token"], "确认二")

        self.assertEqual(first, storage.get_owner_question_to_send()["id"])
        storage.mark_owner_question_sent(first)
        self.assertIsNone(storage.get_owner_question_to_send())
        storage.answer_active_owner_question("10", "回答一")
        self.assertEqual(second, storage.get_owner_question_to_send()["id"])

    def test瞬时故障释放租约后可重试(self):
        feedback_id = storage.add_feedback("1", "甲", "10", "连接故障", "queued")
        job = storage.claim_agent_job("w")
        self.assertTrue(
            storage.release_agent_job(
                feedback_id, job["agent_claim_token"], "模型暂时不可用"
            )
        )
        retried = storage.claim_agent_job("w")
        self.assertEqual(feedback_id, retried["id"])
        self.assertEqual(2, retried["agent_attempts"])

    def test老数据库可幂等迁移(self):
        old_db = os.path.join(self.temp.name, "old.db")
        with sqlite3.connect(old_db) as conn:
            conn.execute(
                """
                CREATE TABLE feedback (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    qq TEXT NOT NULL,
                    nickname TEXT,
                    group_id TEXT NOT NULL,
                    content TEXT NOT NULL,
                    issue_no INTEGER,
                    created_at TEXT NOT NULL
                )
                """
            )
        storage.DB_PATH = old_db
        storage.init_db()
        storage.init_db()
        with sqlite3.connect(old_db) as conn:
            cols = {row[1] for row in conn.execute("PRAGMA table_info(feedback)")}
        self.assertIn("agent_state", cols)
        self.assertIn("agent_reply_sent_at", cols)
        self.assertIn("status", cols)


if __name__ == "__main__":
    unittest.main()
