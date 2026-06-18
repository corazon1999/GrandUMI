# -*- coding: utf-8 -*-
"""命令行标记反馈的修复状态,写回 feedback.db。

用法:
  py mark.py <编号> <status> [备注]
  status: fixed(已修) / wontfix(非bug) / open(待修)
  py mark.py <编号> dup <主编号>     # 把该条标成主条目的重复
  py mark.py <编号> dup 0            # 取消重复标记

例:
  py mark.py 8 fixed "OP16-042不限张数已修复"
  py mark.py 10 wontfix "角标1卡,搜索面板开『显示角标1卡』可见"
  py mark.py 13 open
  py mark.py 51 dup 49              # #51 是 #49 的重复(修 #49 时 #51 一起闭环)

标记后建议重跑 `py export_by_date.py` 刷新日报(或说"导出bug日报")。
查疑似重复用 `py dedup.py`。
"""

import sys

import storage


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")  # Windows 控制台 GBK 兜底
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    try:
        fid = int(sys.argv[1])
    except ValueError:
        sys.exit(f"编号必须是数字: {sys.argv[1]}")

    status = sys.argv[2].lower()
    note = sys.argv[3] if len(sys.argv) > 3 else None

    storage.init_db()

    # dup 子命令:py mark.py <编号> dup <主编号>
    if status == "dup":
        if note is None:
            sys.exit("用法: py mark.py <编号> dup <主编号>(取消用 0)")
        try:
            main_id = int(note)
        except ValueError:
            sys.exit(f"主编号必须是数字: {note}")
        try:
            ok = storage.set_dup(fid, main_id)
        except ValueError as e:
            sys.exit(str(e))
        if not ok:
            print(f"未找到反馈 #{fid}")
        elif main_id in (0, fid):
            print(f"反馈 #{fid} 已取消重复标记")
        else:
            print(f"反馈 #{fid} 已标记为 #{main_id} 的重复(修主条目时会带动它一起闭环)")
        return

    try:
        ok = storage.set_status(fid, status, note)
    except ValueError as e:
        sys.exit(str(e))

    label = {"fixed": "已修复", "wontfix": "非bug", "open": "待修"}.get(status, status)
    if ok:
        msg = f"反馈 #{fid} 状态已更新为 {status}({label})"
        if note:
            msg += f",备注: {note}"
        print(msg)
    else:
        print(f"未找到反馈 #{fid}")


if __name__ == "__main__":
    main()
