# -*- coding: utf-8 -*-
"""GrandUMI QQ 群 bug 反馈机器人(OneBot 11 / NapCat 正向 WebSocket)。

工作流程:
  1. 主动连接 NapCat 的正向 WS 服务端(同一条连接收事件 + 发动作)。
  2. 普通成员 @机器人时固定回复；管理员 @与 Bug 路由按需处理图片。
  3. 含 bug 的消息先检查描述完整性，合格后只记录到 SQLite/Issue。

运行: py bot.py
依赖: websockets(见 requirements.txt);GitHub 走本机已登录的 gh CLI。
"""

import asyncio
import contextlib
import hashlib
import json
import os
import re
import signal
import sys
import time
from uuid import uuid4

import websockets
# 注意:websockets 16.0 的新版 asyncio 实现(默认的 websockets.connect)与 NapCat
# 的 WebSocket 握手不兼容(会被 NapCat 直接关连接,报 InvalidMessage)。
# 实测老版 legacy 实现可正常连接,故这里显式使用 legacy 客户端。
from websockets.legacy.client import connect as ws_connect

import storage
import media_pipeline
import qq_whitelist_sync

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


class OneBotClient:
    """在同一条正向 WebSocket 上并发接收事件并等待 API 动作响应。"""

    def __init__(self, ws):
        self.ws = ws
        self.pending = {}

    async def send(self, payload) -> None:
        await self.ws.send(payload)

    async def call_action(
        self, action: str, params: dict, timeout: float = 20
    ) -> dict:
        echo = f"grandumi:{uuid4().hex}"
        future = asyncio.get_running_loop().create_future()
        self.pending[echo] = future
        try:
            await self.send(
                json.dumps(
                    {"action": action, "params": params, "echo": echo},
                    ensure_ascii=False,
                )
            )
            response = await asyncio.wait_for(future, timeout=timeout)
        finally:
            self.pending.pop(echo, None)
        if response.get("status") != "ok" or response.get("retcode", 0) != 0:
            detail = response.get("message") or response.get("wording") or action
            raise RuntimeError(f"NapCat 动作失败：{detail}")
        return response

    def resolve_response(self, response: dict) -> bool:
        future = self.pending.get(str(response.get("echo") or ""))
        if not future or future.done():
            return False
        future.set_result(response)
        return True

    def close(self) -> None:
        for future in self.pending.values():
            if not future.done():
                future.cancel()
        self.pending.clear()


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


def _message_segments(value):
    if isinstance(value, list):
        return value
    if isinstance(value, dict):
        for key in ("message", "content", "messages"):
            if isinstance(value.get(key), list):
                return value[key]
    return []


def _sender_label(value: dict) -> str:
    sender = value.get("sender") or {}
    data = value.get("data") or {}
    return str(
        sender.get("card")
        or sender.get("nickname")
        or data.get("name")
        or data.get("nickname")
        or value.get("nickname")
        or sender.get("user_id")
        or data.get("uin")
        or "转发成员"
    )[:80]


async def _collect_segments(client, segments, state: dict, depth: int = 0) -> None:
    if depth > state["max_depth"] or state["nodes"] >= state["max_nodes"]:
        return
    for segment in _message_segments(segments):
        if state["nodes"] >= state["max_nodes"]:
            break
        if not isinstance(segment, dict):
            continue
        state["nodes"] += 1
        kind = str(segment.get("type") or "")
        data = segment.get("data") or {}
        if kind == "text":
            text = str(data.get("text") or "").strip()
            if text:
                state["text"].append(text)
        elif kind == "image" and len(state["images"]) < state["max_images"]:
            state["images"].append(
                {
                    "url": str(data.get("url") or "").strip(),
                    "file": str(data.get("file") or "").strip(),
                    "summary": str(data.get("summary") or "").strip()[:100],
                    "source": "forward" if depth else "direct",
                }
            )
        elif kind == "node":
            content = data.get("content") or data.get("message") or []
            label = _sender_label(segment)
            before = len(state["text"])
            await _collect_segments(client, content, state, depth + 1)
            if len(state["text"]) > before:
                joined = " ".join(state["text"][before:])
                state["text"][before:] = [f"{label}：{joined}"]
        elif kind == "forward":
            content = data.get("content")
            if not isinstance(content, list):
                message_id = str(data.get("id") or "").strip()
                if not message_id or not hasattr(client, "call_action"):
                    continue
                response = await client.call_action(
                    "get_forward_msg", {"message_id": message_id}
                )
                payload = response.get("data") or {}
                content = (
                    payload.get("messages")
                    or payload.get("message")
                    or payload.get("content")
                    or []
                ) if isinstance(payload, dict) else payload
            if isinstance(content, list):
                state["text"].append("【合并转发】")
                for node in content[: state["max_nodes"]]:
                    node_segments = _message_segments(node)
                    if not node_segments and isinstance(node, dict):
                        node_segments = [node]
                    label = _sender_label(node) if isinstance(node, dict) else "转发成员"
                    before = len(state["text"])
                    await _collect_segments(
                        client, node_segments, state, depth + 1
                    )
                    if len(state["text"]) > before:
                        joined = " ".join(state["text"][before:])
                        state["text"][before:] = [f"{label}：{joined}"]


async def expand_event_content(client, event: dict, cfg: dict):
    """展开合并转发，返回带说话人上下文的文本和图片引用。"""
    state = {
        "text": [],
        "images": [],
        "nodes": 0,
        "max_nodes": max(5, min(100, int(cfg.get("forward_max_nodes", 40)))),
        "max_depth": max(1, min(5, int(cfg.get("forward_max_depth", 3)))),
        "max_images": max(1, min(8, int(cfg.get("vision_max_images", 4)))),
    }
    await _collect_segments(client, event.get("message"), state)
    text = "\n".join(state["text"]).strip()
    return text, state["images"]


async def download_media_refs(client, refs, cfg: dict):
    """只在已命中聊天或 Bug 路由后下载图片。"""
    if not refs or not cfg.get("vision_enabled", True):
        return [], 0
    maximum = max(64 * 1024, int(cfg.get("vision_max_image_bytes", 8 * 1024 * 1024)))
    media = []
    failures = 0
    media_pipeline.cleanup_expired_media(
        int(cfg.get("vision_media_ttl_seconds", 86400))
    )
    for reference in refs[: max(1, int(cfg.get("vision_max_images", 4)))]:
        url = str(reference.get("url") or "").strip()
        if not url and reference.get("file") and hasattr(client, "call_action"):
            try:
                response = await client.call_action(
                    "get_image", {"file": reference["file"]}
                )
                data = response.get("data") or {}
                url = str(data.get("url") or "").strip()
            except RuntimeError as exc:
                print(f"[识图] NapCat 获取图片失败：{exc}")
        if not url:
            failures += 1
            continue
        try:
            item = await asyncio.to_thread(
                media_pipeline.download_image, url, maximum
            )
            item["source"] = reference.get("source") or "direct"
            media.append(item)
        except (OSError, ValueError) as exc:
            failures += 1
            print(f"[识图] 图片读取失败：{exc}")
    return media, failures


# 反馈触发:群消息里只要出现 bug（忽略大小写）即进入描述检查。
_BUG_RE = re.compile(r"bug", re.IGNORECASE)
_LEADING_BUG_RE = re.compile(r"^\s*#bug(?:反馈)?[\s:：]*", re.IGNORECASE)
_CHAT_TRIGGER_RE = re.compile(r"^\s*#聊天(?:\s+|[:：])?(.*)$", re.DOTALL)
_PERSONALITY_SWITCH_RE = re.compile(
    r"^\s*#切换\s*(娜美|罗宾|女帝)\s*$"
)
_PERSONALITY_KEYS = {
    "娜美": "nami",
    "罗宾": "robin",
    "女帝": "hancock",
}
_PERSONALITY_SWITCH_REPLIES = {
    "nami": "已经切换成娜美。接下来由我掌舵，可别给我添乱。",
    "robin": "已经切换成罗宾。呵呵，接下来就让我安静地陪着各位吧。",
    "hancock": "已经切换成女帝。能由妾身回应，是你们莫大的荣幸。",
}
_PERSONALITY_BUSY_REPLIES = {
    "hancock": "妾身现在没空，稍后再来觐见吧。",
    "nami": "我现在忙不过来，等会儿再问吧。",
    "robin": "我现在暂时抽不开身，稍后再聊吧。",
}
_PERSONALITY_EMPTY_REPLIES = {
    "hancock": "嗯？妾身刚才没听清。",
    "nami": "嗯？刚才那句我没听清。",
    "robin": "刚才那句话我没有听清，可以再说一次吗？",
}
_PERSONALITY_FAILED_REPLIES = {
    "hancock": "妾身现在暂时无法回答。过一会儿再来觐见吧。",
    "nami": "我现在暂时回答不了，过一会儿再来吧。",
    "robin": "我现在暂时无法回答，稍后再聊吧。",
}
_ORDINARY_CHAT_DISABLED_REPLY = "我只跟释迦大人聊天"


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


def match_personality_switch(text: str):
    if not text:
        return None
    match = _PERSONALITY_SWITCH_RE.match(text)
    return _PERSONALITY_KEYS.get(match.group(1)) if match else None


def at_message(qq: str, text: str) -> list[dict]:
    """使用 OneBot 消息段构造 @，避免把外部文本当作 CQ 码解析。"""
    return [
        {"type": "at", "data": {"qq": str(qq)}},
        {"type": "text", "data": {"text": " " + text}},
    ]


def member_verification_groups(cfg: dict) -> set[str]:
    """新人验证必须显式配置目标群；可与申请阶段自动审批顺序共存。"""
    if not cfg.get("new_member_verification_enabled", False):
        return set()
    groups = set()
    for value in cfg.get("new_member_verification_groups") or []:
        text = str(value).strip()
        if text.isdigit() and int(text) > 0:
            groups.add(str(int(text)))
    return groups


def group_add_auto_approval_groups(cfg: dict) -> set[str]:
    """自动审批只对显式配置的群生效；空列表永远不表示全部群。"""
    if not cfg.get("group_add_auto_approval_enabled", False):
        return set()
    groups = set()
    for value in cfg.get("group_add_auto_approval_groups") or []:
        text = str(value).strip()
        if text.isdigit() and int(text) > 0:
            groups.add(str(int(text)))
    return groups


def is_real_at_self(event: dict) -> bool:
    """验证流程只接受 OneBot 顶层结构化 at 段，不信任正文或 CQ 字符串。"""
    self_id = str(event.get("self_id") or "")
    message = event.get("message")
    if not self_id or not isinstance(message, list):
        return False
    return any(
        isinstance(segment, dict)
        and segment.get("type") == "at"
        and str((segment.get("data") or {}).get("qq") or "") == self_id
        for segment in message
    )


_QQ_NUMBER_RE = re.compile(r"(?<!\d)([1-9]\d{4,11})(?!\d)")


_GROUP_REQUEST_ANSWER_RE = re.compile(
    r"(?:^|[\r\n])\s*(?:答案|回答|answer)\s*[：:]\s*([^\r\n]*)",
    re.IGNORECASE,
)
_GROUP_REQUEST_QQ_WRAPPERS = (
    re.compile(r"([1-9]\d{4,11})"),
    re.compile(
        r"(?:邀请人(?:的)?\s*)?(?:QQ|qq)(?:号|号码)?\s*(?:是|为|[：:=])?\s*"
        r"([1-9]\d{4,11})"
    ),
    re.compile(
        r"邀请人(?:的)?(?:\s*(?:QQ|qq)(?:号|号码)?)?\s*(?:是|为)?\s*[：:=]?\s*"
        r"([1-9]\d{4,11})"
    ),
    re.compile(
        r"([1-9]\d{4,11})\s*(?:是|为)?\s*邀请人(?:的)?"
        r"(?:\s*(?:QQ|qq)(?:号|号码)?)?"
    ),
)


def extract_group_request_inviter_qq(event: dict):
    """从加群申请答案中严格提取唯一邀请人 QQ，避免扫描无关事件字段。"""
    comment = event.get("comment")
    if not isinstance(comment, str) or not comment.strip():
        return None, "未填写有效的邀请人 QQ 号。"
    text = comment.strip()
    labelled_answers = [
        value.strip() for value in _GROUP_REQUEST_ANSWER_RE.findall(text)
    ]
    if labelled_answers:
        if len(labelled_answers) != 1 or not labelled_answers[0]:
            return None, "回答格式无效，请只填写一位邀请人的 QQ 号。"
        text = labelled_answers[0]

    candidates = _QQ_NUMBER_RE.findall(text)
    if len(candidates) != 1:
        return None, "回答格式无效，请只填写一位邀请人的 QQ 号。"
    for pattern in _GROUP_REQUEST_QQ_WRAPPERS:
        match = pattern.fullmatch(text.strip())
        if match:
            return match.group(1), ""
    return None, "回答格式无效，请只填写一位邀请人的 QQ 号。"


# 同一进程内只在 OneBot 明确确认动作成功后去重。动作失败或查询失败不会写入，
# 让 NapCat 重投同一申请时仍可安全重试；上限用于避免长时间运行后无限增长。
_HANDLED_GROUP_ADD_REQUEST_LIMIT = 2048
_handled_group_add_requests: dict[str, None] = {}


def _remember_handled_group_add_request(key: str) -> None:
    _handled_group_add_requests[key] = None
    while len(_handled_group_add_requests) > _HANDLED_GROUP_ADD_REQUEST_LIMIT:
        _handled_group_add_requests.pop(next(iter(_handled_group_add_requests)))


async def _set_group_add_request_result(
    client, flag: str, approve: bool, reason: str = ""
) -> None:
    params = {"flag": flag, "sub_type": "add", "approve": approve}
    if not approve:
        params["reason"] = reason
    await client.call_action("set_group_add_request", params)


async def handle_group_add_auto_approval(client, cfg: dict, event: dict) -> bool:
    """按实时群成员身份区分好友邀请与普通申请，并审批目标群请求。"""
    group_id = str(event.get("group_id") or "")
    if (
        event.get("post_type") != "request"
        or event.get("request_type") != "group"
        or event.get("sub_type") != "add"
        or group_id not in group_add_auto_approval_groups(cfg)
    ):
        return False

    raw_flag = event.get("flag")
    applicant = str(event.get("user_id") or "").strip()
    bot_qq = str(event.get("self_id") or "").strip()
    if (
        not isinstance(raw_flag, str)
        or not raw_flag.strip()
        or not _QQ_NUMBER_RE.fullmatch(applicant)
        or not _QQ_NUMBER_RE.fullmatch(bot_qq)
    ):
        print(
            f"[加群审批] 群{group_id}收到字段不完整的申请事件，保持待审批："
            f"flag={'有效' if isinstance(raw_flag, str) and raw_flag.strip() else '无效'}，"
            f"申请人={applicant or '无'}，"
            f"机器人={bot_qq or '无'}"
        )
        return True

    # flag 是 OneBot 的不透明请求标识，审批动作必须原样回传。
    flag = raw_flag
    request_key = f"{group_id}:{flag}"
    if request_key in _handled_group_add_requests:
        print(f"[加群审批] 群{group_id}申请{flag}已处理，忽略重复事件")
        return True

    try:
        members = await get_authoritative_group_members(client, group_id)
    except asyncio.CancelledError:
        raise
    except Exception as exc:
        print(
            f"[加群审批] 群{group_id}请求发起人{applicant}获取实时成员列表失败，"
            f"保持待审批：{exc}"
        )
        return True

    # NapCat 将“群成员邀请他人且需管理员审核”也上报为 add，且 user_id 是邀请人。
    # 普通主动申请的 user_id 是申请人，当前不应在群；因此必须先用实时成员列表区分。
    if applicant in members:
        try:
            await _set_group_add_request_result(client, flag, True)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            print(
                f"[加群审批] 群{group_id}群内发起人{applicant}的请求同意动作失败，"
                f"保持待审批：{exc}"
            )
            return True
        _remember_handled_group_add_request(request_key)
        print(f"[加群审批] 群{group_id}已同意群内发起人{applicant}对应的好友入群邀请")
        return True

    candidate, error = extract_group_request_inviter_qq(event)
    reject_reason = error
    if not reject_reason and candidate == applicant:
        reject_reason = "不能填写申请人自己的 QQ 号作为邀请人。"
    if not reject_reason and candidate == bot_qq:
        reject_reason = "不能填写本群机器人 QQ 号作为邀请人。"

    if reject_reason:
        try:
            await _set_group_add_request_result(client, flag, False, reject_reason)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            print(
                f"[加群审批] 群{group_id}申请人{applicant}拒绝动作失败，"
                f"保持待审批：{exc}"
            )
            return True
        _remember_handled_group_add_request(request_key)
        print(f"[加群审批] 群{group_id}已拒绝申请人{applicant}：{reject_reason}")
        return True

    approve = candidate in members
    reason = "" if approve else f"邀请人 QQ {candidate} 当前不在本群。"
    verification = None
    if approve and group_id in member_verification_groups(cfg):
        try:
            # 必须先落一条不可踢人的预备授权，再调用外部审批动作。这样即使
            # 审批响应后进程退出，后续真实入群通知仍能原子恢复同一会话。
            verification = storage.prepare_member_verification_approval(
                group_id,
                applicant,
                candidate,
                _event_source_time(event),
                _member_verification_reminder_delay(cfg),
                now=_event_received_at(event),
            )
        except Exception as exc:
            print(
                f"[加群审批] 群{group_id}申请人{applicant}登记验证授权失败，"
                f"保持待审批：{exc}"
            )
            return True
    try:
        await _set_group_add_request_result(client, flag, approve, reason)
    except asyncio.CancelledError:
        if verification:
            with contextlib.suppress(Exception):
                storage.record_member_verification_approval_failure(
                    verification["id"], "审批响应中断，等待事件重试"
                )
        raise
    except Exception as exc:
        if verification:
            with contextlib.suppress(Exception):
                storage.record_member_verification_approval_failure(
                    verification["id"], f"OneBot 审批动作失败：{exc}"
                )
        print(
            f"[加群审批] 群{group_id}申请人{applicant}"
            f"{'同意' if approve else '拒绝'}动作失败，保持待审批：{exc}"
        )
        return True
    if verification:
        try:
            storage.complete_member_verification_approval(verification["id"])
        except Exception as exc:
            # 外部动作已经成功，不能反向伪装成未审批；预备记录仍是非踢人状态，
            # 后续真实入群通知可继续把它推进到提示阶段。
            print(
                f"[加群审批] 群{group_id}申请人{applicant}已同意，"
                f"但验证授权待入群通知恢复：{exc}"
            )
    _remember_handled_group_add_request(request_key)
    if approve:
        print(f"[加群审批] 群{group_id}已同意申请人{applicant}，邀请人{candidate}在群")
    else:
        print(f"[加群审批] 群{group_id}已拒绝申请人{applicant}：{reason}")
    return True


def extract_inviter_qq(event: dict):
    """只从顶层文字和真实 at 段提取唯一 QQ，不读取引用/转发/昵称。"""
    message = event.get("message")
    if not isinstance(message, list):
        return None, "请真正 @机器人，并在正文里填写邀请人的 QQ 号。"
    self_id = str(event.get("self_id") or "")
    candidates = set()
    for segment in message:
        if not isinstance(segment, dict):
            continue
        data = segment.get("data") or {}
        if segment.get("type") == "at":
            qq = str(data.get("qq") or "").strip()
            if qq not in ("", "all", self_id) and _QQ_NUMBER_RE.fullmatch(qq):
                candidates.add(qq)
        elif segment.get("type") == "text":
            candidates.update(_QQ_NUMBER_RE.findall(str(data.get("text") or "")))
    if not candidates:
        return None, "没有识别到邀请人 QQ，请只填写一个完整 QQ 号后重新 @机器人。"
    if len(candidates) != 1:
        return None, "识别到多个 QQ 号，请只保留一位邀请人的 QQ 后重新 @机器人。"
    return next(iter(candidates)), ""


_INVITER_REGISTRATION_BODY_RE = re.compile(
    r"^\s*邀请人\s*(?:的\s*)?QQ(?:号|号码)?\s*[：:=]\s*([1-9]\d{4,11})\s*$",
    re.IGNORECASE,
)


def extract_strict_inviter_registration_qq(event: dict):
    """识别无会话提示专用的严格登记正文，避免把普通含 QQ 聊天误判为登记。"""
    if not is_real_at_self(event):
        return None
    self_id = str(event.get("self_id") or "")
    text_parts = []
    for segment in event.get("message") or []:
        if not isinstance(segment, dict):
            return None
        segment_type = segment.get("type")
        data = segment.get("data") or {}
        if segment_type == "text":
            text_parts.append(str(data.get("text") or ""))
            continue
        if segment_type == "at" and str(data.get("qq") or "") == self_id:
            continue
        # 图片、引用、转发或对其他成员的 @ 都不属于严格登记正文。
        return None
    match = _INVITER_REGISTRATION_BODY_RE.fullmatch("".join(text_parts))
    return match.group(1) if match else None


def member_verification_message_key(event: dict) -> str:
    """优先使用 OneBot 消息号；缺失时对可信事件字段做稳定摘要。"""
    message_id = event.get("message_id")
    if message_id not in (None, ""):
        return f"onebot:{message_id}"
    payload = {
        "group_id": str(event.get("group_id") or ""),
        "user_id": str(event.get("user_id") or ""),
        "time": int(event.get("time") or 0),
        "message": event.get("message") if isinstance(event.get("message"), list) else [],
    }
    encoded = json.dumps(
        payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")
    return "sha256:" + hashlib.sha256(encoded).hexdigest()


def _event_received_at(event: dict) -> int:
    value = event.get("_grandumi_received_at")
    try:
        return int(value)
    except (TypeError, ValueError):
        return int(time.time())


def _event_source_time(event: dict) -> int:
    """OneBot 时间只用于审计/通知去重，异常时间退回本机接收时间。"""
    received_at = _event_received_at(event)
    try:
        source_time = int(event.get("time"))
    except (TypeError, ValueError):
        return received_at
    if abs(source_time - received_at) > 86400:
        return received_at
    return source_time


async def send_group_msg(ws, group_id, message) -> None:
    """发送群消息(动作经同一条正向 WS 下发)。"""
    payload = {
        "action": "send_group_msg",
        "params": {"group_id": int(group_id), "message": message},
    }
    await ws.send(json.dumps(payload, ensure_ascii=False))


async def send_group_msg_confirmed(client, group_id, message) -> dict:
    """安全关键消息必须等待 OneBot 成功响应，不能把“已写入 WS”当作已送达。"""
    if not hasattr(client, "call_action"):
        raise RuntimeError("当前 OneBot 客户端不支持确认式动作调用")
    return await client.call_action(
        "send_group_msg",
        {"group_id": int(group_id), "message": message},
    )


async def get_authoritative_group_members(client, group_id) -> set[str]:
    """通过成功返回的 OneBot 成员列表核验，不信任玩家正文或本地缓存。"""
    response = await client.call_action(
        "get_group_member_list",
        {"group_id": int(group_id), "no_cache": True},
    )
    data = response.get("data")
    if not isinstance(data, list):
        raise RuntimeError("OneBot 成员列表响应格式异常")
    members = set()
    for item in data:
        if not isinstance(item, dict) or item.get("user_id") in (None, ""):
            raise RuntimeError("OneBot 成员列表包含无效成员记录")
        returned_group = item.get("group_id")
        if returned_group not in (None, "") and str(returned_group) != str(group_id):
            raise RuntimeError("OneBot 成员列表混入其他群的数据")
        members.add(str(item["user_id"]))
    return members


def _member_verification_reminder_delay(cfg: dict) -> int:
    """历史 timeout 配置现仅决定首次追问时间，不再限制回答或触发移出。"""
    return max(
        60,
        min(86400, int(cfg.get("new_member_verification_timeout_seconds", 1800))),
    )


def _member_verification_poll_interval(cfg: dict) -> int:
    """后台恢复任务允许最长一小时轮询；群消息事件不经过此间隔。"""
    return max(
        1,
        min(
            3600,
            int(cfg.get("new_member_verification_poll_interval_seconds", 300)),
        ),
    )


def _member_verification_lease(cfg: dict) -> int:
    return max(
        30,
        min(300, int(cfg.get("new_member_verification_claim_lease_seconds", 60))),
    )


async def _try_send_verification_message(client, group_id, qq, text) -> bool:
    try:
        await send_group_msg_confirmed(client, group_id, at_message(str(qq), text))
        return True
    except asyncio.CancelledError:
        raise
    except Exception as exc:
        print(f"[新人验证] 群{group_id} 给{qq}发送消息失败：{exc}")
        return False


async def process_member_verification_prompt(
    client, cfg: dict, verification_id: int | None = None
) -> bool:
    """发送并确认新人提示；提示未确认送达时不开始一次提醒计时。"""
    groups = member_verification_groups(cfg)
    job = storage.claim_member_verification_prompt(
        verification_id,
        lease_seconds=_member_verification_lease(cfg),
        group_ids=groups,
    )
    if not job:
        return False
    token = str(job["claim_token"])
    reminder_seconds = _member_verification_reminder_delay(cfg)
    approved_inviter = str(job.get("inviter_qq") or "")
    if approved_inviter:
        text = (
            f"欢迎加入本群。加群申请已审核邀请人 QQ：{approved_inviter}。"
            "请真正 @“释迦的助理”，并回复同一 QQ 完成登记；"
            "审核记录不能在群消息中改写。"
        )
    else:
        text = (
            "欢迎加入本群。请回答邀请人的 QQ 号；"
            "回复时请真正 @“释迦的助理”，例如"
            "“@释迦的助理 邀请人QQ：123456789”。"
            "机器人确认邀请人当前在群后才算验证完成。"
        )
    try:
        await send_group_msg_confirmed(
            client,
            job["group_id"],
            at_message(str(job["newcomer_qq"]), text),
        )
    except asyncio.CancelledError:
        storage.release_member_verification_claim(
            job["id"], token, "prompt", "连接中断，等待重试"
        )
        raise
    except Exception as exc:
        storage.release_member_verification_claim(
            job["id"], token, "prompt", f"提示发送失败：{exc}"
        )
        print(
            f"[新人验证#{job['id']}] 群{job['group_id']}提示发送失败，"
            f"将自动重试：{exc}"
        )
        return True
    if not storage.complete_member_verification_prompt(
        job["id"], token, reminder_seconds
    ):
        print(f"[新人验证#{job['id']}] 提示已发送，但会话状态已变化")
        return True
    print(
        f"[新人验证#{job['id']}] 已提示群{job['group_id']}新人"
        f"{job['newcomer_qq']}，{reminder_seconds}秒后追问一次"
    )
    return True


async def process_member_inviter_check(
    client, job: dict, notify_on_failure: bool = False
) -> bool:
    """核查已持久化的邀请人答案；网络失败时保留答案等待恢复。"""
    token = str(job.get("claim_token") or "")
    candidate = str(job.get("candidate_qq") or "")
    try:
        members = await get_authoritative_group_members(client, job["group_id"])
    except asyncio.CancelledError:
        storage.defer_member_inviter_check(
            job["id"], token, "连接中断，等待自动重试"
        )
        raise
    except Exception as exc:
        storage.defer_member_inviter_check(
            job["id"], token, f"成员列表核查失败：{exc}"
        )
        print(f"[新人验证#{job['id']}] 成员列表核查失败，将自动重试：{exc}")
        if notify_on_failure:
            await _try_send_verification_message(
                client,
                job["group_id"],
                job["newcomer_qq"],
                "成员列表暂时无法核查，答案已经保存，机器人会自动重试，请勿重复发送。",
            )
        return True

    newcomer = str(job["newcomer_qq"])
    if newcomer not in members:
        storage.complete_member_verification_absent(job["id"], token)
        print(f"[新人验证#{job['id']}] 新人已不在群，结束验证")
        return True
    if candidate in members:
        if storage.complete_member_inviter_check(job["id"], token, candidate):
            await _try_send_verification_message(
                client,
                job["group_id"],
                newcomer,
                f"验证完成，已记录邀请人 QQ：{candidate}。",
            )
            print(
                f"[新人验证#{job['id']}] 验证完成：新人{newcomer}，"
                f"邀请人{candidate}"
            )
        return True

    result = storage.reject_member_inviter_check(
        job["id"], token, f"QQ {candidate} 不在当前群成员列表"
    )
    if result and result.get("can_retry"):
        await _try_send_verification_message(
            client,
            job["group_id"],
            newcomer,
            f"QQ {candidate} 当前不在本群，请核对后重新 @机器人，回答另一位邀请人 QQ。",
        )
    return True


async def process_member_verification_reminder(client, job: dict) -> bool:
    """到点后只发送一次确认式追问；失败保留租约恢复，绝不查询或移出成员。"""
    token = str(job.get("claim_token") or "")
    approved_inviter = str(job.get("inviter_qq") or "")
    if approved_inviter:
        text = (
            "还没有完成邀请人登记。请真正 @“释迦的助理”，并回复已审核的"
            f"邀请人 QQ：{approved_inviter}；之后补充即可。"
        )
    else:
        text = (
            "还没有收到邀请人 QQ。请真正 @“释迦的助理”并回复邀请人 QQ；"
            "之后补充即可。"
        )
    try:
        await send_group_msg_confirmed(
            client,
            job["group_id"],
            at_message(str(job["newcomer_qq"]), text),
        )
    except asyncio.CancelledError:
        storage.release_member_verification_reminder(
            job["id"], token, "连接中断，等待重新发送提醒"
        )
        raise
    except Exception as exc:
        storage.release_member_verification_reminder(
            job["id"], token, f"提醒发送失败：{exc}"
        )
        print(f"[新人验证#{job['id']}] 追问发送失败，将自动重试：{exc}")
        return True
    if storage.complete_member_verification_reminder(job["id"], token):
        print(
            f"[新人验证#{job['id']}] 已追问群{job['group_id']}新人"
            f"{job['newcomer_qq']}，后续可无限期回答"
        )
    else:
        print(f"[新人验证#{job['id']}] 追问已发送，但会话状态已变化")
    return True


async def run_member_verification_job_once(client, cfg: dict) -> bool:
    """按答案恢复、首次提示、一次追问的优先级执行一个持久任务。"""
    groups = member_verification_groups(cfg)
    if not groups:
        return False
    lease = _member_verification_lease(cfg)
    job = storage.claim_pending_member_inviter_check(
        lease_seconds=lease, group_ids=groups
    )
    if job:
        return await process_member_inviter_check(client, job)
    if await process_member_verification_prompt(client, cfg):
        return True
    job = storage.claim_due_member_verification_reminder(
        lease_seconds=lease, group_ids=groups
    )
    if job:
        return await process_member_verification_reminder(client, job)
    return False


async def member_verification_loop(client, cfg: dict, event_lock) -> None:
    """连接恢复后从 SQLite 继续未完成的提示、答案核查和一次追问。"""
    interval = _member_verification_poll_interval(cfg)
    while True:
        try:
            async with event_lock:
                await run_member_verification_job_once(client, cfg)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            print(f"[新人验证] 后台调度异常：{exc}")
        await asyncio.sleep(interval)


async def handle_member_verification_notice(client, cfg: dict, event: dict) -> bool:
    groups = member_verification_groups(cfg)
    group_id = str(event.get("group_id") or "")
    if (
        event.get("post_type") != "notice"
        or group_id not in groups
        or event.get("notice_type") not in ("group_increase", "group_decrease")
    ):
        return False
    newcomer = str(event.get("user_id") or "")
    if not newcomer or newcomer == str(event.get("self_id") or ""):
        return True
    if event.get("notice_type") == "group_decrease":
        storage.mark_member_verification_left(
            group_id,
            newcomer,
            now=_event_received_at(event),
            detail=f"收到 OneBot 退群通知：{event.get('sub_type') or 'unknown'}",
        )
        return True

    sender = event.get("sender") or {}
    nickname = str(
        sender.get("card") or sender.get("nickname") or event.get("nickname") or ""
    )
    verification = storage.start_member_verification(
        group_id,
        newcomer,
        nickname,
        _event_source_time(event),
        now=_event_received_at(event),
    )
    if verification.get("created"):
        await process_member_verification_prompt(client, cfg, verification["id"])
    return True


async def handle_member_verification_reply(client, cfg: dict, event: dict) -> bool:
    group_id = str(event.get("group_id") or "")
    newcomer = str(event.get("user_id") or "")
    if group_id not in member_verification_groups(cfg):
        return False
    if not is_real_at_self(event):
        return False
    received_at = _event_received_at(event)
    active = storage.get_active_member_verification(group_id, newcomer)
    strict_registration_qq = extract_strict_inviter_registration_qq(event)
    if active and active["state"] == "approval_pending":
        # 申请虽然可信，但外部审批结果尚未确认；此状态不能仅凭正文升级权限。
        if strict_registration_qq:
            await _try_send_verification_message(
                client,
                group_id,
                newcomer,
                "加群审批结果尚未确认，请稍后再试或联系释迦大人。",
            )
            return True
        return False
    if active and active["state"] == "awaiting_join":
        active = storage.activate_member_verification_from_reply(
            group_id,
            newcomer,
            _member_verification_reminder_delay(cfg),
            event_time=_event_source_time(event),
            now=received_at,
        )
    if not active:
        if storage.has_member_verification_response(
            group_id,
            newcomer,
            member_verification_message_key(event),
        ):
            return True
        if strict_registration_qq:
            await _try_send_verification_message(
                client,
                group_id,
                newcomer,
                "当前没有待登记的邀请人验证，请联系释迦大人核对。",
            )
            return True
        return False
    candidate, error = extract_inviter_qq(event)
    if error:
        await _try_send_verification_message(client, group_id, newcomer, error)
        return True
    if candidate == newcomer:
        await _try_send_verification_message(
            client, group_id, newcomer, "不能把自己填写为邀请人，请核对后重新回答。"
        )
        return True
    if candidate == str(event.get("self_id") or ""):
        await _try_send_verification_message(
            client, group_id, newcomer, "不能把本群机器人填写为邀请人，请核对后重新回答。"
        )
        return True
    approved_inviter = str(active.get("inviter_qq") or "")
    if approved_inviter and candidate != approved_inviter:
        await _try_send_verification_message(
            client,
            group_id,
            newcomer,
            "填写的邀请人 QQ 与已审核的加群申请不一致，不能通过群消息改写；"
            f"请回复已审核的 QQ：{approved_inviter}。",
        )
        return True

    started = storage.begin_member_inviter_check(
        group_id,
        newcomer,
        candidate,
        member_verification_message_key(event),
        _event_source_time(event),
        received_at=received_at,
    )
    status = started.get("status")
    if status in ("duplicate", "conflict", "no_session"):
        return True
    if status == "busy":
        await _try_send_verification_message(
            client, group_id, newcomer, "上一次回答仍在核查中，请稍候，不用重复发送。"
        )
        return True
    if status == "expired":
        await _try_send_verification_message(
            client, group_id, newcomer, "验证时间已经结束，机器人正在进行最终状态核查。"
        )
        return True
    if status != "claimed":
        print(f"[新人验证] 未知回答领取结果：{status}")
        return True
    await process_member_inviter_check(
        client, started["verification"], notify_on_failure=True
    )
    return True


async def handle_feedback(ws, cfg, event, content, media=None) -> None:
    """把含 bug 的消息送入描述检查队列；此处不回确认话术。"""
    group_id = event.get("group_id")
    qq = str(event.get("user_id", ""))
    sender = event.get("sender") or {}
    nickname = sender.get("card") or sender.get("nickname") or ""
    personality = storage.get_group_personality(str(group_id))

    intake_id = storage.add_chat_message(
        qq,
        nickname,
        str(group_id),
        content or "（只提到了 bug，没有描述具体现象）",
        kind="bug_intake",
        media=media,
        personality=personality,
    )
    print(f"[Bug检查#{intake_id}] 群{group_id} {nickname}({qq}): {content}")


async def handle_chat(
    ws, cfg, event, content: str, media=None, kind: str = "chat"
) -> None:
    """把群聊请求写入独立只读 Agent 队列。"""
    group_id = event.get("group_id")
    qq = str(event.get("user_id", ""))
    sender = event.get("sender") or {}
    nickname = sender.get("card") or sender.get("nickname") or "玩家"
    personality = storage.get_group_personality(str(group_id))

    enabled_key = (
        "admin_agent_enabled" if kind == "admin_agent" else "chat_agent_enabled"
    )
    if not cfg.get(enabled_key, False):
        await send_group_msg(
            ws, group_id, at_message(qq, _PERSONALITY_BUSY_REPLIES[personality])
        )
        return
    content = match_chat(content) if match_chat(content) is not None else content
    if not content:
        content = (
            "（玩家附了一张或多张图片，请直接查看图片后回答）"
            if media else "（玩家只@了你，没有附加文字）"
        )
    length_key = (
        "admin_agent_max_content_length"
        if kind == "admin_agent"
        else "chat_max_content_length"
    )
    default_length = 3000 if kind == "admin_agent" else 500
    max_length = max(20, int(cfg.get(length_key, default_length)))
    if len(content) > max_length:
        await send_group_msg(
            ws,
            group_id,
            at_message(qq, f"一次最多聊 {max_length} 字，精简一下再告诉我。"),
        )
        return

    chat_id = storage.add_chat_message(
        qq,
        nickname,
        str(group_id),
        content,
        kind=kind,
        media=media,
        personality=personality,
    )
    label = "管理员Agent" if kind == "admin_agent" else "聊天"
    print(f"[{label}#{chat_id}] 群{group_id} {nickname}({qq}): {content}")


def enqueue_bug_followup(event: dict, content: str, media=None):
    """若玩家正在回答 Bug 追问，把这条消息作为补充说明重新检查。"""
    sender = event.get("sender") or {}
    nickname = sender.get("card") or sender.get("nickname") or "玩家"
    group_id = str(event.get("group_id", ""))
    return storage.add_bug_followup(
        str(event.get("user_id", "")),
        nickname,
        group_id,
        content,
        media=media,
        personality=storage.get_group_personality(group_id),
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


def is_admin_agent_request(event: dict, cfg: dict) -> bool:
    """只信任 OneBot 事件中的真实发送者 QQ 与真实 @ 消息段。"""
    if not cfg.get("admin_agent_enabled", False):
        return False
    owner_qq = str(cfg.get("admin_agent_owner_qq", "651846226"))
    return str(event.get("user_id", "")) == owner_qq and is_at_self(event)


async def handle_personality_switch(ws, cfg, event, personality: str) -> bool:
    owner_qq = str(
        cfg.get("admin_agent_owner_qq")
        or cfg.get("agent_owner_qq")
        or "651846226"
    )
    qq = str(event.get("user_id", ""))
    group_id = str(event.get("group_id", ""))
    if qq != owner_qq:
        await send_group_msg(
            ws,
            group_id,
            at_message(qq, "只有赛博释迦可以切换机器人的人格。"),
        )
        return True
    selected = storage.set_group_personality(group_id, personality, qq)
    await send_group_msg(
        ws,
        group_id,
        at_message(qq, _PERSONALITY_SWITCH_REPLIES[selected]),
    )
    return True


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
    next_media_cleanup = 0.0
    while True:
        try:
            now = asyncio.get_running_loop().time()
            if now >= next_media_cleanup:
                await asyncio.to_thread(
                    media_pipeline.cleanup_expired_media,
                    int(cfg.get("vision_media_ttl_seconds", 86400)),
                )
                next_media_cleanup = now + 3600
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
                    personality = storage.normalize_personality(
                        chat.get("personality")
                    )
                    text = str(
                        chat.get("reply")
                        or _PERSONALITY_EMPTY_REPLIES[personality]
                    )
                else:
                    personality = storage.normalize_personality(
                        chat.get("personality")
                    )
                    text = _PERSONALITY_FAILED_REPLIES[personality]
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
    if await handle_group_add_auto_approval(ws, cfg, event):
        return

    if await handle_member_verification_notice(ws, cfg, event):
        return

    # 其余路由只处理群消息
    if event.get("post_type") != "message" or event.get("message_type") != "group":
        return

    if await handle_member_verification_reply(ws, cfg, event):
        return

    allowed = cfg.get("allowed_groups") or []
    if allowed and event.get("group_id") not in allowed:
        return  # 群白名单(为空表示全部群)

    if not is_at_self(event) and await handle_owner_reply(ws, cfg, event):
        return
    try:
        text, image_refs = await expand_event_content(ws, event, cfg)
    except Exception as exc:
        print(f"[错误] 展开消息异常: {exc}")
        text = extract_plain_text(event)
        image_refs = []
    personality = match_personality_switch(text)
    if personality is not None:
        await handle_personality_switch(ws, cfg, event, personality)
        return
    if is_admin_agent_request(event, cfg):
        media = []
        try:
            media, failures = await download_media_refs(ws, image_refs, cfg)
            admin_text = text.strip()
            if failures:
                admin_text += f"\n（有 {failures} 张图片读取失败）"
            await handle_chat(
                ws,
                cfg,
                event,
                admin_text.strip(),
                media,
                kind="admin_agent",
            )
        except Exception as exc:
            media_pipeline.cleanup_media(media)
            print(f"[错误] 处理管理员 Agent 请求异常: {exc}")
        return
    content = match_feedback(text)
    if content is not None:
        media = []
        try:
            media, failures = await download_media_refs(ws, image_refs, cfg)
            if failures:
                content += f"\n（有 {failures} 张图片读取失败）"
            await handle_feedback(ws, cfg, event, content, media)
        except Exception as e:
            media_pipeline.cleanup_media(media)
            print(f"[错误] 处理反馈异常: {e}")
        return
    if storage.has_pending_bug_followup(
        str(event.get("user_id", "")), str(event.get("group_id", ""))
    ):
        media = []
        try:
            media, failures = await download_media_refs(ws, image_refs, cfg)
            followup_text = text
            if failures:
                followup_text += f"\n（有 {failures} 张图片读取失败）"
            followup_id = enqueue_bug_followup(
                event, followup_text.strip(), media
            )
            if followup_id:
                print(
                    f"[Bug补充#{followup_id}] 群{event.get('group_id')} "
                    f"{event.get('user_id')}: {text}"
                )
                return
            media_pipeline.cleanup_media(media)
        except Exception as exc:
            media_pipeline.cleanup_media(media)
            print(f"[错误] 处理 Bug 补充异常: {exc}")
    if is_at_self(event):
        try:
            await send_group_msg(
                ws,
                event.get("group_id"),
                at_message(
                    str(event.get("user_id", "")),
                    _ORDINARY_CHAT_DISABLED_REPLY,
                ),
            )
        except Exception as e:  # 单条消息出错不应拖垮整个连接
            print(f"[错误] 发送普通聊天关闭提示异常: {e}")


async def _dispatch_event(lock, client, cfg, event) -> None:
    """保持群消息到达顺序，同时让主接收循环继续分发 API 响应。"""
    async with lock:
        await on_event(client, cfg, event)


def _finish_event_task(tasks: set, task) -> None:
    tasks.discard(task)
    if task.cancelled():
        return
    error = task.exception()
    if error:
        print(f"[错误] 群消息任务异常: {error}")


async def _run_connected_session(
    ws,
    cfg: dict,
    verification_groups: set[str],
    whitelist_sync_config: qq_whitelist_sync.SyncConfig,
) -> None:
    """消费一条 OneBot 连接，并在取消时收束全部后台任务。"""
    client = OneBotClient(ws)
    notifier = None
    verifier = None
    whitelist_syncer = None
    event_tasks = set()
    event_lock = asyncio.Lock()
    if cfg.get("agent_enabled", False) or cfg.get(
        "chat_agent_enabled", False
    ):
        notifier = asyncio.create_task(notification_loop(client, cfg))
    if verification_groups:
        verifier = asyncio.create_task(
            member_verification_loop(client, cfg, event_lock)
        )
    if whitelist_sync_config.enabled:
        whitelist_syncer = asyncio.create_task(
            qq_whitelist_sync.run_sync_loop(client, whitelist_sync_config)
        )
    try:
        async for raw in ws:
            try:
                event = json.loads(raw)
            except json.JSONDecodeError:
                continue
            # API 动作响应交给等待中的事件任务，不能阻塞主接收循环。
            if "post_type" not in event:
                client.resolve_response(event)
                continue
            # 本机接收时间用于验证时限边界，不能由群消息正文伪造。
            event["_grandumi_received_at"] = int(time.time())
            task = asyncio.create_task(
                _dispatch_event(event_lock, client, cfg, event)
            )
            event_tasks.add(task)
            task.add_done_callback(
                lambda done: _finish_event_task(event_tasks, done)
            )
    finally:
        client.close()
        for task in event_tasks:
            task.cancel()
        if event_tasks:
            await asyncio.gather(*event_tasks, return_exceptions=True)
        background_tasks = tuple(
            task
            for task in (notifier, verifier, whitelist_syncer)
            if task is not None
        )
        for task in background_tasks:
            task.cancel()
        if background_tasks:
            await asyncio.gather(*background_tasks, return_exceptions=True)


async def _consume_until_stopped(ws, cfg, verification_groups, sync_config, stop_event):
    """让连接消费和停止事件竞争，确保 SIGTERM 能打断等待消息。"""
    consumer = asyncio.create_task(
        _run_connected_session(ws, cfg, verification_groups, sync_config)
    )
    stopper = asyncio.create_task(stop_event.wait())
    done, _ = await asyncio.wait(
        (consumer, stopper), return_when=asyncio.FIRST_COMPLETED
    )
    if stopper in done:
        consumer.cancel()
        await asyncio.gather(consumer, return_exceptions=True)
        return
    stopper.cancel()
    await asyncio.gather(stopper, return_exceptions=True)
    await consumer


async def _wait_for_reconnect_or_stop(stop_event: asyncio.Event, delay: float) -> bool:
    """返回 True 表示停止优先于重连等待发生。"""
    try:
        await asyncio.wait_for(stop_event.wait(), timeout=delay)
    except TimeoutError:
        return False
    return True


async def run(stop_event: asyncio.Event | None = None) -> None:
    cfg = load_config()
    storage.init_db()
    if stop_event is None:
        stop_event = asyncio.Event()
    whitelist_sync_config = qq_whitelist_sync.SyncConfig.from_bot_config(cfg)
    verification_groups = member_verification_groups(cfg)
    auto_approval_groups = group_add_auto_approval_groups(cfg)
    cancelled = storage.cancel_member_verifications_outside_groups(
        verification_groups
    )
    if cancelled:
        print(f"[新人验证] 因目标群配置变化取消了 {cancelled} 条遗留会话")
    url = build_ws_url(cfg)
    print(f"GrandUMI bug 反馈机器人启动,连接 {cfg['ws_url']} …")
    if cfg.get("new_member_verification_enabled", False) and not verification_groups:
        print(
            "[新人验证] 目标群列表为空或无效；为避免误踢，功能不会生效"
        )
    if cfg.get("group_add_auto_approval_enabled", False) and not auto_approval_groups:
        print("[加群审批] 已配置启用，但目标群列表为空或无效；为避免误审批，功能不会生效")
    elif auto_approval_groups:
        print(f"[加群审批] 已启用，目标群：{', '.join(sorted(auto_approval_groups))}")
    if whitelist_sync_config.enabled:
        print(
            "[QQ 白名单同步] 已启用："
            f"{whitelist_sync_config.group_name}（{whitelist_sync_config.group_id}），"
            "每个 Asia/Singapore 自然整点执行"
        )
    else:
        print("[QQ 白名单同步] 安全关闭")

    # 断线自动重连
    while not stop_event.is_set():
        try:
            async with ws_connect(
                url,
                max_size=None,
                open_timeout=10,
                close_timeout=5,
            ) as ws:
                print("已连接 NapCat,等待群消息…")
                await _consume_until_stopped(
                    ws,
                    cfg,
                    verification_groups,
                    whitelist_sync_config,
                    stop_event,
                )
        except (OSError, websockets.exceptions.WebSocketException) as e:
            if stop_event.is_set():
                break
            print(f"连接断开/失败: {e};5 秒后重连…")
            if await _wait_for_reconnect_or_stop(stop_event, 5):
                break
    print("已完成停止清理。")


async def main() -> None:
    """注册容器停止信号，并等待主循环完成清理。"""
    stop_event = asyncio.Event()
    loop = asyncio.get_running_loop()
    registered_signals = []

    def request_stop(signame: str) -> None:
        if not stop_event.is_set():
            print(f"收到 {signame}，开始安全停止……")
            stop_event.set()

    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            loop.add_signal_handler(sig, request_stop, sig.name)
        except (NotImplementedError, RuntimeError):
            continue
        registered_signals.append(sig)
    try:
        await run(stop_event)
    finally:
        for sig in registered_signals:
            loop.remove_signal_handler(sig)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n已退出。")
