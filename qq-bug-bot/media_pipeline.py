# -*- coding: utf-8 -*-
"""QQ 图片的受限下载、校验与临时文件清理。"""

import hashlib
import ipaddress
import os
import socket
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from uuid import uuid4


MEDIA_DIR = Path(os.environ.get("BUG_BOT_MEDIA_DIR", "/data/media"))
DEFAULT_MAX_IMAGE_BYTES = 8 * 1024 * 1024


def _resolve_public_host(hostname: str) -> None:
    """拒绝回环、内网、链路本地等地址，避免图片 URL 被用于 SSRF。"""
    try:
        addresses = {
            item[4][0]
            for item in socket.getaddrinfo(
                hostname, None, type=socket.SOCK_STREAM
            )
        }
    except socket.gaierror as exc:
        raise ValueError("图片地址无法解析") from exc
    if not addresses:
        raise ValueError("图片地址没有可用 IP")
    for value in addresses:
        address = ipaddress.ip_address(value.split("%", 1)[0])
        if not address.is_global:
            raise ValueError("图片地址指向非公网 IP")


def validate_image_url(url: str) -> str:
    value = str(url or "").strip()
    parsed = urllib.parse.urlsplit(value)
    if parsed.scheme not in ("http", "https"):
        raise ValueError("图片地址只允许 HTTP/HTTPS")
    if not parsed.hostname or parsed.username or parsed.password:
        raise ValueError("图片地址格式无效")
    if parsed.port not in (None, 80, 443):
        raise ValueError("图片地址端口不受支持")
    _resolve_public_host(parsed.hostname)
    return value


class _SafeRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        validate_image_url(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def detect_image_format(data: bytes):
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "png", "image/png"
    if data.startswith(b"\xff\xd8\xff"):
        return "jpg", "image/jpeg"
    if len(data) >= 12 and data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "webp", "image/webp"
    raise ValueError("仅支持 PNG、JPEG 或 WebP 图片")


def download_image(url: str, max_bytes: int = DEFAULT_MAX_IMAGE_BYTES) -> dict:
    """下载一张 NapCat 提供的图片，返回可安全跨 SSH 传递的元数据。"""
    checked_url = validate_image_url(url)
    limit = max(64 * 1024, min(20 * 1024 * 1024, int(max_bytes)))
    opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}), _SafeRedirectHandler()
    )
    request = urllib.request.Request(
        checked_url,
        headers={
            "User-Agent": "GrandUMI-QQ-Vision/1.0",
            "Accept": "image/png,image/jpeg,image/webp",
        },
    )
    try:
        with opener.open(request, timeout=20) as response:
            length = response.headers.get("Content-Length")
            if length and int(length) > limit:
                raise ValueError("图片超过大小限制")
            data = response.read(limit + 1)
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        raise ValueError("图片下载失败") from exc
    if len(data) > limit:
        raise ValueError("图片超过大小限制")
    extension, mime = detect_image_format(data)
    MEDIA_DIR.mkdir(parents=True, exist_ok=True)
    name = f"{uuid4().hex}.{extension}"
    target = MEDIA_DIR / name
    with target.open("xb") as file:
        file.write(data)
    return {
        "name": name,
        "size": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "mime": mime,
    }


def validate_media_name(name: str) -> str:
    value = str(name or "")
    stem, dot, extension = value.partition(".")
    if (
        len(stem) != 32
        or any(ch not in "0123456789abcdef" for ch in stem)
        or dot != "."
        or extension not in ("png", "jpg", "webp")
    ):
        raise ValueError("图片临时文件名无效")
    return value


def cleanup_media(items) -> int:
    removed = 0
    for item in items or []:
        try:
            name = validate_media_name((item or {}).get("name"))
            target = (MEDIA_DIR / name).resolve()
            if target.parent != MEDIA_DIR.resolve():
                continue
            target.unlink(missing_ok=True)
            removed += 1
        except (OSError, TypeError, ValueError):
            continue
    return removed


def cleanup_expired_media(max_age_seconds: int = 86400) -> int:
    if not MEDIA_DIR.is_dir():
        return 0
    cutoff = time.time() - max(3600, int(max_age_seconds))
    removed = 0
    for target in MEDIA_DIR.iterdir():
        try:
            validate_media_name(target.name)
            if target.is_file() and target.stat().st_mtime < cutoff:
                target.unlink()
                removed += 1
        except (OSError, ValueError):
            continue
    return removed
