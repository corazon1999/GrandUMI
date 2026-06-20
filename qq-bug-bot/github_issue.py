# -*- coding: utf-8 -*-
"""通过本机已登录的 gh CLI 创建 GitHub Issue。

因为机器人就跑在已 `gh auth login` 的本机上,所以直接调用 gh 命令,
无需在配置里保存 token。失败(网络/未登录)时返回 None,由调用方降级处理。
"""

import re
import subprocess


def create_issue(repo: str, title: str, body: str, timeout: int = 30):
    """创建 Issue,成功返回 (issue_no, url),失败返回 None。"""
    # gh issue create 成功时会在 stdout 打印 issue 的 URL,如:
    #   https://github.com/corazon1999/GrandUMI/issues/12
    try:
        result = subprocess.run(
            [
                "gh", "issue", "create",
                "--repo", repo,
                "--title", title,
                "--body", body,
                "--label", "bug",
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=timeout,
        )
    except (FileNotFoundError, subprocess.TimeoutExpired) as e:
        print(f"[github] 调用 gh 失败: {e}")
        return None

    if result.returncode != 0:
        # 常见原因:label 不存在。带 label 失败则重试一次不带 label。
        if "label" in (result.stderr or "").lower():
            return _create_without_label(repo, title, body, timeout)
        print(f"[github] gh 返回错误: {result.stderr.strip()}")
        return None

    return _parse_url(result.stdout)


def _create_without_label(repo: str, title: str, body: str, timeout: int):
    try:
        result = subprocess.run(
            ["gh", "issue", "create", "--repo", repo,
             "--title", title, "--body", body],
            capture_output=True, text=True, encoding="utf-8", timeout=timeout,
        )
    except (FileNotFoundError, subprocess.TimeoutExpired) as e:
        print(f"[github] 重试调用 gh 失败: {e}")
        return None
    if result.returncode != 0:
        print(f"[github] gh 重试仍失败: {result.stderr.strip()}")
        return None
    return _parse_url(result.stdout)


def _parse_url(stdout: str):
    """从 gh 输出里提取 issue URL 与编号。"""
    m = re.search(r"https://github\.com/\S+/issues/(\d+)", stdout or "")
    if not m:
        print(f"[github] 无法从输出解析 issue 编号: {stdout!r}")
        return None
    return int(m.group(1)), m.group(0)
