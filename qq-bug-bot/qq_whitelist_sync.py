# -*- coding: utf-8 -*-
"""把两个固定 QQ 群的实时成员并集同步到游戏准入白名单。"""

import asyncio
import hashlib
import json
import os
import re
import time
import unicodedata
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from urllib.parse import urlsplit
from uuid import uuid4

from websockets.exceptions import WebSocketException

import storage


QQ_PATTERN = re.compile(r"^[0-9]{5,12}$", re.ASCII)
SUCCESS_PHRASE = "白名单已更新"
BUSINESS_TIMEZONE = timezone(timedelta(hours=8), name="Asia/Singapore")
FIXED_SYNC_GROUP_IDS = ("297542853", "524996856")
SYNC_INTERVAL_HOURS = 2
SOURCE_SET_KEY = "+".join(FIXED_SYNC_GROUP_IDS)
PROCESS_RUN_ID = str(uuid4())


class SyncConfigurationError(RuntimeError):
    pass


class SyncRejectedError(RuntimeError):
    """服务明确拒绝请求；相同请求重发不会成功。"""


class SyncTransportError(RuntimeError):
    """网络或服务端瞬时错误；可在当前时隙窗口内有限重试。"""


@dataclass(frozen=True)
class SyncGroup:
    group_id: str
    expected_name: str | None = None


@dataclass(frozen=True)
class SyncConfig:
    enabled: bool
    groups: tuple[SyncGroup, ...]
    timezone_name: str
    endpoint: str
    secret: str
    excluded_member_ids: tuple[str, ...] = ()
    interval_hours: int = SYNC_INTERVAL_HOURS
    minimum_member_count: int = 100
    maximum_shrink_percent: int = 25
    maximum_delay_seconds: int = 600
    update_retry_delays: tuple[float, ...] = (0, 5, 20)
    notification_retry_delays: tuple[float, ...] = (0, 5, 20)
    http_timeout_seconds: float = 20

    @property
    def group_ids(self) -> tuple[str, ...]:
        return tuple(group.group_id for group in self.groups)

    @property
    def source_set_key(self) -> str:
        return "+".join(self.group_ids)

    # 兼容仍会读取旧单群展示字段的外围代码；同步协议不再使用它们冒充数据源。
    @property
    def group_id(self) -> str:
        return self.groups[0].group_id if self.groups else ""

    @property
    def group_name(self) -> str:
        if not self.groups:
            return ""
        return self.groups[0].expected_name or "实时群名"

    @classmethod
    def from_bot_config(cls, cfg: dict):
        enabled = cfg.get("qq_whitelist_sync_enabled", False) is True
        if not enabled:
            return cls(False, (), "Asia/Singapore", "", "")

        configured_ids = cfg.get("qq_whitelist_sync_group_ids")
        if configured_ids is None:
            legacy_id = _normalize_qq(cfg.get("qq_whitelist_sync_group_id"))
            if legacy_id != FIXED_SYNC_GROUP_IDS[0]:
                raise SyncConfigurationError(
                    "旧版 QQ 白名单同步配置不是固定原群，拒绝自动扩展数据源"
                )
            group_ids = FIXED_SYNC_GROUP_IDS
        else:
            if not isinstance(configured_ids, list):
                raise SyncConfigurationError(
                    "qq_whitelist_sync_group_ids 必须是群号数组"
                )
            group_ids = tuple(_normalize_qq(value) for value in configured_ids)
            if len(set(group_ids)) != len(group_ids):
                raise SyncConfigurationError("QQ 白名单同步群号数组包含重复值")
            if group_ids != FIXED_SYNC_GROUP_IDS:
                raise SyncConfigurationError(
                    "QQ 白名单同步必须按固定顺序统计群 297542853 与 524996856"
                )

        legacy_name = _normalize_group_name(
            cfg.get("qq_whitelist_sync_group_name")
        )
        groups = (
            SyncGroup(group_ids[0], legacy_name),
            SyncGroup(group_ids[1], None),
        )
        timezone_name = str(
            cfg.get("qq_whitelist_sync_timezone") or "Asia/Singapore"
        ).strip()
        if timezone_name != "Asia/Singapore":
            raise SyncConfigurationError(
                "QQ 白名单同步时区必须是 Asia/Singapore（UTC+8）"
            )
        interval_hours = _bounded_int(
            cfg,
            "qq_whitelist_sync_interval_hours",
            SYNC_INTERVAL_HOURS,
            SYNC_INTERVAL_HOURS,
            SYNC_INTERVAL_HOURS,
        )
        endpoint = _validate_endpoint(cfg.get("qq_whitelist_sync_endpoint"))
        secret_env = str(
            cfg.get("qq_whitelist_sync_secret_env")
            or "GRANDUMI_QQ_WHITELIST_SYNC_SECRET"
        ).strip()
        if not re.fullmatch(r"[A-Z][A-Z0-9_]{2,100}", secret_env):
            raise SyncConfigurationError("QQ 白名单同步密钥环境变量名无效")
        secret = os.environ.get(secret_env, "")
        if not 32 <= len(secret.encode("utf-8")) <= 512:
            raise SyncConfigurationError(
                f"启用 QQ 白名单同步时，{secret_env} 必须提供 32–512 字节随机密钥"
            )
        allowed_groups = {
            str(value).strip() for value in (cfg.get("allowed_groups") or [])
        }
        if any(group_id not in allowed_groups for group_id in group_ids):
            raise SyncConfigurationError(
                "两个 QQ 白名单同步群都必须明确列入 allowed_groups"
            )

        excluded = set()
        explicit_excluded = cfg.get("qq_whitelist_sync_excluded_qqs") or []
        if not isinstance(explicit_excluded, list):
            raise SyncConfigurationError(
                "qq_whitelist_sync_excluded_qqs 必须是 QQ 数组"
            )
        for value in explicit_excluded:
            excluded.add(_normalize_qq(value))
        connections = cfg.get("assistant_connections") or []
        if not isinstance(connections, list):
            raise SyncConfigurationError("assistant_connections 必须是数组")
        for connection in connections:
            if not isinstance(connection, dict):
                continue
            value = connection.get("expected_self_id")
            if value not in (None, ""):
                excluded.add(_normalize_qq(value))
        legacy_self_id = cfg.get("expected_self_id")
        if legacy_self_id not in (None, ""):
            excluded.add(_normalize_qq(legacy_self_id))

        return cls(
            True,
            groups,
            timezone_name,
            endpoint,
            secret,
            tuple(sorted(excluded)),
            interval_hours,
            _bounded_int(cfg, "qq_whitelist_sync_min_members", 100, 1, 10000),
            _bounded_int(
                cfg, "qq_whitelist_sync_max_shrink_percent", 25, 0, 90
            ),
            _bounded_int(
                cfg, "qq_whitelist_sync_max_delay_seconds", 600, 30, 1800
            ),
            http_timeout_seconds=float(
                _bounded_int(
                    cfg, "qq_whitelist_sync_http_timeout_seconds", 20, 5, 60
                )
            ),
        )


def _bounded_int(cfg, key, default, minimum, maximum):
    try:
        value = int(cfg.get(key, default))
    except (TypeError, ValueError) as exc:
        raise SyncConfigurationError(f"{key} 必须是整数") from exc
    if not minimum <= value <= maximum:
        if minimum == maximum:
            raise SyncConfigurationError(f"{key} 必须是 {minimum}")
        raise SyncConfigurationError(f"{key} 必须是 {minimum}–{maximum}")
    return value


def _normalize_qq(value) -> str:
    normalized = unicodedata.normalize("NFKC", str(value or "")).strip()
    if not QQ_PATTERN.fullmatch(normalized):
        raise SyncConfigurationError("QQ 白名单同步群号或成员 QQ 格式无效")
    return normalized


def _normalize_group_name(value) -> str:
    normalized = unicodedata.normalize("NFKC", str(value or "")).strip()
    if not 1 <= len(normalized) <= 100 or any(
        unicodedata.category(char) == "Cc" for char in normalized
    ):
        raise SyncConfigurationError("QQ 白名单同步群名格式无效")
    return normalized


def _validate_endpoint(value) -> str:
    endpoint = str(value or "").strip().rstrip("/")
    parsed = urlsplit(endpoint)
    if (
        parsed.username
        or parsed.password
        or parsed.query
        or parsed.fragment
        or parsed.path != "/internal/qq-whitelist/sync"
    ):
        raise SyncConfigurationError("QQ 白名单同步内部端点格式无效")
    loopback = parsed.hostname in {"127.0.0.1", "::1", "localhost"}
    if parsed.scheme != "https" and not (parsed.scheme == "http" and loopback):
        raise SyncConfigurationError("QQ 白名单同步跨主机端点必须使用 HTTPS")
    if not parsed.hostname:
        raise SyncConfigurationError("QQ 白名单同步内部端点缺少主机名")
    return endpoint


def scheduled_slot_epoch(
    now: datetime,
    timezone_name="Asia/Singapore",
    interval_hours=SYNC_INTERVAL_HOURS,
) -> int:
    """返回 now 所在的 UTC+8 两小时确定性计划槽。"""
    _validate_schedule_config(timezone_name, interval_hours)
    local = now.astimezone(BUSINESS_TIMEZONE)
    slot_hour = local.hour - local.hour % interval_hours
    return int(
        local.replace(
            hour=slot_hour, minute=0, second=0, microsecond=0
        ).timestamp()
    )


def next_slot(
    now: datetime,
    timezone_name="Asia/Singapore",
    interval_hours=SYNC_INTERVAL_HOURS,
) -> datetime:
    """按 UTC+8 墙上时钟重算下一个 00/02/... 时隙，不累计漂移。"""
    current = scheduled_slot_epoch(now, timezone_name, interval_hours)
    return datetime.fromtimestamp(
        current + interval_hours * 3600, tz=BUSINESS_TIMEZONE
    )


def scheduled_midnight_epoch(
    now: datetime, timezone_name="Asia/Singapore"
) -> int:
    """保留给旧调用者的自然日零点计算；新调度使用 scheduled_slot_epoch。"""
    _validate_schedule_config(timezone_name, SYNC_INTERVAL_HOURS)
    local = now.astimezone(BUSINESS_TIMEZONE)
    return int(
        local.replace(hour=0, minute=0, second=0, microsecond=0).timestamp()
    )


def next_midnight(now: datetime, timezone_name="Asia/Singapore") -> datetime:
    _validate_schedule_config(timezone_name, SYNC_INTERVAL_HOURS)
    local = now.astimezone(BUSINESS_TIMEZONE)
    return local.replace(
        hour=0, minute=0, second=0, microsecond=0
    ) + timedelta(days=1)


def current_hour_epoch(now: datetime, timezone_name="Asia/Singapore") -> int:
    return scheduled_slot_epoch(now, timezone_name)


def next_hour(now: datetime, timezone_name="Asia/Singapore") -> datetime:
    return next_slot(now, timezone_name)


def _validate_schedule_config(timezone_name, interval_hours):
    if timezone_name != "Asia/Singapore":
        raise SyncConfigurationError(
            "QQ 白名单同步时区必须是 Asia/Singapore（UTC+8）"
        )
    if int(interval_hours) != SYNC_INTERVAL_HOURS:
        raise SyncConfigurationError("QQ 白名单同步间隔必须是 2 小时")


def _is_two_hour_slot(scheduled_hour: int) -> bool:
    try:
        scheduled = datetime.fromtimestamp(
            int(scheduled_hour), tz=BUSINESS_TIMEZONE
        )
    except (OverflowError, OSError, TypeError, ValueError):
        return False
    return (
        scheduled.hour % SYNC_INTERVAL_HOURS == 0
        and scheduled.minute == 0
        and scheduled.second == 0
        and scheduled.microsecond == 0
    )


def _is_within_schedule_window(
    scheduled_hour: int, now: int, maximum_delay_seconds: int
) -> bool:
    return (
        _is_two_hour_slot(scheduled_hour)
        and int(scheduled_hour) <= int(now)
        and int(now) - int(scheduled_hour) <= int(maximum_delay_seconds)
    )


def _canonical_group_ids(group_ids) -> tuple[str, ...]:
    normalized = tuple(_normalize_qq(value) for value in group_ids)
    if len(normalized) != 2 or len(set(normalized)) != 2:
        raise SyncConfigurationError("QQ 白名单同步必须包含两个不同群号")
    return tuple(sorted(normalized))


def snapshot_sha256(members) -> str:
    normalized = sorted({_normalize_qq(value) for value in members})
    canonical = "".join(f"{value}\n" for value in normalized).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def build_operation_key(group_ids, scheduled_hour: int, members=None) -> str:
    """v2 键绑定双群、时隙和最终去重快照；保留旧单群构造兼容测试工具。"""
    if isinstance(group_ids, str) and members is None:
        return f"qq-whitelist:{_normalize_qq(group_ids)}:{int(scheduled_hour)}"
    canonical_groups = _canonical_group_ids(group_ids)
    if canonical_groups != FIXED_SYNC_GROUP_IDS:
        raise SyncConfigurationError("QQ 白名单同步数据源群集合无效")
    if members is None:
        raise SyncConfigurationError("v2 QQ 白名单同步键缺少最终去重快照")
    digest = snapshot_sha256(members)
    return (
        f"qq-whitelist:v2:{'+'.join(canonical_groups)}:"
        f"{int(scheduled_hour)}:{digest}"
    )


def build_slot_key(group_ids, scheduled_hour: int) -> str:
    groups = _canonical_group_ids(group_ids)
    return f"qq-whitelist-slot:v2:{'+'.join(groups)}:{int(scheduled_hour)}"


def build_capture_failure_key(group_ids, scheduled_hour: int) -> str:
    groups = _canonical_group_ids(group_ids)
    return (
        f"qq-whitelist:v2:{'+'.join(groups)}:"
        f"{int(scheduled_hour)}:capture-failed"
    )


async def get_realtime_group_snapshot(onebot, group, excluded_member_ids=()):
    """无缓存读取一个群；所有原始记录先严格核验，再过滤已知机器人账号。"""
    if isinstance(group, SyncConfig):
        config = group
        if not config.groups:
            raise SyncRejectedError("QQ 白名单同步缺少目标群")
        group = config.groups[0]
        excluded_member_ids = config.excluded_member_ids
    group_id = group.group_id
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
    rows = members_response.get("data") if isinstance(members_response, dict) else None
    if not isinstance(rows, list):
        raise SyncRejectedError("OneBot 群成员列表响应格式异常")
    raw_members = []
    seen = set()
    for index, item in enumerate(rows, 1):
        if not isinstance(item, dict):
            raise SyncRejectedError(
                f"OneBot 第 {index} 条群成员记录格式异常"
            )
        if str(item.get("group_id") or "") != group_id:
            raise SyncRejectedError("OneBot 群成员列表混入其他群的数据")
        try:
            qq = _normalize_qq(item.get("user_id"))
        except SyncConfigurationError as exc:
            raise SyncRejectedError(
                f"OneBot 第 {index} 条群成员 QQ 无效"
            ) from exc
        if qq in seen:
            raise SyncRejectedError("OneBot 单群成员列表包含重复 QQ")
        seen.add(qq)
        raw_members.append(qq)
    after_response = await onebot.call_action(
        "get_group_info",
        {"group_id": int(group_id), "no_cache": True},
    )
    after_name, after_count = _validate_group_info(
        after_response, group, "第二次"
    )
    if not raw_members:
        raise SyncRejectedError("拒绝用空群成员列表覆盖白名单")
    if before_name != after_name:
        raise SyncRejectedError("OneBot 采样期间群名发生变化")
    if before_count != len(raw_members) or after_count != len(raw_members):
        raise SyncRejectedError("OneBot 群信息与群成员列表的人数不一致")
    excluded = set(excluded_member_ids)
    members = sorted(qq for qq in raw_members if qq not in excluded)
    if not members:
        raise SyncRejectedError("过滤机器人账号后群成员列表为空")
    return {
        "groupId": group_id,
        "groupName": before_name,
        "reportedMemberCount": len(raw_members),
        "eligibleMemberCount": len(members),
        "excludedMemberCount": len(raw_members) - len(members),
        "members": members,
    }


def _validate_group_info(response, group: SyncGroup, position: str):
    info = response.get("data") if isinstance(response, dict) else None
    if not isinstance(info, dict):
        raise SyncRejectedError(f"OneBot {position}群信息响应格式异常")
    if str(info.get("group_id") or "") != group.group_id:
        raise SyncRejectedError("OneBot 群信息返回了错误群号")
    try:
        returned_name = _normalize_group_name(info.get("group_name"))
    except SyncConfigurationError as exc:
        raise SyncRejectedError("OneBot 群信息返回的群名无效") from exc
    if group.expected_name and returned_name != group.expected_name:
        raise SyncRejectedError("OneBot 群信息返回了错误群名")
    reported_count = _strict_positive_int(info.get("member_count"), "群成员数")
    return returned_name, reported_count


async def get_combined_group_snapshot(onebot, config: SyncConfig, previous_count=None):
    """两个群全部成功后才构造并集；任一群失败都不会返回部分快照。"""
    snapshots = []
    for group in config.groups:
        snapshots.append(
            await get_realtime_group_snapshot(
                onebot, group, config.excluded_member_ids
            )
        )
    members = sorted(
        {
            member
            for snapshot in snapshots
            for member in snapshot["members"]
        }
    )
    if len(members) < config.minimum_member_count:
        raise SyncRejectedError("双群去重成员数量低于配置的安全下限")
    if previous_count and len(members) * 100 < previous_count * (
        100 - config.maximum_shrink_percent
    ):
        raise SyncRejectedError("双群去重成员数量相较上次成功同步显著缩水")
    source_groups = [
        {
            "groupId": snapshot["groupId"],
            "groupName": snapshot["groupName"],
            "reportedMemberCount": snapshot["reportedMemberCount"],
            "eligibleMemberCount": snapshot["eligibleMemberCount"],
            "excludedMemberCount": snapshot["excludedMemberCount"],
        }
        for snapshot in snapshots
    ]
    return {
        "sourceGroups": source_groups,
        "reportedMemberCount": len(members),
        "members": members,
        "snapshotSha256": snapshot_sha256(members),
    }


def _strict_positive_int(value, label):
    if isinstance(value, bool):
        raise SyncRejectedError(f"OneBot {label}无效")
    try:
        parsed = int(value)
    except (TypeError, ValueError) as exc:
        raise SyncRejectedError(f"OneBot {label}无效") from exc
    if str(value).strip() != str(parsed) or parsed <= 0:
        raise SyncRejectedError(f"OneBot {label}无效")
    return parsed


class GameWhitelistClient:
    def __init__(self, config: SyncConfig):
        self.endpoint = config.endpoint
        self.secret = config.secret
        self.timeout = config.http_timeout_seconds

    async def synchronize(self, payload):
        return await asyncio.to_thread(self._post, self.endpoint, payload, False)

    async def status(self, operation_key, client_instance_id):
        return await asyncio.to_thread(
            self._post,
            self.endpoint + "/status",
            {
                "operationKey": operation_key,
                "clientInstanceId": client_instance_id,
            },
            True,
        )

    async def acknowledge(self, operation_key, client_instance_id, version):
        return await asyncio.to_thread(
            self._post,
            self.endpoint + "/notification-ack",
            {
                "operationKey": operation_key,
                "clientInstanceId": client_instance_id,
                "version": int(version),
            },
            False,
        )

    async def report_failure(self, payload):
        return await asyncio.to_thread(
            self._post, self.endpoint + "/failure", payload, False
        )

    def _post(self, url, payload, allow_not_found):
        encoded = json.dumps(
            payload, ensure_ascii=False, separators=(",", ":")
        ).encode("utf-8")
        request = urllib.request.Request(
            url,
            data=encoded,
            method="POST",
            headers={
                "Authorization": f"Bearer {self.secret}",
                "Content-Type": "application/json; charset=utf-8",
                "Accept": "application/json",
                "User-Agent": "GrandUMI-QQ-Whitelist-Sync/2",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                raw = response.read(65537)
                if len(raw) > 65536:
                    raise SyncTransportError("游戏服务响应体过大")
                data = json.loads(raw.decode("utf-8"))
                if not isinstance(data, dict):
                    raise SyncTransportError("游戏服务响应格式异常")
                return data
        except urllib.error.HTTPError as exc:
            if allow_not_found and exc.code == 404:
                return None
            detail = _read_http_error(exc)
            if exc.code >= 500:
                raise SyncTransportError(
                    f"游戏服务暂时失败（HTTP {exc.code}）：{detail}"
                ) from exc
            raise SyncRejectedError(
                f"游戏服务拒绝同步（HTTP {exc.code}）：{detail}"
            ) from exc
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            raise SyncTransportError(
                f"无法连接游戏白名单内部端点：{exc}"
            ) from exc
        except (UnicodeError, json.JSONDecodeError) as exc:
            raise SyncTransportError("游戏服务响应不是有效 JSON") from exc


def _read_http_error(error):
    try:
        body = error.read(4096).decode("utf-8", errors="replace")
        data = json.loads(body)
        if isinstance(data, dict) and isinstance(data.get("error"), str):
            return data["error"][:500]
    except Exception:
        pass
    return "未提供错误详情"


def _validate_game_response(
    response, operation_key, config, scheduled_hour, expected_member_count
):
    if not isinstance(response, dict):
        raise SyncTransportError("游戏服务同步响应格式异常")
    if response.get("protocolVersion") != 2:
        raise SyncTransportError("游戏服务返回了错误协议版本")
    if response.get("operationKey") != operation_key:
        raise SyncTransportError("游戏服务返回了错误幂等键")
    returned_groups = response.get("sourceGroupIds")
    if not isinstance(returned_groups, list) or tuple(
        str(value) for value in returned_groups
    ) != config.group_ids:
        raise SyncTransportError("游戏服务返回了错误双群数据源")
    returned_hour = response.get("scheduledHour")
    if (
        isinstance(returned_hour, bool)
        or not isinstance(returned_hour, int)
        or returned_hour != int(scheduled_hour)
    ):
        raise SyncTransportError("游戏服务返回了错误计划时间")
    if response.get("groupId") != config.group_ids[0]:
        raise SyncTransportError("游戏服务返回了错误主数据源群号")
    if response.get("groupName") != config.source_set_key:
        raise SyncTransportError("游戏服务返回了错误双群数据源标识")
    version = _strict_game_positive_int(response.get("version"), "版本")
    member_count = _strict_game_positive_int(
        response.get("memberCount"), "成员数"
    )
    if member_count != int(expected_member_count):
        raise SyncTransportError("游戏服务返回的成员数与本地完整快照不一致")
    owner = response.get("notificationOwner")
    if not isinstance(owner, bool):
        raise SyncTransportError("游戏服务返回的通知所有者标记无效")
    acknowledged_at = response.get("notificationAcknowledgedAt")
    if acknowledged_at is not None and (
        isinstance(acknowledged_at, bool)
        or not isinstance(acknowledged_at, int)
        or acknowledged_at <= 0
    ):
        raise SyncTransportError("游戏服务返回的通知确认时间无效")
    if owner and acknowledged_at is not None:
        raise SyncTransportError("游戏服务返回了互相冲突的通知状态")
    return version, member_count, owner


def _strict_game_positive_int(value, label):
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise SyncTransportError(f"游戏服务返回的{label}无效")
    return value


async def execute_sync_hour(
    onebot,
    config: SyncConfig,
    scheduled_hour: int,
    game_client=None,
    now_fn=time.time,
    sleep_fn=asyncio.sleep,
):
    """执行或恢复一个已到达的两小时时隙。"""
    now = int(now_fn())
    if not _is_within_schedule_window(
        scheduled_hour, now, config.maximum_delay_seconds
    ):
        return {"status": "stale"}
    if config.group_ids != FIXED_SYNC_GROUP_IDS:
        raise SyncConfigurationError("QQ 白名单同步数据源不是固定双群")
    game_client = game_client or GameWhitelistClient(config)
    instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(now=now)
    row = storage.prepare_qq_whitelist_sync_slot(
        build_slot_key(config.group_ids, scheduled_hour),
        scheduled_hour,
        config.source_set_key,
        config.group_id,
        instance_id,
        now=now,
    )
    if row.get("source_set_key") != config.source_set_key:
        print(
            f"[QQ 白名单同步] 时隙 {scheduled_hour} 已存在旧版单群任务，"
            "为避免跨协议重复覆盖，本时隙安全跳过"
        )
        return {"status": "legacy_slot_already_used"}
    resumed = await _resume_non_started_row(
        onebot, config, game_client, row, instance_id, sleep_fn
    )
    if resumed is not None:
        return resumed

    if row.get("snapshot_sha256") is None:
        row, capture_error = await _capture_and_bind_snapshot(
            onebot, config, row, scheduled_hour, now_fn, sleep_fn
        )
        if row and row.get("snapshot_sha256") is not None:
            resumed = await _resume_non_started_row(
                onebot, config, game_client, row, instance_id, sleep_fn
            )
            if resumed is not None:
                return resumed
        else:
            failure_key = build_capture_failure_key(
                config.group_ids, scheduled_hour
            )
            row = storage.bind_qq_whitelist_sync_failure_key(
                config.source_set_key,
                scheduled_hour,
                failure_key,
                now=int(now_fn()),
            )
            if row and row.get("snapshot_sha256") is not None:
                return await _execute_bound_snapshot(
                    onebot,
                    config,
                    game_client,
                    row,
                    instance_id,
                    now_fn,
                    sleep_fn,
                )
            return await _finalize_failed_sync(
                onebot,
                config,
                game_client,
                row["operation_key"] if row else failure_key,
                instance_id,
                capture_error or "双群实时快照获取失败",
                int(now_fn()),
                sleep_fn,
            )

    return await _execute_bound_snapshot(
        onebot,
        config,
        game_client,
        row,
        instance_id,
        now_fn,
        sleep_fn,
    )


async def _resume_non_started_row(
    onebot, config, game_client, row, instance_id, sleep_fn
):
    if not row or row["state"] == "started":
        return None
    if row["state"] == "notified":
        await _acknowledge_notification(
            game_client, row, instance_id, config
        )
        return {"status": "notified"}
    if row["state"] == "failed":
        recovered, report_error = await _report_failed_row(
            game_client, row, config, int(time.time())
        )
        if recovered and recovered["state"] in {
            "committed",
            "notification_uncertain",
        }:
            return await _notify_committed(
                onebot, config, game_client, recovered, instance_id, sleep_fn
            )
        if recovered and recovered["state"] == "suppressed":
            return {"status": "suppressed"}
        return {
            "status": "failed",
            "error": row.get("last_error"),
            **({"reportError": report_error} if report_error else {}),
        }
    if row["state"] in {"committed", "notification_uncertain"}:
        return await _notify_committed(
            onebot, config, game_client, row, instance_id, sleep_fn
        )
    if row["state"] in {"suppressed", "expired"}:
        return {"status": row["state"]}
    return {"status": row["state"]}


async def _capture_and_bind_snapshot(
    onebot, config, row, scheduled_hour, now_fn, sleep_fn
):
    last_error = None
    for delay in config.update_retry_delays:
        if delay:
            await sleep_fn(delay)
        if int(now_fn()) - scheduled_hour > config.maximum_delay_seconds:
            break
        current = storage.get_qq_whitelist_sync_for_slot(
            config.source_set_key, scheduled_hour
        )
        if current and current.get("snapshot_sha256") is not None:
            return current, None
        if current and current.get("state") != "started":
            return current, current.get("last_error")
        try:
            previous_count = (
                storage.get_last_qq_whitelist_sync_source_member_count(
                    config.source_set_key
                )
            )
            snapshot = await get_combined_group_snapshot(
                onebot, config, previous_count
            )
        except SyncRejectedError as exc:
            return current or row, str(exc)
        except (
            RuntimeError,
            asyncio.TimeoutError,
            TimeoutError,
            OSError,
            WebSocketException,
        ) as exc:
            last_error = str(exc) or type(exc).__name__
            if current:
                storage.record_qq_whitelist_sync_error(
                    current["operation_key"], last_error, now=int(now_fn())
                )
            print(
                f"[QQ 白名单同步] 时隙 {scheduled_hour} 拉取双群快照失败："
                f"{last_error}"
            )
            continue
        operation_key = build_operation_key(
            config.group_ids, scheduled_hour, snapshot["members"]
        )
        bound = storage.bind_qq_whitelist_sync_snapshot(
            config.source_set_key,
            scheduled_hour,
            operation_key,
            json.dumps(
                snapshot["sourceGroups"],
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            snapshot["snapshotSha256"],
            json.dumps(
                snapshot["members"],
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            now=int(now_fn()),
        )
        return bound, None
    return row, last_error or "当前两小时时隙同步已超过允许延迟"


async def _execute_bound_snapshot(
    onebot,
    config,
    game_client,
    row,
    instance_id,
    now_fn,
    sleep_fn,
):
    try:
        payload = _payload_from_stored_snapshot(row, config)
    except (RuntimeError, SyncConfigurationError) as exc:
        return await _finalize_failed_sync(
            onebot,
            config,
            game_client,
            row["operation_key"],
            instance_id,
            f"本地双群快照损坏：{exc}",
            int(now_fn()),
            sleep_fn,
        )
    operation_key = row["operation_key"]
    scheduled_hour = int(row["scheduled_hour"])
    recovered, recovery_error = await _recover_committed_row(
        game_client,
        operation_key,
        instance_id,
        config,
        scheduled_hour,
        int(now_fn()),
    )
    if recovered is not None:
        return await _notify_committed(
            onebot, config, game_client, recovered, instance_id, sleep_fn
        )
    if recovery_error:
        storage.record_qq_whitelist_sync_error(
            operation_key, recovery_error, now=int(now_fn())
        )

    last_error = None
    for delay in config.update_retry_delays:
        if delay:
            await sleep_fn(delay)
        if int(now_fn()) - scheduled_hour > config.maximum_delay_seconds:
            break
        try:
            response = await game_client.synchronize(payload)
            _persist_committed_response(
                response,
                operation_key,
                config,
                scheduled_hour,
                int(now_fn()),
            )
        except (SyncTransportError, SyncRejectedError, RuntimeError) as exc:
            recovered, recovery_error = await _recover_committed_row(
                game_client,
                operation_key,
                instance_id,
                config,
                scheduled_hour,
                int(now_fn()),
            )
            if recovered is not None:
                return await _notify_committed(
                    onebot,
                    config,
                    game_client,
                    recovered,
                    instance_id,
                    sleep_fn,
                )
            last_error = str(exc)
            if recovery_error:
                last_error = f"{last_error}；状态核对失败：{recovery_error}"
            if isinstance(exc, SyncRejectedError):
                return await _finalize_failed_sync(
                    onebot,
                    config,
                    game_client,
                    operation_key,
                    instance_id,
                    last_error,
                    int(now_fn()),
                    sleep_fn,
                )
            storage.record_qq_whitelist_sync_error(
                operation_key, last_error, now=int(now_fn())
            )
            print(f"[QQ 白名单同步] {operation_key} 尝试失败：{last_error}")
            continue
        committed = storage.get_qq_whitelist_sync(operation_key)
        return await _notify_committed(
            onebot, config, game_client, committed, instance_id, sleep_fn
        )
    return await _finalize_failed_sync(
        onebot,
        config,
        game_client,
        operation_key,
        instance_id,
        last_error or "当前两小时时隙同步已超过允许延迟",
        int(now_fn()),
        sleep_fn,
    )


def _payload_from_stored_snapshot(row, config):
    try:
        source_groups = json.loads(row.get("source_groups_json") or "")
        members = json.loads(row.get("snapshot_members_json") or "")
    except (TypeError, json.JSONDecodeError) as exc:
        raise RuntimeError("持久化 JSON 无效") from exc
    if not isinstance(source_groups, list) or not isinstance(members, list):
        raise RuntimeError("持久化快照格式无效")
    if any(not isinstance(item, dict) for item in source_groups) or tuple(
        str(item.get("groupId") or "") for item in source_groups
    ) != config.group_ids:
        raise RuntimeError("持久化双群身份不一致")
    normalized_members = [_normalize_qq(value) for value in members]
    if normalized_members != sorted(set(normalized_members)):
        raise RuntimeError("持久化成员并集未去重排序")
    digest = snapshot_sha256(normalized_members)
    if digest != row.get("snapshot_sha256"):
        raise RuntimeError("持久化成员摘要不一致")
    expected_key = build_operation_key(
        config.group_ids, int(row["scheduled_hour"]), normalized_members
    )
    if expected_key != row["operation_key"]:
        raise RuntimeError("持久化操作键未绑定当前双群快照")
    return {
        "protocolVersion": 2,
        "operationKey": expected_key,
        "scheduledHour": int(row["scheduled_hour"]),
        "sourceGroups": source_groups,
        "reportedMemberCount": len(normalized_members),
        "clientInstanceId": row["client_instance_id"],
        "members": normalized_members,
    }


def _persist_committed_response(
    response, operation_key, config, scheduled_hour, now
):
    stored = storage.get_qq_whitelist_sync(operation_key)
    if not stored or stored.get("snapshot_sha256") is None:
        raise RuntimeError("游戏服务提交结果缺少对应的本地完整快照")
    expected_payload = _payload_from_stored_snapshot(stored, config)
    version, member_count, owner = _validate_game_response(
        response,
        operation_key,
        config,
        scheduled_hour,
        expected_payload["reportedMemberCount"],
    )
    message = (
        f"{SUCCESS_PHRASE}（两个群去重后共 {member_count} 人，v{version}）"
    )
    storage.mark_qq_whitelist_sync_committed(
        operation_key,
        version,
        member_count,
        owner and response.get("notificationAcknowledgedAt") is None,
        message,
        notification_group_ids=config.group_ids,
        now=now,
    )


async def _recover_committed_row(
    game_client,
    operation_key,
    instance_id,
    config,
    scheduled_hour,
    now,
):
    try:
        recovered = await game_client.status(operation_key, instance_id)
        if recovered is None:
            return None, None
        _persist_committed_response(
            recovered, operation_key, config, scheduled_hour, now
        )
        return storage.get_qq_whitelist_sync(operation_key), None
    except (SyncTransportError, SyncRejectedError, RuntimeError) as exc:
        return None, str(exc) or type(exc).__name__


async def _finalize_failed_sync(
    onebot,
    config,
    game_client,
    operation_key,
    instance_id,
    error,
    now,
    sleep_fn,
):
    storage.fail_qq_whitelist_sync(operation_key, error, now=now)
    row = storage.get_qq_whitelist_sync(operation_key)
    recovered, report_error = await _report_failed_row(
        game_client, row, config, now
    )
    if recovered and recovered["state"] in {
        "committed",
        "notification_uncertain",
    }:
        return await _notify_committed(
            onebot, config, game_client, recovered, instance_id, sleep_fn
        )
    if recovered and recovered["state"] == "suppressed":
        return {"status": "suppressed"}
    print(f"[QQ 白名单同步] {operation_key} 已失败：{error}")
    return {
        "status": "failed",
        "error": error,
        **({"reportError": report_error} if report_error else {}),
    }


async def _report_failed_row(game_client, row, config, now):
    if (
        not row
        or row.get("state") != "failed"
        or row.get("failure_reported_at") is not None
    ):
        return row, None
    try:
        response = await game_client.report_failure(
            {
                "protocolVersion": 2,
                "operationKey": row["operation_key"],
                "scheduledHour": int(row["scheduled_hour"]),
                "sourceGroupIds": list(config.group_ids),
                "clientInstanceId": row["client_instance_id"],
                "error": row.get("last_error") or "未提供失败原因",
            }
        )
        if not isinstance(response, dict):
            raise SyncTransportError("游戏服务失败报告响应格式异常")
        if response.get("operationKey") != row["operation_key"]:
            raise SyncTransportError("游戏服务失败报告返回了错误幂等键")
        if response.get("committed") is True and response.get("status") == "committed":
            _persist_committed_response(
                response,
                row["operation_key"],
                config,
                row["scheduled_hour"],
                now,
            )
            return storage.get_qq_whitelist_sync(row["operation_key"]), None
        update = response.get("update")
        returned_groups = response.get("sourceGroupIds")
        if (
            response.get("protocolVersion") != 2
            or not isinstance(returned_groups, list)
            or tuple(str(value) for value in returned_groups) != config.group_ids
            or response.get("committed") is not False
            or response.get("status") != "failure_recorded"
            or not isinstance(update, dict)
            or update.get("outcome") != "failure"
            or update.get("operationKey") != row["operation_key"]
            or update.get("scheduledHour") != int(row["scheduled_hour"])
        ):
            raise SyncTransportError("游戏服务失败报告确认字段不一致")
        if not storage.mark_qq_whitelist_sync_failure_reported(
            row["operation_key"], now=now
        ):
            current = storage.get_qq_whitelist_sync(row["operation_key"])
            if not current or current.get("state") != "failed":
                return current, None
            raise RuntimeError("失败报告已保存，但本地确认状态发生竞争")
        return storage.get_qq_whitelist_sync(row["operation_key"]), None
    except (SyncTransportError, SyncRejectedError, RuntimeError) as exc:
        detail = str(exc) or type(exc).__name__
        print(f"[QQ 白名单同步] 失败报告暂未落入游戏权威库：{detail}")
        return row, detail


async def _notify_committed(
    onebot, config, game_client, row, instance_id, sleep_fn
):
    if not row or row["state"] not in {
        "committed",
        "notification_uncertain",
    }:
        return {"status": row["state"] if row else "missing"}
    operation_key = row["operation_key"]
    storage.recover_inflight_qq_whitelist_sync_notifications(
        operation_key,
        PROCESS_RUN_ID,
        "机器人进程重启时该群通知仍在发送，按至多一次原则不自动重发",
    )
    for group_id in config.group_ids:
        notifications = {
            item["group_id"]: item
            for item in storage.list_qq_whitelist_sync_notifications(
                operation_key
            )
        }
        current = notifications.get(group_id)
        if not current or current["state"] != "pending":
            continue
        for delay in config.notification_retry_delays:
            if delay:
                await sleep_fn(delay)
            claimed = storage.claim_qq_whitelist_sync_group_notification(
                operation_key, group_id, PROCESS_RUN_ID
            )
            if not claimed:
                break
            try:
                await onebot.call_action(
                    "send_group_msg",
                    {
                        "group_id": int(group_id),
                        "message": claimed["notification_message"],
                    },
                )
            except asyncio.CancelledError:
                storage.mark_qq_whitelist_sync_group_notification_uncertain(
                    operation_key,
                    group_id,
                    "发送通知时连接或进程被取消",
                )
                raise
            except RuntimeError as exc:
                if not getattr(exc, "onebot_explicit_rejection", False):
                    storage.mark_qq_whitelist_sync_group_notification_uncertain(
                        operation_key,
                        group_id,
                        f"群通知抛出未确认的运行时异常，送达状态不确定：{exc}",
                    )
                    print(
                        f"[QQ 白名单同步] 群 {group_id} 通知运行时结果不确定，"
                        "为避免重复消息不自动重发"
                    )
                    break
                storage.release_qq_whitelist_sync_group_notification(
                    operation_key, group_id, str(exc)
                )
                print(
                    f"[QQ 白名单同步] 群 {group_id} 通知明确失败，"
                    f"将有限重试：{exc}"
                )
                continue
            except (
                asyncio.TimeoutError,
                TimeoutError,
                OSError,
                WebSocketException,
            ) as exc:
                storage.mark_qq_whitelist_sync_group_notification_uncertain(
                    operation_key,
                    group_id,
                    f"群通知送达状态不确定：{exc}",
                )
                print(
                    f"[QQ 白名单同步] 群 {group_id} 通知结果不确定，"
                    "为避免重复消息不自动重发"
                )
                break
            except Exception as exc:
                storage.mark_qq_whitelist_sync_group_notification_uncertain(
                    operation_key,
                    group_id,
                    f"群通知抛出未分类异常，送达状态不确定：{exc}",
                )
                print(
                    f"[QQ 白名单同步] 群 {group_id} 通知发生未分类异常，"
                    "为避免重复消息不自动重发"
                )
                break
            try:
                completed = (
                    storage.complete_qq_whitelist_sync_group_notification(
                        operation_key, group_id
                    )
                )
            except Exception as exc:
                storage.mark_qq_whitelist_sync_group_notification_uncertain(
                    operation_key,
                    group_id,
                    f"群通知已发出但本地落盘失败：{exc}",
                )
                break
            if not completed:
                storage.mark_qq_whitelist_sync_group_notification_uncertain(
                    operation_key,
                    group_id,
                    "群通知已发出但本地状态发生竞争",
                )
            break

    aggregate = storage.refresh_qq_whitelist_sync_notification_state(
        operation_key
    )
    notifications = storage.list_qq_whitelist_sync_notifications(operation_key)
    if aggregate and aggregate["state"] == "notified":
        await _acknowledge_notification(
            game_client, aggregate, instance_id, config
        )
        print(
            f"[QQ 白名单同步] {aggregate['notification_message']}，"
            f"两个群均已通知，计划时间={aggregate['scheduled_hour']}"
        )
        return {"status": "notified", "version": aggregate["version"]}
    if any(item["state"] == "uncertain" for item in notifications):
        return {
            "status": "notification_uncertain",
            "notifications": {
                item["group_id"]: item["state"] for item in notifications
            },
        }
    if any(item["state"] == "pending" for item in notifications):
        return {
            "status": "notification_failed",
            "notifications": {
                item["group_id"]: item["state"] for item in notifications
            },
        }
    return {"status": aggregate["state"] if aggregate else "missing"}


async def _acknowledge_notification(game_client, row, instance_id, config):
    if not row or row.get("notification_acked_at") is not None:
        return
    try:
        response = await game_client.acknowledge(
            row["operation_key"], instance_id, row["version"]
        )
        version, member_count, owner = _validate_game_response(
            response,
            row["operation_key"],
            config,
            row["scheduled_hour"],
            row["member_count"],
        )
        if version != int(row["version"]) or member_count != int(
            row["member_count"]
        ):
            raise SyncTransportError("游戏服务通知确认与本地提交版本不一致")
        if owner or response.get("notificationAcknowledgedAt") is None:
            raise SyncTransportError("游戏服务没有确认双群通知已登记")
        storage.acknowledge_qq_whitelist_sync_notification(
            row["operation_key"], response.get("notificationAcknowledgedAt")
        )
    except Exception as exc:
        print(
            "[QQ 白名单同步] 双群通知确认回写失败，稍后随重连恢复："
            f"{exc}"
        )


async def recover_current_slot(
    onebot, config, game_client=None, now_fn=time.time, sleep_fn=asyncio.sleep
):
    """仅在当前两小时时隙延迟窗口内恢复已持久化任务。"""
    now_value = int(now_fn())
    now = datetime.fromtimestamp(now_value, tz=timezone.utc)
    scheduled_hour = scheduled_slot_epoch(
        now, config.timezone_name, config.interval_hours
    )
    recoverable = _is_within_schedule_window(
        scheduled_hour, now_value, config.maximum_delay_seconds
    )
    expiration_cutoff = (
        scheduled_hour
        if recoverable
        else int(
            next_slot(
                now, config.timezone_name, config.interval_hours
            ).timestamp()
        )
    )
    storage.expire_old_qq_whitelist_sync_source_runs(
        config.source_set_key, expiration_cutoff, now=now_value
    )
    if not recoverable:
        return {"status": "nothing_to_recover"}
    row = storage.get_qq_whitelist_sync_for_slot(
        config.source_set_key, scheduled_hour
    )
    if not row:
        return {"status": "nothing_to_recover"}
    return await execute_sync_hour(
        onebot, config, scheduled_hour, game_client, now_fn, sleep_fn
    )


async def recover_scheduled_midnight(
    onebot, config, game_client=None, now_fn=time.time, sleep_fn=asyncio.sleep
):
    """兼容旧函数名；现在恢复当前两小时时隙。"""
    return await recover_current_slot(
        onebot, config, game_client, now_fn, sleep_fn
    )


async def recover_current_hour(
    onebot, config, game_client=None, now_fn=time.time, sleep_fn=asyncio.sleep
):
    return await recover_current_slot(
        onebot, config, game_client, now_fn, sleep_fn
    )


async def recover_unreported_failure_reports(
    config, game_client=None, now_fn=time.time
):
    """只补报持久化失败事件，不重新拉群成员或重跑过期更新。"""
    game_client = game_client or GameWhitelistClient(config)
    now = int(now_fn())
    failures = storage.list_unreported_qq_whitelist_sync_source_failures(
        config.source_set_key
    )
    recovered = 0
    pending = 0
    now_datetime = datetime.fromtimestamp(now, tz=timezone.utc)
    current_slot = scheduled_slot_epoch(
        now_datetime, config.timezone_name, config.interval_hours
    )
    current_is_recoverable = _is_within_schedule_window(
        current_slot, now, config.maximum_delay_seconds
    )
    expiration_cutoff = (
        current_slot
        if current_is_recoverable
        else int(
            next_slot(
                now_datetime, config.timezone_name, config.interval_hours
            ).timestamp()
        )
    )
    current_committed = False
    for row in failures:
        current, report_error = await _report_failed_row(
            game_client, row, config, now
        )
        if report_error:
            pending += 1
        else:
            recovered += 1
        if current and current.get("state") in {
            "committed",
            "notification_uncertain",
        }:
            if (
                current_is_recoverable
                and int(current["scheduled_hour"]) == current_slot
            ):
                current_committed = True
            else:
                storage.expire_old_qq_whitelist_sync_source_runs(
                    config.source_set_key, expiration_cutoff, now=now
                )
    return {
        "recovered": recovered,
        "pending": pending,
        "currentCommitted": current_committed,
    }


async def run_sync_loop(onebot, config: SyncConfig):
    """连接存续期间按 Asia/Singapore 的 00/02/... 确定性时隙运行。"""
    if not config.enabled:
        return
    game_client = GameWhitelistClient(config)
    try:
        await recover_unreported_failure_reports(config, game_client)
    except Exception as exc:
        print(
            "[QQ 白名单同步] 失败报告恢复异常，将继续恢复当前时隙："
            f"{exc}"
        )
    try:
        await recover_current_slot(onebot, config, game_client)
    except Exception as exc:
        print(
            f"[QQ 白名单同步] 当前时隙恢复失败，两小时循环继续运行：{exc}"
        )
    while True:
        now = datetime.now(timezone.utc)
        target = next_slot(
            now, config.timezone_name, config.interval_hours
        )
        scheduled_hour = int(target.timestamp())
        while True:
            remaining = target.timestamp() - time.time()
            if remaining <= 0:
                break
            await asyncio.sleep(min(remaining, 60))
            try:
                await recover_unreported_failure_reports(config, game_client)
                await recover_current_slot(onebot, config, game_client)
            except Exception as exc:
                print(
                    "[QQ 白名单同步] 时隙恢复异常，将继续保留本地记录："
                    f"{exc}"
                )
        actual_now = int(time.time())
        if not _is_within_schedule_window(
            scheduled_hour, actual_now, config.maximum_delay_seconds
        ):
            print(
                f"[QQ 白名单同步] 错过两小时计划时隙 {scheduled_hour}，"
                "不补发过期任务"
            )
            continue
        try:
            await execute_sync_hour(
                onebot, config, scheduled_hour, game_client=game_client
            )
        except Exception as exc:
            print(
                "[QQ 白名单同步] 两小时时隙任务异常，下一个时隙仍会继续："
                f"{exc}"
            )
