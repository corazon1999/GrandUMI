# -*- coding: utf-8 -*-
"""从现有 NapCat / OneBot 实时读取固定双群白名单。"""

import asyncio
import hashlib
import json
import os
import re
import sys
import unicodedata
from datetime import datetime, timedelta, timezone
from time import monotonic
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit
from uuid import uuid4

from websockets.legacy.client import connect as ws_connect


TARGET_GROUPS = (
    {"group_id": "297542853", "expected_name": "GrandUMI测试群"},
    {"group_id": "524996856", "expected_name": None},
)
TARGET_GROUP_IDS = tuple(group["group_id"] for group in TARGET_GROUPS)
ACTION_SEQUENCE = (
    "get_group_info(no_cache=true)",
    "get_group_member_list(no_cache=true)",
    "get_group_info(no_cache=true)",
)
QQ_PATTERN = re.compile(r"^[0-9]{5,12}$", re.ASCII)
MAX_STABILITY_ATTEMPTS = 3
ACTION_TIMEOUT_SECONDS = 15.0
SINGAPORE_TIMEZONE = timezone(timedelta(hours=8), name="Asia/Singapore")


class ExportError(RuntimeError):
    """拒绝交付不可信的实时白名单。"""


class SnapshotChangedError(ExportError):
    """三段读取期间群人数发生变化，可以有限重试。"""


def _strict_identifier(value, label):
    if isinstance(value, bool):
        raise ExportError(f"OneBot {label}无效")
    if isinstance(value, int):
        candidate = str(value)
    elif isinstance(value, str):
        candidate = value
    else:
        raise ExportError(f"OneBot {label}无效")
    if not QQ_PATTERN.fullmatch(candidate):
        raise ExportError(f"OneBot {label}必须是 5–12 位纯数字")
    return candidate


def _strict_positive_count(value, label):
    if isinstance(value, bool):
        raise ExportError(f"OneBot {label}无效")
    if isinstance(value, int):
        parsed = value
    elif isinstance(value, str) and re.fullmatch(r"[1-9][0-9]*", value):
        parsed = int(value)
    else:
        raise ExportError(f"OneBot {label}无效")
    if parsed <= 0 or parsed > 10_000:
        raise ExportError(f"OneBot {label}超出安全范围")
    return parsed


def _strict_group_name(value, label):
    if not isinstance(value, str):
        raise ExportError(f"OneBot {label}无效")
    normalized = unicodedata.normalize("NFKC", value).strip()
    if not 1 <= len(normalized) <= 100 or any(
        unicodedata.category(char) == "Cc" for char in normalized
    ):
        raise ExportError(f"OneBot {label}无效")
    return normalized


def _validate_group_info(response, group, position):
    data = response.get("data") if isinstance(response, dict) else None
    if not isinstance(data, dict):
        raise ExportError(f"OneBot {position}群信息响应格式异常")
    group_id = group["group_id"]
    if _strict_identifier(data.get("group_id"), "群号") != group_id:
        raise ExportError("OneBot 群信息返回了非目标群号")
    group_name = _strict_group_name(data.get("group_name"), "群名")
    if group["expected_name"] and group_name != group["expected_name"]:
        raise ExportError("OneBot 群信息返回了非目标群名")
    return group_name, _strict_positive_count(data.get("member_count"), "群成员数")


def _validate_member_list(response, group_id):
    rows = response.get("data") if isinstance(response, dict) else None
    if not isinstance(rows, list):
        raise ExportError("OneBot 群成员列表响应格式异常")
    if not rows:
        raise ExportError("拒绝导出空群成员列表")
    if len(rows) > 10_000:
        raise ExportError("OneBot 群成员列表超出安全上限")

    members = []
    seen = set()
    for index, row in enumerate(rows, 1):
        if not isinstance(row, dict):
            raise ExportError(f"OneBot 第 {index} 条群成员记录格式异常")
        if _strict_identifier(row.get("group_id"), "成员所属群号") != group_id:
            raise ExportError("OneBot 群成员列表混入其他群的数据")
        qq = _strict_identifier(row.get("user_id"), f"第 {index} 条成员 QQ")
        if qq in seen:
            raise ExportError("OneBot 群成员列表包含重复 QQ")
        seen.add(qq)
        members.append(qq)
    return members


class OneBotSession:
    def __init__(self, websocket, action_timeout=ACTION_TIMEOUT_SECONDS):
        self.websocket = websocket
        self.action_timeout = float(action_timeout)

    async def call_action(self, action, params):
        echo = f"grandumi-live-export:{uuid4().hex}"
        request = {"action": action, "params": params, "echo": echo}
        await self.websocket.send(
            json.dumps(request, ensure_ascii=False, separators=(",", ":"))
        )
        deadline = monotonic() + self.action_timeout
        while True:
            remaining = deadline - monotonic()
            if remaining <= 0:
                raise asyncio.TimeoutError()
            raw = await asyncio.wait_for(self.websocket.recv(), timeout=remaining)
            try:
                response = json.loads(raw)
            except (TypeError, UnicodeError, json.JSONDecodeError) as exc:
                raise ExportError("OneBot 返回了无效 JSON") from exc
            if not isinstance(response, dict) or response.get("echo") != echo:
                continue
            if response.get("status") != "ok" or response.get("retcode", 0) != 0:
                raise ExportError(f"OneBot 动作 {action} 被明确拒绝")
            return response


def _snapshot_sha256(members):
    canonical = "".join(f"{qq}\n" for qq in sorted(set(members))).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def _normalize_excluded_bot_qqs(values):
    if values is None:
        return ()
    if not isinstance(values, (list, tuple, set, frozenset)):
        raise ExportError("机器人排除 QQ 配置必须是数组")
    return tuple(
        sorted({_strict_identifier(value, "机器人 QQ") for value in values})
    )


async def collect_snapshot(onebot, group, excluded_bot_qqs=()):
    group_id = group["group_id"]
    before_response = await onebot.call_action(
        "get_group_info",
        {"group_id": int(group_id), "no_cache": True},
    )
    before_name, before_count = _validate_group_info(
        before_response, group, "第一次"
    )

    members_response = await onebot.call_action(
        "get_group_member_list",
        {"group_id": int(group_id), "no_cache": True},
    )
    raw_members = _validate_member_list(members_response, group_id)

    after_response = await onebot.call_action(
        "get_group_info",
        {"group_id": int(group_id), "no_cache": True},
    )
    after_name, after_count = _validate_group_info(after_response, group, "第二次")

    if before_name != after_name:
        raise SnapshotChangedError("实时拉取期间群名发生变化，拒绝接受不稳定快照")
    if before_count != len(raw_members) or after_count != len(raw_members):
        raise SnapshotChangedError(
            "实时拉取期间群成员数量发生变化，拒绝接受不稳定快照"
        )
    excluded = set(_normalize_excluded_bot_qqs(excluded_bot_qqs))
    excluded_members = sorted(qq for qq in raw_members if qq in excluded)
    members = sorted(qq for qq in raw_members if qq not in excluded)
    if not members:
        raise ExportError("过滤机器人账号后群成员列表为空")
    return {
        "group_id": group_id,
        "group_name": before_name,
        "api_raw_count": len(raw_members),
        "eligible_count": len(members),
        "excluded_bot_count": len(excluded_members),
        "excluded_bot_qqs": excluded_members,
        "group_info_count_before": before_count,
        "group_info_count_after": after_count,
        "members": members,
    }


async def collect_stable_group_snapshot(
    onebot,
    group,
    excluded_bot_qqs=(),
    max_attempts=MAX_STABILITY_ATTEMPTS,
    retry_delay_seconds=1.0,
    sleep_fn=asyncio.sleep,
):
    last_error = None
    for attempt in range(1, max_attempts + 1):
        try:
            snapshot = await collect_snapshot(onebot, group, excluded_bot_qqs)
            snapshot["stability_attempt"] = attempt
            return snapshot
        except SnapshotChangedError as exc:
            last_error = exc
            if attempt < max_attempts:
                await sleep_fn(retry_delay_seconds)
    raise ExportError(
        f"群 {group['group_id']} 连续 {max_attempts} 次读取均处于变化中，未生成白名单"
    ) from last_error


async def collect_stable_snapshot(
    onebot,
    excluded_bot_qqs=(),
    max_attempts=MAX_STABILITY_ATTEMPTS,
    retry_delay_seconds=1.0,
    sleep_fn=asyncio.sleep,
):
    if not isinstance(max_attempts, int) or not 1 <= max_attempts <= 5:
        raise ExportError("稳定性重试次数配置无效")
    excluded_bot_qqs = _normalize_excluded_bot_qqs(excluded_bot_qqs)
    snapshots = []
    for group in TARGET_GROUPS:
        snapshots.append(
            await collect_stable_group_snapshot(
                onebot,
                group,
                excluded_bot_qqs,
                max_attempts,
                retry_delay_seconds,
                sleep_fn,
            )
        )

    members = sorted(
        {
            member
            for snapshot in snapshots
            for member in snapshot["members"]
        }
    )
    eligible_count = sum(snapshot["eligible_count"] for snapshot in snapshots)
    original_count = sum(snapshot["api_raw_count"] for snapshot in snapshots)
    excluded_bot_count = sum(
        snapshot["excluded_bot_count"] for snapshot in snapshots
    )
    source_groups = [dict(snapshot) for snapshot in snapshots]
    fetched_at = datetime.now(SINGAPORE_TIMEZONE).isoformat(timespec="milliseconds")
    return {
        "source": {
            "protocol": "OneBot 11",
            "actions": list(ACTION_SEQUENCE),
            "group_ids": list(TARGET_GROUP_IDS),
            "groups": source_groups,
            "excluded_bot_qqs": list(excluded_bot_qqs),
            "fetched_at": fetched_at,
        },
        "validation": {
            "original_count": original_count,
            "eligible_count": eligible_count,
            "unique_count": len(members),
            "duplicate_count": eligible_count - len(members),
            "excluded_bot_count": excluded_bot_count,
            "invalid_count": 0,
            "cross_group_count": 0,
            "group_ids_seen": list(TARGET_GROUP_IDS),
        },
        "snapshot_sha256": _snapshot_sha256(members),
        "members": members,
    }


def _build_websocket_url(config):
    raw_url = config.get("ws_url") if isinstance(config, dict) else None
    if not isinstance(raw_url, str):
        raise ExportError("机器人配置缺少 OneBot WebSocket 地址")
    parsed = urlsplit(raw_url)
    if (
        parsed.scheme not in {"ws", "wss"}
        or not parsed.hostname
        or parsed.username
        or parsed.password
        or parsed.fragment
    ):
        raise ExportError("机器人配置中的 OneBot WebSocket 地址无效")
    query = parse_qsl(parsed.query, keep_blank_values=True)
    if any(key == "access_token" for key, _ in query):
        raise ExportError("OneBot WebSocket 地址不得内嵌访问令牌")
    token = config.get("access_token") or ""
    if not isinstance(token, str):
        raise ExportError("机器人 OneBot 访问令牌配置格式无效")
    if token:
        query.append(("access_token", token))
    return urlunsplit(
        (parsed.scheme, parsed.netloc, parsed.path, urlencode(query), "")
    )


def _load_export_config():
    config_path = os.environ.get("BUG_BOT_CONFIG_PATH", "/run/secrets/bot_config")
    try:
        with open(config_path, "r", encoding="utf-8") as handle:
            config = json.load(handle)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ExportError("无法读取现有机器人配置") from exc
    excluded = set()
    configured_excluded = config.get("qq_whitelist_sync_excluded_qqs") or []
    if not isinstance(configured_excluded, list):
        raise ExportError("qq_whitelist_sync_excluded_qqs 必须是数组")
    excluded.update(_normalize_excluded_bot_qqs(configured_excluded))
    connections = config.get("assistant_connections") or []
    if not isinstance(connections, list):
        raise ExportError("assistant_connections 必须是数组")
    for connection in connections:
        if not isinstance(connection, dict):
            continue
        expected_self_id = connection.get("expected_self_id")
        if expected_self_id not in (None, ""):
            excluded.add(_strict_identifier(expected_self_id, "助理机器人 QQ"))
    legacy_self_id = config.get("expected_self_id")
    if legacy_self_id not in (None, ""):
        excluded.add(_strict_identifier(legacy_self_id, "助理机器人 QQ"))
    return _build_websocket_url(config), tuple(sorted(excluded))


def _load_websocket_url():
    return _load_export_config()[0]


async def export_live_whitelist():
    websocket_url, excluded_bot_qqs = _load_export_config()
    try:
        async with ws_connect(
            websocket_url,
            open_timeout=10,
            close_timeout=5,
            ping_interval=10,
            ping_timeout=10,
            max_size=4 * 1024 * 1024,
        ) as websocket:
            return await collect_stable_snapshot(
                OneBotSession(websocket), excluded_bot_qqs
            )
    except ExportError:
        raise
    except asyncio.TimeoutError as exc:
        raise ExportError("连接或调用 NapCat / OneBot 超时") from exc
    except Exception as exc:
        raise ExportError(
            "无法使用现有容器中的 NapCat / OneBot，请确认服务、QQ 登录和网络均正常"
        ) from exc


def main():
    try:
        payload = asyncio.run(export_live_whitelist())
    except KeyboardInterrupt:
        print("导出失败：操作已取消。", file=sys.stderr)
        return 130
    except ExportError as exc:
        print(f"导出失败：{exc}", file=sys.stderr)
        return 1
    except Exception:
        print("导出失败：远端导出器遇到未预期错误。", file=sys.stderr)
        return 1
    sys.stdout.write(json.dumps(payload, ensure_ascii=True, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
