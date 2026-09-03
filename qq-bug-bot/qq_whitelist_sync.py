# -*- coding: utf-8 -*-
"""GrandUMI QQ 群成员到游戏准入白名单的每日同步状态机。"""

import asyncio
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

from websockets.exceptions import WebSocketException

import storage


QQ_PATTERN = re.compile(r"^[0-9]{5,12}$", re.ASCII)
SUCCESS_PHRASE = "白名单已更新"
BUSINESS_TIMEZONE = timezone(timedelta(hours=8), name="Asia/Singapore")


class SyncConfigurationError(RuntimeError):
    pass


class SyncRejectedError(RuntimeError):
    """游戏服务明确拒绝请求；重复发送不会成功。"""


class SyncTransportError(RuntimeError):
    """网络或服务端瞬时错误，可以在当日计划窗口内有限重试。"""


@dataclass(frozen=True)
class SyncConfig:
    enabled: bool
    group_id: str
    group_name: str
    timezone_name: str
    endpoint: str
    secret: str
    minimum_member_count: int = 100
    maximum_shrink_percent: int = 25
    maximum_delay_seconds: int = 600
    update_retry_delays: tuple[float, ...] = (0, 5, 20)
    notification_retry_delays: tuple[float, ...] = (0, 5, 20)
    http_timeout_seconds: float = 20

    @classmethod
    def from_bot_config(cls, cfg: dict):
        enabled = cfg.get("qq_whitelist_sync_enabled", False) is True
        if not enabled:
            return cls(False, "", "", "Asia/Singapore", "", "")
        group_id = _normalize_qq(cfg.get("qq_whitelist_sync_group_id"))
        group_name = _normalize_group_name(
            cfg.get("qq_whitelist_sync_group_name")
        )
        timezone_name = str(
            cfg.get("qq_whitelist_sync_timezone") or "Asia/Singapore"
        ).strip()
        if timezone_name != "Asia/Singapore":
            raise SyncConfigurationError("QQ 白名单同步时区必须是 Asia/Singapore（UTC+8）")
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
        if group_id not in allowed_groups:
            raise SyncConfigurationError(
                "QQ 白名单同步目标群必须同时明确列入 allowed_groups"
            )
        return cls(
            True,
            group_id,
            group_name,
            timezone_name,
            endpoint,
            secret,
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


def scheduled_midnight_epoch(
    now: datetime, timezone_name="Asia/Singapore"
) -> int:
    """返回 now 所在 Asia/Singapore 自然日的 00:00 Unix 时间。"""
    if timezone_name != "Asia/Singapore":
        raise SyncConfigurationError("QQ 白名单同步时区必须是 Asia/Singapore（UTC+8）")
    local = now.astimezone(BUSINESS_TIMEZONE)
    return int(
        local.replace(hour=0, minute=0, second=0, microsecond=0).timestamp()
    )


def next_midnight(now: datetime, timezone_name="Asia/Singapore") -> datetime:
    """始终从墙上时钟重算下一个自然日 00:00，不累计固定间隔。"""
    if timezone_name != "Asia/Singapore":
        raise SyncConfigurationError("QQ 白名单同步时区必须是 Asia/Singapore（UTC+8）")
    local = now.astimezone(BUSINESS_TIMEZONE)
    return local.replace(
        hour=0, minute=0, second=0, microsecond=0
    ) + timedelta(days=1)


def current_hour_epoch(now: datetime, timezone_name="Asia/Singapore") -> int:
    """兼容旧调用名；现在返回当日唯一的 00:00 计划槽。"""
    return scheduled_midnight_epoch(now, timezone_name)


def next_hour(now: datetime, timezone_name="Asia/Singapore") -> datetime:
    """兼容旧调用名；现在返回下一个自然日 00:00。"""
    return next_midnight(now, timezone_name)


def _is_daily_schedule(scheduled_hour: int) -> bool:
    try:
        scheduled = datetime.fromtimestamp(
            int(scheduled_hour), tz=BUSINESS_TIMEZONE
        )
    except (OverflowError, OSError, TypeError, ValueError):
        return False
    return (
        scheduled.hour == 0
        and scheduled.minute == 0
        and scheduled.second == 0
        and scheduled.microsecond == 0
    )


def _is_within_schedule_window(
    scheduled_hour: int, now: int, maximum_delay_seconds: int
) -> bool:
    return (
        _is_daily_schedule(scheduled_hour)
        and int(scheduled_hour) <= int(now)
        and int(now) - int(scheduled_hour) <= int(maximum_delay_seconds)
    )


def build_operation_key(group_id: str, scheduled_hour: int) -> str:
    return f"qq-whitelist:{_normalize_qq(group_id)}:{int(scheduled_hour)}"


async def get_realtime_group_snapshot(onebot, config: SyncConfig, previous_count=None):
    info_response = await onebot.call_action(
        "get_group_info",
        {"group_id": int(config.group_id), "no_cache": True},
    )
    info = info_response.get("data")
    if not isinstance(info, dict):
        raise SyncRejectedError("OneBot 群信息响应格式异常")
    if str(info.get("group_id") or "") != config.group_id:
        raise SyncRejectedError("OneBot 群信息返回了错误群号")
    try:
        returned_name = _normalize_group_name(info.get("group_name"))
    except SyncConfigurationError as exc:
        raise SyncRejectedError("OneBot 群信息返回的群名无效") from exc
    if returned_name != config.group_name:
        raise SyncRejectedError("OneBot 群信息返回了错误群名")
    reported_count = _strict_positive_int(info.get("member_count"), "群成员数")

    members_response = await onebot.call_action(
        "get_group_member_list",
        {"group_id": int(config.group_id), "no_cache": True},
    )
    rows = members_response.get("data")
    if not isinstance(rows, list):
        raise SyncRejectedError("OneBot 群成员列表响应格式异常")
    members = []
    seen = set()
    for index, item in enumerate(rows, 1):
        if not isinstance(item, dict):
            raise SyncRejectedError(f"OneBot 第 {index} 条群成员记录格式异常")
        if str(item.get("group_id") or "") != config.group_id:
            raise SyncRejectedError("OneBot 群成员列表混入其他群的数据")
        try:
            qq = _normalize_qq(item.get("user_id"))
        except SyncConfigurationError as exc:
            raise SyncRejectedError(f"OneBot 第 {index} 条群成员 QQ 无效") from exc
        if qq in seen:
            raise SyncRejectedError("OneBot 群成员列表包含重复 QQ")
        seen.add(qq)
        members.append(qq)
    if not members:
        raise SyncRejectedError("拒绝用空群成员列表覆盖白名单")
    if len(members) != reported_count:
        raise SyncRejectedError("OneBot 群信息与群成员列表的人数不一致")
    if len(members) < config.minimum_member_count:
        raise SyncRejectedError("群成员数量低于配置的安全下限")
    if previous_count and len(members) * 100 < previous_count * (
        100 - config.maximum_shrink_percent
    ):
        raise SyncRejectedError("群成员数量相较上次成功同步显著缩水")
    return {
        "groupId": config.group_id,
        "groupName": config.group_name,
        "reportedMemberCount": reported_count,
        "members": sorted(members),
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
            self._post,
            self.endpoint + "/failure",
            payload,
            False,
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
                "User-Agent": "GrandUMI-QQ-Whitelist-Sync/1",
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
            raise SyncTransportError(f"无法连接游戏白名单内部端点：{exc}") from exc
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


def _validate_game_response(response, operation_key, config, scheduled_hour):
    if not isinstance(response, dict):
        raise SyncTransportError("游戏服务同步响应格式异常")
    if response.get("operationKey") != operation_key:
        raise SyncTransportError("游戏服务返回了错误幂等键")
    if str(response.get("groupId") or "") != config.group_id:
        raise SyncTransportError("游戏服务返回了错误群号")
    returned_hour = response.get("scheduledHour")
    if (
        isinstance(returned_hour, bool)
        or not isinstance(returned_hour, int)
        or returned_hour != int(scheduled_hour)
    ):
        raise SyncTransportError("游戏服务返回了错误计划时间")
    version = _strict_game_positive_int(response.get("version"), "版本")
    member_count = _strict_game_positive_int(
        response.get("memberCount"), "成员数"
    )
    return version, member_count, bool(response.get("notificationOwner"))


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
    """执行或恢复一个已到达的 00:00 计划槽；其他时间不执行。"""
    now = int(now_fn())
    if not _is_within_schedule_window(
        scheduled_hour, now, config.maximum_delay_seconds
    ):
        return {"status": "stale"}
    game_client = game_client or GameWhitelistClient(config)
    operation_key = build_operation_key(config.group_id, scheduled_hour)
    instance_id = storage.get_or_create_qq_whitelist_sync_instance_id(now=now)
    row = storage.prepare_qq_whitelist_sync(
        operation_key,
        scheduled_hour,
        config.group_id,
        config.group_name,
        instance_id,
        now=now,
    )

    if row["state"] == "notifying":
        storage.mark_qq_whitelist_sync_notification_uncertain(
            operation_key,
            "机器人重启时通知动作仍在发送，按至多一次原则不自动重发",
            now=now,
        )
        return {"status": "notification_uncertain"}
    if row["state"] == "notified":
        await _acknowledge_notification(game_client, row, instance_id)
        return {"status": "notified"}
    if row["state"] == "failed":
        recovered, report_error = await _report_failed_row(
            game_client, row, config, now
        )
        if recovered and recovered["state"] == "committed":
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
    if row["state"] in {
        "suppressed",
        "expired",
        "notification_uncertain",
    }:
        return {"status": row["state"]}
    if row["state"] == "committed":
        return await _notify_committed(
            onebot, config, game_client, row, instance_id, sleep_fn
        )

    # 进程可能在服务端提交后、写入本地提交态前退出；先查幂等状态，
    # 找到已提交版本时只恢复通知，不再覆盖白名单。
    row, recovery_error = await _recover_committed_row(
        game_client,
        operation_key,
        instance_id,
        config,
        scheduled_hour,
        now,
    )
    if recovery_error:
        storage.record_qq_whitelist_sync_error(
            operation_key, recovery_error, now=now
        )
    if row is not None:
        return await _notify_committed(
            onebot, config, game_client, row, instance_id, sleep_fn
        )

    last_error = None
    for delay in config.update_retry_delays:
        if delay:
            await sleep_fn(delay)
        if int(now_fn()) - scheduled_hour > config.maximum_delay_seconds:
            break
        try:
            previous_count = storage.get_last_qq_whitelist_sync_member_count(
                config.group_id
            )
            snapshot = await get_realtime_group_snapshot(
                onebot, config, previous_count
            )
        except SyncRejectedError as exc:
            last_error = str(exc)
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
        except (
            RuntimeError,
            asyncio.TimeoutError,
            TimeoutError,
            OSError,
            WebSocketException,
        ) as exc:
            last_error = str(exc) or type(exc).__name__
            storage.record_qq_whitelist_sync_error(
                operation_key, last_error, now=int(now_fn())
            )
            print(f"[QQ 白名单同步] {operation_key} 拉取群快照失败：{last_error}")
            continue

        try:
            payload = {
                "operationKey": operation_key,
                "scheduledHour": int(scheduled_hour),
                "groupId": snapshot["groupId"],
                "groupName": snapshot["groupName"],
                "reportedMemberCount": snapshot["reportedMemberCount"],
                "clientInstanceId": instance_id,
                "members": snapshot["members"],
            }
            response = await game_client.synchronize(payload)
            _persist_committed_response(
                response, operation_key, config, scheduled_hour, int(now_fn())
            )
        except (SyncTransportError, SyncRejectedError, RuntimeError) as exc:
            # POST 超时或连接中断时，服务端可能已经原子提交。先查幂等状态，
            # 再决定是否重试，避免下一份已变化快照把成功任务误判为冲突。
            recovered_row, recovery_error = await _recover_committed_row(
                game_client,
                operation_key,
                instance_id,
                config,
                scheduled_hour,
                int(now_fn()),
            )
            if recovered_row is not None:
                return await _notify_committed(
                    onebot,
                    config,
                    game_client,
                    recovered_row,
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

        row = storage.get_qq_whitelist_sync(operation_key)
        return await _notify_committed(
            onebot, config, game_client, row, instance_id, sleep_fn
        )
    last_error = last_error or "当日 00:00 同步已超过允许延迟"
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


def _persist_committed_response(
    response, operation_key, config, scheduled_hour, now
):
    version, member_count, owner = _validate_game_response(
        response, operation_key, config, scheduled_hour
    )
    message = f"{SUCCESS_PHRASE}（{member_count} 人，v{version}）"
    storage.mark_qq_whitelist_sync_committed(
        operation_key,
        version,
        member_count,
        owner and response.get("notificationAcknowledgedAt") is None,
        message,
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
    """查询服务端幂等记录，区分“未提交”与“响应丢失但已经提交”。"""
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
    if recovered and recovered["state"] == "committed":
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
    """把本地失败幂等写入游戏权威库；若 POST 实际已成功则以前者纠正本地状态。"""
    if (
        not row
        or row.get("state") != "failed"
        or row.get("failure_reported_at") is not None
    ):
        return row, None
    try:
        response = await game_client.report_failure(
            {
                "operationKey": row["operation_key"],
                "scheduledHour": int(row["scheduled_hour"]),
                "groupId": row["group_id"],
                "groupName": row["group_name"],
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
        if (
            response.get("committed") is not False
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
    if not row or row["state"] != "committed":
        return {"status": row["state"] if row else "missing"}
    operation_key = row["operation_key"]
    for delay in config.notification_retry_delays:
        if delay:
            await sleep_fn(delay)
        claimed = storage.claim_qq_whitelist_sync_notification(operation_key)
        if not claimed:
            current = storage.get_qq_whitelist_sync(operation_key)
            return {"status": current["state"] if current else "missing"}
        try:
            await onebot.call_action(
                "send_group_msg",
                {
                    "group_id": int(config.group_id),
                    "message": claimed["notification_message"],
                },
            )
        except asyncio.CancelledError:
            storage.mark_qq_whitelist_sync_notification_uncertain(
                operation_key, "发送通知时连接或进程被取消"
            )
            raise
        except RuntimeError as exc:
            # call_action 只有收到 OneBot 明确失败响应时才抛 RuntimeError，
            # 该结果可以安全重试，不会把超时的未知送达状态当成失败。
            storage.release_qq_whitelist_sync_notification(
                operation_key, str(exc)
            )
            print(f"[QQ 白名单同步] 群通知明确失败，将有限重试：{exc}")
            continue
        except (
            asyncio.TimeoutError,
            TimeoutError,
            OSError,
            WebSocketException,
        ) as exc:
            storage.mark_qq_whitelist_sync_notification_uncertain(
                operation_key, f"群通知送达状态不确定：{exc}"
            )
            print("[QQ 白名单同步] 群通知结果不确定，为避免重复消息不自动重发")
            return {"status": "notification_uncertain"}
        try:
            completed_locally = storage.complete_qq_whitelist_sync_notification(
                operation_key
            )
        except Exception as exc:
            # 消息已经成功发出，任何本地落盘失败都必须按未知状态停止，
            # 不能回到 committed 后再次发送。
            print(f"[QQ 白名单同步] 群通知已发出但本地落盘失败：{exc}")
            return {"status": "notification_uncertain"}
        if not completed_locally:
            current = storage.get_qq_whitelist_sync(operation_key)
            if not current or current.get("state") != "notified":
                storage.mark_qq_whitelist_sync_notification_uncertain(
                    operation_key, "群通知已发出但本地状态发生竞争"
                )
                return {"status": "notification_uncertain"}
        completed = storage.get_qq_whitelist_sync(operation_key)
        await _acknowledge_notification(game_client, completed, instance_id)
        print(
            f"[QQ 白名单同步] {completed['notification_message']}，"
            f"计划时间={completed['scheduled_hour']}"
        )
        return {"status": "notified", "version": completed["version"]}
    return {"status": "notification_failed"}


async def _acknowledge_notification(game_client, row, instance_id):
    if not row or row.get("notification_acked_at") is not None:
        return
    try:
        response = await game_client.acknowledge(
            row["operation_key"], instance_id, row["version"]
        )
        storage.acknowledge_qq_whitelist_sync_notification(
            row["operation_key"], response.get("notificationAcknowledgedAt")
        )
    except (SyncTransportError, SyncRejectedError, RuntimeError) as exc:
        # 群消息已经确认成功，确认回写失败只影响服务端展示；绝不因此重发。
        print(f"[QQ 白名单同步] 通知确认回写失败，稍后随重连恢复：{exc}")


async def recover_scheduled_midnight(
    onebot, config, game_client=None, now_fn=time.time, sleep_fn=asyncio.sleep
):
    """仅在当日 00:00 延迟窗口内恢复已持久化的任务。"""
    now_value = int(now_fn())
    now = datetime.fromtimestamp(now_value, tz=timezone.utc)
    scheduled_hour = scheduled_midnight_epoch(now, config.timezone_name)
    recoverable = _is_within_schedule_window(
        scheduled_hour, now_value, config.maximum_delay_seconds
    )
    expiration_cutoff = (
        scheduled_hour
        if recoverable
        else int(next_midnight(now, config.timezone_name).timestamp())
    )
    storage.expire_old_qq_whitelist_sync_runs(
        config.group_id, expiration_cutoff, now=now_value
    )
    if not recoverable:
        return {"status": "nothing_to_recover"}
    row = storage.get_qq_whitelist_sync_for_hour(
        config.group_id, scheduled_hour
    )
    if not row:
        return {"status": "nothing_to_recover"}
    return await execute_sync_hour(
        onebot, config, scheduled_hour, game_client, now_fn, sleep_fn
    )


async def recover_current_hour(
    onebot, config, game_client=None, now_fn=time.time, sleep_fn=asyncio.sleep
):
    """兼容旧调用名；恢复当日 00:00 计划槽。"""
    return await recover_scheduled_midnight(
        onebot, config, game_client, now_fn, sleep_fn
    )


async def recover_unreported_failure_reports(
    config, game_client=None, now_fn=time.time
):
    """只补报持久化失败事件，不重新拉群成员或重跑过期更新。"""
    game_client = game_client or GameWhitelistClient(config)
    now = int(now_fn())
    failures = storage.list_unreported_qq_whitelist_sync_failures(
        config.group_id
    )
    recovered = 0
    pending = 0
    now_datetime = datetime.fromtimestamp(now, tz=timezone.utc)
    current_hour = scheduled_midnight_epoch(now_datetime, config.timezone_name)
    current_is_recoverable = _is_within_schedule_window(
        current_hour, now, config.maximum_delay_seconds
    )
    expiration_cutoff = (
        current_hour
        if current_is_recoverable
        else int(next_midnight(now_datetime, config.timezone_name).timestamp())
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
        if current and current.get("state") == "committed":
            if (
                current_is_recoverable
                and int(current["scheduled_hour"]) == current_hour
            ):
                current_committed = True
            else:
                # 过期或旧版非零点任务只恢复权威结果，不补发群消息。
                storage.expire_old_qq_whitelist_sync_runs(
                    config.group_id, expiration_cutoff, now=now
                )
    return {
        "recovered": recovered,
        "pending": pending,
        "currentCommitted": current_committed,
    }


async def run_sync_loop(onebot, config: SyncConfig):
    """连接存续期间运行；每日仅在 Asia/Singapore 00:00 执行。"""
    if not config.enabled:
        return
    game_client = GameWhitelistClient(config)
    try:
        await recover_unreported_failure_reports(config, game_client)
    except Exception as exc:
        print(f"[QQ 白名单同步] 失败报告恢复异常，将继续恢复当日任务：{exc}")
    try:
        await recover_scheduled_midnight(onebot, config, game_client)
    except Exception as exc:
        print(f"[QQ 白名单同步] 当日任务恢复失败，每日循环继续运行：{exc}")
    while True:
        now = datetime.now(timezone.utc)
        target = next_midnight(now, config.timezone_name)
        scheduled_hour = int(target.timestamp())
        while True:
            remaining = target.timestamp() - time.time()
            if remaining <= 0:
                break
            # 分段按墙钟复核，系统校时或暂停恢复不会造成固定间隔漂移。
            await asyncio.sleep(min(remaining, 60))
            try:
                recovery = await recover_unreported_failure_reports(
                    config, game_client
                )
                if recovery["currentCommitted"]:
                    await recover_scheduled_midnight(onebot, config, game_client)
            except Exception as exc:
                print(f"[QQ 白名单同步] 失败报告恢复异常，将继续保留本地记录：{exc}")
        actual_now = int(time.time())
        if not _is_within_schedule_window(
            scheduled_hour, actual_now, config.maximum_delay_seconds
        ):
            print(
                f"[QQ 白名单同步] 错过每日 00:00 计划时间 "
                f"{scheduled_hour}，不补发过期任务"
            )
            continue
        try:
            await execute_sync_hour(
                onebot, config, scheduled_hour, game_client=game_client
            )
        except Exception as exc:
            print(f"[QQ 白名单同步] 每日任务异常，下一天 00:00 仍会继续：{exc}")
