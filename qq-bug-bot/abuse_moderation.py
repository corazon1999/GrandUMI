# -*- coding: utf-8 -*-
"""群辱骂治理的保守、纯本地判定器。

只读取 OneBot 顶层结构化消息段。引用、合并转发、图片及其内部文字都不会
进入判定；玩家输入也不会作为指令或正则表达式执行。
"""

from dataclasses import dataclass
import hashlib
import re
import unicodedata


MAX_TOP_LEVEL_TEXT_LENGTH = 1000
_QQ_RE = re.compile(r"^[1-9]\d{4,11}$")

# 词表刻意保持短而明确。普通的“菜”“坑”“差”“垃圾代码”等负面表达不在这里。
_SEVERE_INSULT = (
    r"(?:傻逼|煞笔|沙比|脑残|废物|狗东西|畜生|牲口|贱人|婊子|"
    r"杂种|狗杂种|人渣|死妈玩意)"
)
_SEVERE_INSULT_RE = re.compile(_SEVERE_INSULT)

# 明确的人身指向。中间只允许常见系词和程度词，避免把“你看看这个词”等
# 技术或讨论语句误识别为攻击。
_DIRECT_PERSONAL_INSULT_RE = re.compile(
    rf"(?:你|你们)(?:他妈的?)?"
    rf"(?:(?:可?真(?:是)?|简直(?:就是|是)?|就是|是|也太|这么|那么|"
    rf"怎么(?:这么|那么)|纯纯(?:是)?|像))?"
    rf"(?:一?个|这(?:一)?个|那(?:一)?个)?{_SEVERE_INSULT}"
    rf"(?=$|啊|吧|呀|呢|了|滚|闭嘴|东西|玩意)"
)
_REVERSED_PERSONAL_INSULT_RE = re.compile(
    rf"{_SEVERE_INSULT}(?:玩意|东西)?(?:啊|吧|呀|呢|了)?(?:你|你们)$"
)
_FAMILY_ABUSE_RE = re.compile(
    r"(?:(?:操|艹|草|肏|日|干)(?:死)?你(?:妈|娘|全家)|"
    r"你妈(?:必死|炸了|怎么还不死))"
)
_COMPACT_CURSE_RE = re.compile(
    r"^(?:草泥马|nmsl|cnm|cnmb)(?:啊|吧|呀|呢)?$", re.IGNORECASE
)
_DEATH_WISH_RE = re.compile(
    r"(?:(?:你|你们)(?:给我)?去死(?!循环|锁)(?:吧)?|"
    r"去死(?!循环|锁)(?:吧)?(?:你|你们)|"
    r"你全家(?:都)?(?:去死|死光|死完))"
)
_AT_STANDALONE_INSULT_RE = re.compile(
    rf"^{_SEVERE_INSULT}(?:玩意|东西)?(?:啊|吧|呀|呢)?$"
)

# 出现在这些明确语境时宁可漏报，也不自动处罚词汇讨论、示例、引用和表演。
_NON_ATTACK_CONTEXT_RE = re.compile(
    r"(?:测试|测一下|示例|举例|例子|正则|关键词|敏感词|词库|过滤|屏蔽|"
    r"检测|审核|辱骂(?:词|语言)?|脏话(?:词)?|攻击性语言|这个词|该词|词语|"
    r"台词|剧本|歌词|配音|角色扮演|引用|原话|复述|转述|他说|她说|有人说|"
    r"怎么处理|如何处理|名为|叫做|废物利用|死循环|死锁|脑残粉|牲口棚)"
)
_NEGATED_OR_DISCOURAGED_INSULT_RE = re.compile(
    rf"(?:不是|并非|不算|别叫|不要叫|别说|不要说|别骂|不要骂|不该骂|"
    rf"不能骂).{{0,6}}{_SEVERE_INSULT}"
)
_QUOTE_OR_CODE_MARK_RE = re.compile(r"[“”‘’「」『』《》\"'`]")


@dataclass(frozen=True)
class AbuseDecision:
    """不保存辱骂原文，只返回可审计规则与正文摘要。"""

    rule_id: str
    content_sha256: str


def _compact_text(text: str) -> str:
    normalized = unicodedata.normalize("NFKC", text).lower()
    # \w 在 Unicode 模式下保留中文、字母和数字；同时显式移除下划线。
    return re.sub(r"[\W_]+", "", normalized, flags=re.UNICODE)


def classify_group_message(event: dict) -> AbuseDecision | None:
    """保守判定一条结构化顶层群消息是否为明确人身攻击。"""
    if (
        not isinstance(event, dict)
        or event.get("post_type") != "message"
        or event.get("message_type") != "group"
    ):
        return None
    message = event.get("message")
    if not isinstance(message, list) or len(message) > 128:
        return None

    self_id = str(event.get("self_id") or "").strip()
    text_parts: list[str] = []
    text_length = 0
    has_other_member_at = False
    for segment in message:
        if not isinstance(segment, dict):
            return None
        data = segment.get("data")
        if not isinstance(data, dict):
            return None
        segment_type = segment.get("type")
        if segment_type == "text":
            value = data.get("text", "")
            if not isinstance(value, str):
                return None
            text_parts.append(value)
            text_length += len(value)
            if text_length > MAX_TOP_LEVEL_TEXT_LENGTH:
                return None
        elif segment_type == "at":
            target = str(data.get("qq") or "").strip()
            if target not in ("", "all", self_id) and _QQ_RE.fullmatch(target):
                has_other_member_at = True
        # reply、forward、image 等段只作为不可采信的外部内容忽略。

    text = "".join(text_parts)
    if not text or len(text) > MAX_TOP_LEVEL_TEXT_LENGTH:
        return None
    normalized = unicodedata.normalize("NFKC", text)
    if _QUOTE_OR_CODE_MARK_RE.search(normalized):
        return None
    compact = _compact_text(normalized)
    if not compact or _NON_ATTACK_CONTEXT_RE.search(compact):
        return None
    if _NEGATED_OR_DISCOURAGED_INSULT_RE.search(compact):
        return None

    rule_id = None
    if _FAMILY_ABUSE_RE.search(compact) or _COMPACT_CURSE_RE.fullmatch(compact):
        rule_id = "severe_family_abuse"
    elif _DEATH_WISH_RE.search(compact):
        rule_id = "direct_death_wish"
    elif (
        _DIRECT_PERSONAL_INSULT_RE.search(compact)
        or _REVERSED_PERSONAL_INSULT_RE.search(compact)
    ):
        rule_id = "direct_personal_insult"
    elif (
        has_other_member_at
        and _AT_STANDALONE_INSULT_RE.fullmatch(compact)
    ):
        rule_id = "at_personal_insult"
    if rule_id is None:
        return None

    return AbuseDecision(
        rule_id=rule_id,
        content_sha256=hashlib.sha256(text.encode("utf-8")).hexdigest(),
    )
