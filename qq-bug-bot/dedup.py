# -*- coding: utf-8 -*-
"""疑似重复反馈查重(只读分析,不改数据库)。

思路:绝大多数 bug 锁定到一张卡,所以「重复」主要靠卡号归一来精确识别
     (OP16-92 ↔ OP16-092 视为同一张);卡号对不上或纯功能描述的,
     按功能关键词粗分到「待人工确认」区——不自动归并,列出来等人拍板。

运行: py dedup.py            # 只看 open 的疑似重复
      py dedup.py --all      # 含已处理的也一并扫
依赖: 仅标准库,读 feedback.db。

确认重复后用: py mark.py <重复编号> dup <主编号>
"""

import os
import re
import sqlite3
import sys
from collections import defaultdict

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(BASE_DIR, "feedback.db")

# 卡号:前缀(OP/ST/EB/PRB)+两位包号-编号,中间可能夹空格/全角,统一归一为 PRE##-###
CARD_RE = re.compile(r"(OP|ST|EB|PRB)\s*0?(\d{2})\s*[-_·]\s*(\d{1,3})", re.I)

# 无卡号时的功能关键词归类(命中第一个即归入该类,顺序即优先级)
FEATURE_KEYWORDS = [
    ("检索/底卡", ["检索", "底卡", "顶卡", "查看牌库", "查询墓地"]),
    ("断线重连", ["断线", "掉线", "重连", "重新连接"]),
    ("手牌/界面遮挡", ["盖住", "遮挡", "挡住", "看不见", "看不到", "比例", "横屏", "竖屏"]),
    ("卡组编辑", ["卡组", "新建卡组", "组卡", "卡表"]),
    ("抽牌/血区", ["抽牌", "抽卡", "血区", "生命"]),
    ("登场/效果", ["登场", "触发", "效果", "发动"]),
]


def norm_card(pre: str, pkg: str, num: str) -> str:
    return f"{pre.upper()}{pkg}-{int(num):03d}"


def feature_of(content: str) -> str:
    c = content
    for label, kws in FEATURE_KEYWORDS:
        if any(kw in c for kw in kws):
            return label
    return "其它/未归类"


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    scan_all = "--all" in sys.argv[1:]

    with sqlite3.connect(DB_PATH) as conn:
        where = "" if scan_all else "WHERE status = 'open'"
        rows = conn.execute(
            f"SELECT id, content, status, dup_of FROM feedback {where} ORDER BY id"
        ).fetchall()

    by_card = defaultdict(list)   # 卡号 -> [(id, content)]
    no_card = []                  # 无卡号 [(id, content)]
    already = []                  # 已经标过 dup 的,提示一下
    for fid, content, _status, dup_of in rows:
        if dup_of is not None:
            already.append((fid, dup_of))
            continue
        m = CARD_RE.search(content.replace("　", " "))
        if m:
            by_card[norm_card(*m.groups())].append((fid, content))
        else:
            no_card.append((fid, content))

    scope = "全部反馈" if scan_all else "open 反馈"
    print(f"=== 查重范围: {scope}(共 {len(rows)} 条,已标重复 {len(already)} 条已跳过)===\n")

    print("【高置信·同卡号疑似重复】(直接 py mark.py <重复> dup <主>)")
    found = False
    for card, items in sorted(by_card.items()):
        if len(items) < 2:
            continue
        found = True
        main_id = items[0][0]  # 编号最小的当主条目
        print(f"  ◆ {card}  →  建议主条目 #{main_id}")
        for fid, content in items:
            tag = " (主)" if fid == main_id else " ↳ 重复?"
            print(f"      #{fid}{tag}  {content[:48]}")
    if not found:
        print("  (无同卡号重复)")

    print("\n【待人工确认·无卡号功能反馈】(按功能粗分,跨卡号同问题也在此找)")
    by_feature = defaultdict(list)
    for fid, content in no_card:
        by_feature[feature_of(content)].append((fid, content))
    for label, items in by_feature.items():
        print(f"  ▶ {label}({len(items)})")
        for fid, content in items:
            print(f"      #{fid}  {content[:48]}")

    print("\n提示:同卡号的可直接归并;功能类/跨卡号拿不准的,挑出来问一句再定,别死磕。")


if __name__ == "__main__":
    main()
