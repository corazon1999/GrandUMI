# -*- coding: utf-8 -*-

import asyncio
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

BOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BOT_DIR))

import bot
import storage


class FakeWebSocket:
    def __init__(self):
        self.sent = []

    async def send(self, payload):
        self.sent.append(json.loads(payload))


class BotAgentReplyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.old_path = storage.DB_PATH
        storage.DB_PATH = os.path.join(self.temp.name, "feedback.db")
        storage.init_db()
        feedback_id = storage.add_feedback(
            "100", "玩家", "456", "效果错误", agent_state="queued"
        )
        job = storage.claim_agent_job("worker")
        storage.request_owner_question(
            feedback_id, job["agent_claim_token"], "请确认正确效果"
        )
        storage.mark_owner_question_sent(feedback_id)

    def tearDown(self):
        storage.DB_PATH = self.old_path
        self.temp.cleanup()

    @staticmethod
    def event(user_id="651846226", include_at=True, text="#回复 这是 Bug"):
        message = []
        if include_at:
            message.append({"type": "at", "data": {"qq": "9999"}})
        message.append({"type": "text", "data": {"text": text}})
        return {
            "post_type": "message",
            "message_type": "group",
            "group_id": 456,
            "user_id": int(user_id),
            "self_id": 9999,
            "message": message,
            "raw_message": "[CQ:at,qq=9999] " + text,
        }

    def test只接受指定管理员且必须at机器人(self):
        ws = FakeWebSocket()
        cfg = {"agent_owner_qq": 651846226}
        handled = asyncio.run(bot.handle_owner_reply(ws, cfg, self.event()))
        self.assertTrue(handled)
        row = storage.get_feedback(1)
        self.assertEqual("owner_answered", row["agent_state"])
        self.assertEqual("这是 Bug", row["agent_answer"])
        message = ws.sent[0]["params"]["message"]
        self.assertEqual("at", message[0]["type"])
        self.assertEqual("651846226", message[0]["data"]["qq"])

    def test冒充或未at会被忽略(self):
        cfg = {"agent_owner_qq": 651846226}
        self.assertFalse(
            asyncio.run(
                bot.handle_owner_reply(
                    FakeWebSocket(), cfg, self.event(user_id="123456")
                )
            )
        )
        self.assertFalse(
            asyncio.run(
                bot.handle_owner_reply(
                    FakeWebSocket(), cfg, self.event(include_at=False)
                )
            )
        )
        self.assertEqual("waiting_owner", storage.get_feedback(1)["agent_state"])

    def test纯文本抽取不会保留cq码(self):
        event = self.event()
        event["message"] = "[CQ:at,qq=9999] #回复 判断"
        self.assertEqual(" #回复 判断", bot.extract_plain_text(event))


if __name__ == "__main__":
    unittest.main()
