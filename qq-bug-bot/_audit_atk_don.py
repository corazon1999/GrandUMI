# -*- coding: utf-8 -*-
"""#127 审计:核出所有【咚!!×1】【攻击时】卡,逐张采集 effectTags/DSL/脚本 现状并分类。只读,不改任何文件。"""
import json, glob, os, re, sys
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"D:\Self\GrandUMI"
DEFS = os.path.join(ROOT, r"服务端WebSocket\Effects\Definitions")
SCR  = os.path.join(ROOT, r"服务端WebSocket\Effects\Scripted")
DATA = os.path.join(ROOT, "卡牌数据")
ORIG = os.path.join(ROOT, "卡牌数据_含原文")

# 1) 真实清单:effectText 含 "咚!!×1】【攻击时"(允许两种顺序)
def has_atk_don(t):
    return ("咚!!×1】【攻击时" in t) or ("攻击时】【咚!!×1" in t)
cards = {}  # number -> effectText
for f in glob.glob(os.path.join(ORIG, "*.json")):
    try: d = json.load(open(f, encoding="utf-8"))
    except: continue
    if not isinstance(d, list): continue
    for c in d:
        t = c.get("effectText") or ""
        if has_atk_don(t):
            cards[c["number"]] = t

# 2) effectTags(卡牌数据/{SET}.json)
def set_of(n): return n.split("-")[0]
tags_cache = {}
def load_tags(s):
    if s in tags_cache: return tags_cache[s]
    p = os.path.join(DATA, s + ".json"); m = {}
    if os.path.exists(p):
        for c in json.load(open(p, encoding="utf-8")):
            m[c.get("number")] = c.get("effectTags") or []
    tags_cache[s] = m; return m

# 3) DSL(Definitions/{SET}.json + {SET}_wf.json + _gap),找 OnAttackDeclare 触发 + 咚门槛
defs_cache = {}
def load_def(fn):
    if fn in defs_cache: return defs_cache[fn]
    p = os.path.join(DEFS, fn); obj = {}
    if os.path.exists(p):
        try: obj = json.load(open(p, encoding="utf-8"))
        except: obj = {}
    defs_cache[fn] = obj; return obj
GATE_KEYS = ("donAttachedGte","selfDonAttachedGte","attachedDonTotalGte")
def dsl_status(n):
    s = set_of(n)
    for fn in (s+".json", s+"_wf.json", s+"_gap.json", "_GAP_wf.json", "OP12_gap.json"):
        d = load_def(fn).get(n)
        if not d: continue
        trigs = d.get("triggers")
        if isinstance(trigs, list):
            for t in trigs:
                if t.get("on") == "OnAttackDeclare":
                    gate = bool(t.get("if")) and any(k in (t.get("if") or {}) for k in GATE_KEYS)
                    ops = [st.get("op") for st in (t.get("then") or []) if isinstance(st,dict)]
                    return ("DSL", fn, gate, ops)
        # main/activated 不算攻击时
    return (None, None, False, [])

# 4) 脚本
scr_files = os.listdir(SCR)
def script_status(n):
    pre = n.replace("-", "_")  # OP01-015 -> OP01_015
    for f in scr_files:
        if f.startswith(pre):
            txt = open(os.path.join(SCR, f), encoding="utf-8").read()
            handles_atk = "OnAttackDeclare" in txt and "HandlesTrigger" in txt
            has_gate = "AttachedDonCount" in txt
            return (f, handles_atk, has_gate)
    return (None, False, False)

# 汇总分类
rows = []
for n in sorted(cards):
    tags = load_tags(set_of(n)).get(n, [])
    has_tag = "OnAttackDeclare" in tags
    dsl_kind, dsl_fn, dsl_gate, dsl_ops = dsl_status(n)
    scr_f, scr_atk, scr_gate = script_status(n)
    impl = dsl_kind or (scr_f and scr_atk)
    # 分类
    if not impl:
        cls = "❌缺条目(无OnAttackDeclare实现)"
    elif not has_tag:
        cls = "❌缺effectTags(有实现但收集不到)"
    elif scr_f and scr_atk:
        cls = "🔶脚本(需人工核对收益+门槛%s)" % ("有咚门槛" if scr_gate else "⚠无AttachedDonCount")
    elif dsl_kind == "DSL":
        cls = "✅DSL%s" % ("(有咚门槛)" if dsl_gate else "⚠无咚门槛")
    else:
        cls = "?未知"
    rows.append((n, cls, has_tag, dsl_fn or scr_f or "-", dsl_ops, cards[n][:40]))

print("== #127 审计:【咚!!×1】【攻击时】卡共 %d 张 ==\n" % len(rows))
for n, cls, tag, src, ops, txt in rows:
    print("%-9s %-30s tag=%-5s src=%-16s ops=%s" % (n, cls, tag, src, ",".join(ops) if ops else "-"))

# 统计
from collections import Counter
cnt = Counter(r[1].split("(")[0] for r in rows)
print("\n== 分类统计 ==")
for k, v in cnt.most_common(): print("  %s : %d" % (k, v))
