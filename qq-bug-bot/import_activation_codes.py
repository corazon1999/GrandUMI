# -*- coding: utf-8 -*-
"""从标准输入安全、幂等地导入群领取激活码库存。"""

import argparse
import json
import re
import sys

import storage


MAX_INPUT_BYTES = 2 * 1024 * 1024
_CODE_RE = re.compile(r"^[A-Za-z0-9-]{17}$")
_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class ActivationCodeInputError(ValueError):
    """输入验证失败；异常消息只描述位置或类型，绝不包含激活码。"""


def parse_activation_code_bytes(payload: bytes) -> tuple[list[str], str]:
    if not payload:
        raise ActivationCodeInputError("激活码文件为空")
    if len(payload) > MAX_INPUT_BYTES:
        raise ActivationCodeInputError("激活码文件超过安全大小限制")
    try:
        text = payload.decode("utf-8-sig", errors="strict")
    except UnicodeDecodeError as exc:
        raise ActivationCodeInputError("激活码文件不是有效 UTF-8") from exc
    lines = text.splitlines()
    if not lines:
        raise ActivationCodeInputError("激活码文件为空")
    for index, code in enumerate(lines, start=1):
        if code != code.strip() or not _CODE_RE.fullmatch(code):
            raise ActivationCodeInputError(f"第 {index} 行激活码格式无效")
    if len(set(lines)) != len(lines):
        raise ActivationCodeInputError("激活码文件包含重复项")
    return lines, storage.activation_code_digest(lines)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="从标准输入导入激活码；输出仅包含计数与 SHA-256。"
    )
    parser.add_argument("--stdin", action="store_true", required=True)
    parser.add_argument("--expected-count", type=int, required=True)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--backup-dir", required=True)
    return parser


def main(argv=None) -> int:
    args = build_parser().parse_args(argv)
    expected_sha256 = str(args.expected_sha256 or "").strip().lower()
    if args.expected_count <= 0:
        print("预期激活码数量无效", file=sys.stderr)
        return 2
    if not _SHA256_RE.fullmatch(expected_sha256):
        print("预期 SHA-256 无效", file=sys.stderr)
        return 2
    try:
        codes, digest = parse_activation_code_bytes(sys.stdin.buffer.read(MAX_INPUT_BYTES + 1))
        if len(codes) != args.expected_count or digest != expected_sha256:
            raise ActivationCodeInputError("激活码数量或 SHA-256 与预期不一致")
        storage.init_activation_code_db()
        storage.backup_activation_code_db(args.backup_dir, "pre-import")
        result = storage.import_activation_codes(codes, digest)
        storage.backup_activation_code_db(args.backup_dir, "post-import")
        summary = storage.get_activation_code_inventory_summary()
    except ActivationCodeInputError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    except Exception:
        print("激活码导入失败；未输出任何库存内容", file=sys.stderr)
        return 1
    print(
        json.dumps(
            {
                "input_count": result["input_count"],
                "inserted_count": result["inserted_count"],
                "existing_count": result["existing_count"],
                "total_count": summary["total_count"],
                "available_count": summary["available_count"],
                "allocated_count": summary["allocated_count"],
                "sha256": summary["inventory_sha256"],
            },
            ensure_ascii=False,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
