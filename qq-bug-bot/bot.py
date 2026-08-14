# -*- coding: utf-8 -*-
"""GrandUMI QQ 群 bug 反馈机器人(OneBot 11 / NapCat 正向 WebSocket)。

工作流程:
  1. 主动连接 NapCat 的正向 WS 服务端(同一条连接收事件 + 发动作)。
  2. 监听群消息,识别以 command_prefix(默认 "#bug ")开头的消息。
  3. 写入本地 SQLite -> 尝试建 GitHub Issue -> 在群里 @ 上报人回执。

运行: py bot.py
依赖: websockets(见 requirements.txt);GitHub 走本机已登录的 gh CLI。
"""

import asyncio
import contextlib
import json
import os
import re
import sys

import websockets
# 注意:websockets 16.0 的新版 asyncio 实现(默认的 websockets.connect)与 NapCat
# 的 WebSocket 握手不兼容(会被 NapCat 直接关连接,报 InvalidMessage)。
# 实测老版 legacy 实现可正常连接,故这里显式使用 legacy 客户端。
from websockets.legacy.client import connect as ws_connect

import storage

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.environ.get(
    "BUG_BOT_CONFIG_PATH", os.path.join(BASE_DIR, "config.json")
)


def load_config() -> dict:
    if not os.path.exists(CONFIG_PATH):
        sys.exit(
            f"找不到配置文件 {CONFIG_PATH}\n"
            f"请先复制 config.example.json 为 config.json 并填好 ws_url 等。"
        )
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def build_ws_url(cfg: dict) -> str:
    """如配置了 access_token,则以查询参数附加(OneBot 通用、跨版本最稳)。"""
    url = cfg["ws_url"]
    token = cfg.get("access_token") or ""
    if token:
        sep = "&" if "?" in url else "?"
        url = f"{url}{sep}access_token={token}"
    return url


def extract_plain_text(event: dict) -> str:
    """从 OneBot 事件里取纯文本，并丢弃 @、图片等 CQ 片段。"""
    msg = event.get("message")
    if isinstance(msg, list):
        return "".join(
            seg.get("data", {}).get("text", "")
            for seg in msg
            if isinstance(seg, dict) and seg.get("type") == "text"
        )
    if isinstance(msg, str) and msg:
        return re.sub(r"\[CQ:[^\]]+\]", "", msg)
    raw = event.get("raw_message")
    if isinstance(raw, str) and raw:
        return re.sub(r"\[CQ:[^\]]+\]", "", raw)
    return ""


# 反馈触发:群消息里只要出现 bug（忽略大小写）即进入描述检查。
_BUG_RE = re.compile(r"bug", re.IGNORECASE)
_LEADING_BUG_RE = re.compile(r"^\s*#bug(?:反馈)?[\s:：]*", re.IGNORECASE)
_CHAT_TRIGGER_RE = re.compile(r"^\s*#聊天(?:\s+|[:：])?(.*)$", re.DOTALL)


def match_feedback(text: str):
    """识别任意含 bug 的群消息，开头为 #bug 时剥掉指令前缀。"""
    if not text or not _BUG_RE.search(text):
        return None
    return _LEADING_BUG_RE.sub("", text, count=1).strip()


def match_chat(text: str):
    """识别“#聊天 内容”，命中时返回剥离前缀后的正文。"""
    if not text:
        return None
    match = _CHAT_TRIGGER_RE.match(text)
    return match.group(1).strip() if match else None


def at_message(qq: str, text: str) -> list[dict]:
    """使用 OneBot 消息段构造 @，避免把外部文本当作 CQ 码解析。"""
    return [
        {"type": "at", "data": {"qq": str(qq)}},
        {"type": "text", "data": {"text": " " + text}},
    ]


async def send_group_msg(ws, group_id, message) -> None:
    """发送群消息(动作经同一条正向 WS 下发)。"""
    payload = {
        "action": "send_group_msg",
        "params": {"group_id": int(group_id), "message": message},
    }
    await ws.send(json.dumps(payload, ensure_ascii=False))


async def handle_feedback(ws, cfg, event, content) -> None:
    """把含 bug 的消息送入描述检查队列；此处不回确认话术。"""
    group_id = event.get("group_id")
    qq = str(event.get("user_id", ""))
    sender = event.get("sender") or {}
    nickname = sender.get("card") or sender.get("nickname") or ""

    intake_id = storage.add_chat_message(
        qq,
        nickname,
        str(group_id),
        content or "（只提到了 bug，没有描述具体现象）",
        kind="bug_intake",
    )
    print(f"[Bug检查#{intake_id}] 群{group_id} {nickname}({qq}): {content}")


async def handle_chat(ws, cfg, event, content: str) -> None:
    """把群聊请求写入独立只读 Agent 队列。"""
    group_id = event.get("group_id")
    qq = str(event.get("user_id", ""))
    sender = event.get("sender") or {}
    nickname = sender.get("card") or sender.get("nickname") or "玩家"

    if not cfg.get("chat_agent_enabled", False):
        await send_group_msg(
            ws, group_id, at_message(qq, "妾身现在没空，稍后再来觐见吧。")
        )
        return
    content = match_chat(content) if match_chat(content) is not None else content
    if not content:
        content = "（玩家只@了你，没有附加文字）"
    max_length = max(20, int(cfg.get("chat_max_content_length", 500)))
    if len(content) > max_length:
        await send_group_msg(
            ws,
            group_id,
            at_message(qq, f"一次最多聊 {max_length} 字，精简一下再告诉我。"),
        )
        return

    chat_id = storage.add_chat_message(qq, nickname, str(group_id), content)
    print(f"[聊天#{chat_id}] 群{group_id} {nickname}({qq}): {content}")


def enqueue_bug_followup(event: dict, content: str):
    """若玩家正在回答 Bug 追问，把这条消息作为补充说明重新检查。"""
    sender = event.get("sender") or {}
    nickname = sender.get("card") or sender.get("nickname") or "玩家"
    return storage.add_bug_followup(
        str(event.get("user_id", "")),
        nickname,
        str(event.get("group_id", "")),
        content,
    )


def is_at_self(event: dict) -> bool:
    self_id = str(event.get("self_id", ""))
    if not self_id:
        return False
    message = event.get("message")
    if isinstance(message, list):
        return any(
            isinstance(seg, dict)
            and seg.get("type") == "at"
            and str(seg.get("data", {}).get("qq", "")) == self_id
            for seg in message
        )
    raw = str(event.get("raw_message") or message or "")
    return re.search(
        rf"\[CQ:at,qq={re.escape(self_id)}(?:,[^\]]*)?\]", raw
    ) is not None


_OWNER_REPLY_RE = re.compile(r"^\s*#回复(?:\s+|[:：])?(.*)$", re.DOTALL)


async def handle_owner_reply(ws, cfg, event) -> bool:
    """只接受指定管理员在原群发送的“#回复 …”，无需真实 @。"""
    owner_qq = str(cfg.get("agent_owner_qq", "651846226"))
    if str(event.get("user_id", "")) != owner_qq:
        return False
    match = _OWNER_REPLY_RE.match(extract_plain_text(event))
    if not match:
        return False
    answer = match.group(1).strip()
    group_id = event.get("group_id")
    if not answer:
        await send_group_msg(
            ws, group_id,
            at_message(owner_qq, "请在 #回复 后填写你的判断或补充说明。"),
        )
        return True
    if len(answer) > 3000:
        await send_group_msg(
            ws, group_id,
            at_message(owner_qq, "回复超过 3000 字，请精简后重新发送。"),
        )
        return True
    row = storage.answer_active_owner_question(str(group_id), answer)
    if not row:
        await send_group_msg(
            ws, group_id,
            at_message(owner_qq, "当前群没有等待确认的问题。"),
        )
        return True
    await send_group_msg(
        ws, group_id,
        at_message(
            owner_qq,
            f"已收到对反馈 #{row['id']} 的回复，Agent 将继续处理。",
        ),
    )
    return True


def result_text(row: dict) -> str:
    state = row.get("agent_state")
    summary = str(row.get("agent_summary") or "处理完成").strip()
    if len(summary) > 600:
        summary = summary[:600] + "…"
    if state == "fixed":
        commit = str(row.get("agent_commit") or "")[:12]
        suffix = f"\n提交：{commit}" if commit else ""
        return (
            f"✅ 反馈 #{row['id']} 已由 Agent 修复并上线测试服：{summary}"
            f"{suffix}\n测试地址：https://test.grand-umi.com/"
        )
    if state == "rejected":
        return f"ℹ️ 反馈 #{row['id']} 已完成确认：{summary}"
    if state == "manual":
        return f"⚠️ 反馈 #{row['id']} 自动处理已转人工：{summary}"
    return f"❌ 反馈 #{row['id']} 自动处理失败：{summary}"


async def notification_loop(ws, cfg) -> None:
    """串行发送管理员问题与玩家最终结果。"""
    interval = max(1, int(cfg.get("agent_notification_interval_seconds", 3)))
    owner_qq = str(cfg.get("agent_owner_qq", "651846226"))
    while True:
        try:
            if cfg.get("agent_enabled", False):
                question = storage.get_owner_question_to_send()
                if question:
                    content = str(question.get("content") or "")
                    if len(content) > 500:
                        content = content[:500] + "…"
                    detail = str(question.get("agent_question") or "需要你的确认")
                    text = (
                        f"反馈 #{question['id']} 需要确认\n"
                        f"玩家反馈：{content}\n\n{detail}\n\n"
                        "请发送：#回复 你的判断或补充说明（无需 @机器人）"
                    )
                    await send_group_msg(
                        ws, question["group_id"], at_message(owner_qq, text)
                    )
                    storage.mark_owner_question_sent(question["id"])

                result = storage.get_agent_result_to_send()
                if result:
                    await send_group_msg(
                        ws,
                        result["group_id"],
                        at_message(str(result["qq"]), result_text(result)),
                    )
                    storage.mark_agent_result_sent(result["id"])

            chat = (
                storage.get_chat_result_to_send()
                if cfg.get("chat_agent_enabled", False)
                else None
            )
            if chat:
                if chat["state"] == "completed":
                    text = str(chat.get("reply") or "嗯？妾身刚才没听清。")
                else:
                    text = "妾身现在暂时无法回答。过一会儿再来觐见吧。"
                await send_group_msg(
                    ws,
                    chat["group_id"],
                    at_message(str(chat["qq"]), text),
                )
                storage.mark_chat_result_sent(chat["id"])
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            print(f"[Agent通知] 发送失败: {exc}")
        await asyncio.sleep(interval)


async def on_event(ws, cfg, event) -> None:
    # 只处理群消息
    if event.get("post_type") != "message" or event.get("message_type") != "group":
        return

    allowed = cfg.get("allowed_groups") or []
    if allowed and event.get("group_id") not in allowed:
        return  # 群白名单(为空表示全部群)

    text = extract_plain_text(event)
    if await handle_owner_reply(ws, cfg, event):
        return
    content = match_feedback(text)
    if content is not None:
        try:
            await handle_feedback(ws, cfg, event, content)
        except Exception as e:
            print(f"[错误] 处理反馈异常: {e}")
        return
    followup_id = enqueue_bug_followup(event, text)
    if followup_id:
        print(
            f"[Bug补充#{followup_id}] 群{event.get('group_id')} "
            f"{event.get('user_id')}: {text}"
        )
        return
    if is_at_self(event):
        try:
            await handle_chat(ws, cfg, event, text.strip())
        except Exception as e:  # 单条消息出错不应拖垮整个连接
            print(f"[错误] 处理聊天异常: {e}")


async def run() -> None:
    cfg = load_config()
    storage.init_db()
    url = build_ws_url(cfg)
    print(f"GrandUMI bug 反馈机器人启动,连接 {cfg['ws_url']} …")

    # 断线自动重连
    while True:
        try:
            async with ws_connect(url, max_size=None) as ws:
                print("已连接 NapCat,等待群消息…")
                notifier = None
                if cfg.get("agent_enabled", False) or cfg.get(
                    "chat_agent_enabled", False
                ):
                    notifier = asyncio.create_task(notification_loop(ws, cfg))
                try:
                    async for raw in ws:
                        try:
                            event = json.loads(raw)
                        except json.JSONDecodeError:
                            continue
                        # 动作响应(带 echo / status)无 post_type,跳过
                        if "post_type" not in event:
                            continue
                        await on_event(ws, cfg, event)
                finally:
                    if notifier:
                        notifier.cancel()
                        with contextlib.suppress(asyncio.CancelledError):
                            await notifier
        except (OSError, websockets.exceptions.WebSocketException) as e:
            print(f"连接断开/失败: {e};5 秒后重连…")
            await asyncio.sleep(5)


if __name__ == "__main__":
    try:
        asyncio.run(run())
    except KeyboardInterrupt:
        print("\n已退出。")
