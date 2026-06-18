# -*- coding: utf-8 -*-
"""按日期把 feedback.db 里的 bug 反馈归整成每日 Markdown 清单。

用途:每天重跑一次,在 reports/ 下生成 YYYY-MM-DD.md(当天反馈清单)
     和 README.md(按日期倒序的索引),方便交给同事按天修 bug。

运行: py export_by_date.py
依赖: 仅标准库。读 feedback.db,只写 reports/,不改动数据库。
"""

import os
import re
import sqlite3
from collections import defaultdict

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(BASE_DIR, "feedback.db")
REPORTS_DIR = os.path.join(BASE_DIR, "reports")
# Issue 链接指向的仓库(与 bot 默认 github_repo 一致)
GITHUB_REPO = "corazon1999/GrandUMI"

# 修复状态 -> 报告里「修复」列的标记
STATUS_MARK = {"open": "☐", "fixed": "☑", "wontfix": "🚫"}

# 灌水/测试关键词:内容命中其一即归入「疑似无效」区
NOISE_KEYWORDS = [
    "测试", "功能测试", "测试功能", "test",
    "谢谢", "感谢", "帅爆", "牛逼", "666", "哈哈",
]


def is_noise(content: str) -> bool:
    """保守判定:去空白后过短,或纯灌水关键词,才算无效。"""
    c = content.strip()
    if len(c) <= 3:
        return True
    low = c.lower()
    # 内容很短(<=8 字)且命中灌水词才算,避免误伤"测试时崩溃"这类真反馈
    if len(c) <= 8:
        for kw in NOISE_KEYWORDS:
            if kw in low:
                return True
    # 极短的纯感谢/夸赞,无论长短都算灌水
    for kw in ("谢谢", "感谢", "帅爆", "牛逼"):
        if kw in low and len(c) <= 12:
            return True
    return False


def md_escape(text: str) -> str:
    """转义表格里会破坏 Markdown 的字符。"""
    return text.replace("|", "\\|").replace("\n", " ").strip()


def issue_cell(issue_no) -> str:
    if issue_no is None:
        return "—"
    return f"[#{issue_no}](https://github.com/{GITHUB_REPO}/issues/{issue_no})"


def reporter_cell(nickname: str, qq: str) -> str:
    nick = md_escape(nickname or "")
    return f"{nick}<br>(QQ {qq})" if nick else f"QQ {qq}"


def load_rows():
    with sqlite3.connect(DB_PATH) as conn:
        return conn.execute(
            "SELECT id, qq, nickname, group_id, content, issue_no, created_at, "
            "status, fix_note, dup_of FROM feedback ORDER BY created_at, id"
        ).fetchall()


def content_with_note(content: str, fix_note, dup_of=None) -> str:
    cell = md_escape(content)
    if dup_of:
        cell = f"↳ 重复于 #{dup_of} · " + cell
    if fix_note:
        cell += f" — {md_escape(fix_note)}"
    return cell


def build_table(rows) -> str:
    """生成有效反馈表格。"""
    lines = [
        "| 修复 | 编号 | 内容 | 上报人 | Issue | 时间 |",
        "| :--: | :--: | --- | --- | :--: | --- |",
    ]
    for fid, qq, nickname, _gid, content, issue_no, created_at, status, fix_note, dup_of in rows:
        t = created_at.split("T")[-1] if "T" in created_at else created_at
        mark = STATUS_MARK.get(status, "☐")
        lines.append(
            f"| {mark} | {fid} | {content_with_note(content, fix_note, dup_of)} | "
            f"{reporter_cell(nickname, qq)} | {issue_cell(issue_no)} | {t} |"
        )
    return "\n".join(lines)


def build_noise_section(rows) -> str:
    if not rows:
        return ""
    out = [
        "",
        "<details>",
        f"<summary>疑似无效 / 灌水测试({len(rows)} 条,点击展开)</summary>",
        "",
        "| 编号 | 内容 | 上报人 | 时间 |",
        "| :--: | --- | --- | --- |",
    ]
    for fid, qq, nickname, _gid, content, _issue, created_at, _status, fix_note, dup_of in rows:
        t = created_at.split("T")[-1] if "T" in created_at else created_at
        out.append(
            f"| {fid} | {content_with_note(content, fix_note, dup_of)} | {reporter_cell(nickname, qq)} | {t} |"
        )
    out.append("")
    out.append("</details>")
    return "\n".join(out)


def write_day_report(date: str, day_rows) -> tuple:
    valid = [r for r in day_rows if not is_noise(r[4])]
    noise = [r for r in day_rows if is_noise(r[4])]
    # 重复条(dup_of 非空)不计入「有效」分母,但仍在表里显示并标注
    primary = [r for r in valid if r[9] is None]
    dup_cnt = len(valid) - len(primary)
    done = sum(1 for r in primary if r[7] in ("fixed", "wontfix"))

    dup_note = f",其中 {dup_cnt} 条为重复(已折算)" if dup_cnt else ""
    md = [
        f"# {date} bug 反馈清单",
        "",
        f"> 共 {len(day_rows)} 条:有效 **{len(primary)}** 条(已处理 {done}){dup_note},疑似无效 {len(noise)} 条。",
        '> 「修复」列:☐待修 ☑已修 🚫非bug。改完用 `py mark.py <编号> fixed "备注"` 写回数据库,重跑导出不会被刷掉。',
        '> 重复反馈用 `py mark.py <重复编号> dup <主编号>` 归并(修主条目时自动带动);查疑似重复用 `py dedup.py`。',
        "",
    ]
    if valid:
        md.append(build_table(valid))
    else:
        md.append("_当天没有有效反馈。_")
    noise_md = build_noise_section(noise)
    if noise_md:
        md.append(noise_md)
    md.append("")

    path = os.path.join(REPORTS_DIR, f"{date}.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(md))
    return len(primary), len(noise), done


def write_index(stats) -> None:
    """stats: list of (date, valid, noise, done),按日期倒序写索引。"""
    lines = [
        "# GrandUMI bug 反馈日报索引",
        "",
        "由 `export_by_date.py` 自动生成。每天重跑刷新。",
        "",
        "| 日期 | 有效 | 已处理 | 疑似无效 | 文件 |",
        "| --- | :--: | :--: | :--: | --- |",
    ]
    total_valid = total_noise = total_done = 0
    for date, valid, noise, done in sorted(stats, key=lambda x: x[0], reverse=True):
        total_valid += valid
        total_noise += noise
        total_done += done
        lines.append(
            f"| {date} | {valid} | {done} | {noise} | [{date}.md](./{date}.md) |"
        )
    lines.append(
        f"| **合计** | **{total_valid}** | **{total_done}** | **{total_noise}** | |"
    )
    lines.append("")
    with open(os.path.join(REPORTS_DIR, "README.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def main():
    if not os.path.exists(DB_PATH):
        raise SystemExit(f"找不到数据库 {DB_PATH}")
    os.makedirs(REPORTS_DIR, exist_ok=True)

    by_date = defaultdict(list)
    for row in load_rows():
        created_at = row[6]
        date = created_at.split("T")[0] if "T" in created_at else created_at[:10]
        by_date[date].append(row)

    stats = []
    for date, day_rows in by_date.items():
        valid, noise, done = write_day_report(date, day_rows)
        stats.append((date, valid, noise, done))
        print(f"[{date}] 有效 {valid} 条(已处理 {done}),疑似无效 {noise} 条 -> reports/{date}.md")

    write_index(stats)
    print(f"索引已生成 -> reports/README.md(共 {len(stats)} 天)")


if __name__ == "__main__":
    main()
