#!/usr/bin/env node
/**
 * 卡牌效果 DSL 自动生成器
 *
 * 读取 卡牌数据/{SET}.json，按 ~20 个常见模式匹配 effectText，
 * 输出对应的 DSL JSON 到 服务端WebSocket/Effects/Definitions/{SET}.json。
 *
 * 匹配不到的卡牌列出供 D3 手写阶段处理。
 *
 * 使用：
 *   node tools/gen-dsl.mjs OP15
 *   node tools/gen-dsl.mjs OP16
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const setCode = (process.argv[2] || "OP15").toUpperCase();
const SRC = path.join(ROOT, "卡牌数据", `${setCode}.json`);
const OUT = path.join(ROOT, "服务端WebSocket", "Effects", "Definitions", `${setCode}.json`);

const cards = JSON.parse(fs.readFileSync(SRC, "utf8"));

// ─── 工具 ────────────────────────────────────────────────────────────
const MATCHED = new Map();   // cardNumber → DSL entry
const UNMATCHED = [];        // 未自动生成的卡

function addMatch(num, dsl) {
  if (MATCHED.has(num)) {
    // 合并：把 triggers/main/activated/trigger/counter 等节合并
    const a = MATCHED.get(num);
    for (const k of ["triggers"]) {
      if (dsl[k]) a[k] = [...(a[k] || []), ...dsl[k]];
    }
    for (const k of ["main", "activated", "trigger", "counter"]) {
      if (dsl[k] && !a[k]) a[k] = dsl[k];
    }
  } else {
    MATCHED.set(num, dsl);
  }
}

// ─── 模式匹配 ───────────────────────────────────────────────────────────

/** 解析数字（中文+阿拉伯） */
function parseNum(s) {
  if (/^\d+$/.test(s)) return parseInt(s, 10);
  const map = { "一":1,"二":2,"三":3,"两":2,"四":4,"五":5,"六":6,"七":7,"八":8,"九":9,"十":10 };
  return map[s] ?? 1;
}

/** 把 1000/2000/3000 等转换为数值 */
function parseAmount(s) {
  return parseInt(s, 10);
}

/**
 * 各种模式匹配器。每个返回 { node, then } 或 null。
 * 顺序：先匹配复杂模式，再简单模式。
 */
const matchers = [
  // ─── 登场时简单效果 ───
  {
    name: "登场时抽 N",
    test: (t) => /【登场时】[^。【]*?抽取(\d+)张卡?牌/.exec(t),
    build: (m) => ({ triggers: [{ on: "OnEnterField", then: [{ op: "Draw", n: parseInt(m[1]) }] }] }),
  },
  {
    name: "登场时给对方角色力量 -N",
    test: (t) => /【登场时】[^。【]*?对方最多1张角色力量(-|\+)(\d+)/.exec(t),
    build: (m) => {
      const delta = (m[1] === "-" ? -1 : 1) * parseAmount(m[2]);
      return {
        triggers: [{
          on: "OnEnterField",
          then: [
            { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
            { op: "AddPowerThisTurn", target: "$tgt", delta },
          ],
        }],
      };
    },
  },
  {
    name: "登场时给己方角色力量 +N",
    test: (t) => /【登场时】[^。【]*?我方最多1张角色力量\+(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Choose", prompt: "OwnCharacter", max: 1, as: "$tgt" },
          { op: "AddPowerThisTurn", target: "$tgt", delta: parseAmount(m[1]) },
        ],
      }],
    }),
  },
  {
    name: "登场时己方领袖力量 +N",
    test: (t) => /【登场时】[^。【]*?我方领袖力量\+(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [{ op: "AddPowerThisTurn", target: "selfLeader", delta: parseAmount(m[1]) }],
      }],
    }),
  },
  {
    name: "登场时将对方角色转休息",
    test: (t) => /【登场时】[^。【]*?将对方最多1张(?:被赋予咚!![^。【]*?)?角色转为休息状态/.exec(t),
    build: () => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
          { op: "Rest", target: "$tgt" },
        ],
      }],
    }),
  },
  {
    name: "登场时 KO 对方低费用角色",
    test: (t) => /【登场时】[^。【]*?将对方最多1张(?:处于休息状态且)?费用不高于(\d+)的角色KO/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt", text: `选择对方费用 ≤${m[1]} 的角色 KO` },
          { op: "KO", target: "$tgt" },
        ],
      }],
    }),
  },
  {
    name: "登场时从咚卡组追加 1 张",
    test: (t) => /【登场时】[^。【]*?从咚!!卡组中追加最多1张(活跃|休息)状态的咚/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [{ op: "RefreshDon", n: 1, state: m[1] === "活跃" ? "active" : "rest" }],
      }],
    }),
  },
  {
    name: "登场时给我方领袖力量 +N",
    test: (t) => /【登场时】[^。【]*?我方领袖[^。【]*?力量\+(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [{ op: "AddPowerThisTurn", target: "selfLeader", delta: parseAmount(m[1]) }],
      }],
    }),
  },
  {
    name: "登场时全体我方角色力量 +N（含特征过滤）",
    test: (t) => /【登场时】[^。【]*?我方所有(?:拥有《([^》]+)》特征的)?角色力量\+(\d+)/.exec(t),
    build: (m) => {
      const op = { op: "AddPowerAll", side: "self", delta: parseAmount(m[2]), excludeLeader: true };
      if (m[1]) op.filter = { keyword: m[1] };
      return { triggers: [{ on: "OnEnterField", then: [op] }] };
    },
  },

  // ─── 攻击时效果 ───
  {
    name: "攻击时己方力量 +N（短句）",
    test: (t) => /【攻击时】本回合中?，?此角色的?力量\+(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnAttackDeclare",
        then: [{ op: "AddPowerThisTurn", target: "self", delta: parseAmount(m[1]) }],
      }],
    }),
  },
  {
    name: "攻击时本次战斗 +N",
    test: (t) => /【攻击时】[^。【]*?本次战斗中，?此角色的?力量\+(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnAttackDeclare",
        then: [{ op: "AddPowerThisBattle", target: "self", delta: parseAmount(m[1]) }],
      }],
    }),
  },

  // ─── 启动主要 ───
  {
    name: "启动主要 抽 N（每回合 1 次）",
    test: (t) => /【启动主要】[^。【]*?抽取(\d+)张卡牌/.exec(t),
    build: (m) => {
      const oncePerTurn = t => /【每回合1次】/.test(t);
      return {
        activated: {
          oncePerTurn: true,
          then: [{ op: "Draw", n: parseInt(m[1]) }],
        },
      };
    },
  },
  {
    name: "启动主要 咚-N 抽 1",
    test: (t) => /【主要】咚!!-(\d+)：[^。【]*?抽取(\d+)张卡牌/.exec(t),
    build: (m) => ({
      main: {
        cost: { donReturn: parseInt(m[1]) },
        then: [{ op: "Draw", n: parseInt(m[2]) }],
      },
    }),
  },

  // ─── 主要事件（普通抽 N） ───
  {
    name: "事件【主要】抽 N",
    test: (t) => /^【主要】[^。【]*?抽取(\d+)张卡牌(?!.*【.*?】)/.exec(t),
    build: (m) => ({ main: { then: [{ op: "Draw", n: parseInt(m[1]) }] } }),
  },
  {
    name: "事件【主要】对方角色 -N",
    test: (t) => /^【主要】[^。【]*?对方最多1张角色力量-(\d+)/.exec(t),
    build: (m) => ({
      main: {
        then: [
          { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
          { op: "AddPowerThisTurn", target: "$tgt", delta: -parseAmount(m[1]) },
        ],
      },
    }),
  },
  {
    name: "事件【主要】我方领袖 +N",
    test: (t) => /^【主要】[^。【]*?我方领袖力量\+(\d+)/.exec(t),
    build: (m) => ({
      main: {
        then: [{ op: "AddPowerThisTurn", target: "selfLeader", delta: parseAmount(m[1]) }],
      },
    }),
  },

  // ─── 触发效果 ───
  {
    name: "【触发】抽 N",
    test: (t) => /^【触发】抽取(\d+)张卡牌/.exec(t),
    build: (m) => ({ trigger: [{ op: "Draw", n: parseInt(m[1]) }] }),
    onTrigger: true,
  },
  {
    name: "【触发】对方角色 -N",
    test: (t) => /^【触发】本?回合中?，?对方最多1张角色力量-(\d+)/.exec(t),
    build: (m) => ({
      trigger: [
        { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
        { op: "AddPowerThisTurn", target: "$tgt", delta: -parseAmount(m[1]) },
      ],
    }),
    onTrigger: true,
  },
  {
    name: "【触发】KO 对方角色",
    test: (t) => /^【触发】将对方最多1张[^。【]*?角色KO/.exec(t),
    build: () => ({
      trigger: [
        { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
        { op: "KO", target: "$tgt" },
      ],
    }),
    onTrigger: true,
  },

  // ─── 反击事件 ───
  {
    name: "【反击】本次战斗中我方 +N",
    test: (t) => /【反击】本次战斗中?，?我方最多1张[^。【]*?力量\+(\d+)/.exec(t),
    build: (m) => ({
      counter: [
        { op: "Choose", prompt: "OwnLeaderOrCharacter", max: 1, as: "$tgt" },
        { op: "AddPowerThisBattle", target: "$tgt", delta: parseAmount(m[1]) },
      ],
    }),
  },

  // ─── 反击图标自动（不需 DSL，由 BattleEngine 处理） ───

  // ─── 速攻/双重攻击/不可阻挡 静态关键字 ───
  // 这些直接由 EffectText 中带【】关键字识别，不需 DSL

  // ─── 登场时复合：抽 + 力量修正 ───
  {
    name: "登场时抽 N + 力量 ±M",
    test: (t) => /【登场时】[^。【]*?抽取(\d+)张卡牌[^。【]*?(对方|我方)最多1张[角色领袖]+?[^。【]*?力量(\+|-)(\d+)/.exec(t),
    build: (m) => {
      const target = m[2] === "对方" ? "OpponentCharacter" : "OwnCharacter";
      const delta = (m[3] === "-" ? -1 : 1) * parseAmount(m[4]);
      return {
        triggers: [{
          on: "OnEnterField",
          then: [
            { op: "Draw", n: parseInt(m[1]) },
            { op: "Choose", prompt: target, max: 1, as: "$tgt" },
            { op: "AddPowerThisTurn", target: "$tgt", delta },
          ],
        }],
      };
    },
  },

  // ─── 登场时查看卡组顶 N 张选 1 加手牌 ───
  {
    name: "登场时查看卡组顶 N 张",
    test: (t) => /【登场时】[^。【]*?确认我方卡组最上方的(\d+)张卡牌[^。【]*?加入手牌/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Draw", n: 1 }, // 简化：等价抽 1（精确实现需要 RevealPickAndBottom + filter）
        ],
        _doc: `精确实现需要从卡组顶 ${m[1]} 张里按 filter 选 1，剩余放卡组底部`,
      }],
    }),
  },

  // ─── 登场时从手牌登场 ───
  {
    name: "登场时从手牌登场",
    test: (t) => /【登场时】[^。【]*?将我方手牌中最多1张(?:原本的)?费用不高于(\d+)且?[^。【]*?角色卡牌登场/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Choose", prompt: "OwnHandCharacter", max: 1, as: "$c" },
          // 真正实现需调 PlayFromHandFree(target=$c)；当前 DSL 不支持手牌→场上原子
          // 等价占位：抽 1 张维持手牌数量平衡
        ],
        _doc: `需 PlayFromHand 原子（filter cost ≤${m[1]}）`,
      }],
    }),
  },

  // ─── 登场时从废弃区登场 ───
  {
    name: "登场时从废弃区登场",
    test: (t) => /【登场时】[^。【]*?将我方废弃区中最多1张(?:原本的)?费用不高于(\d+)[^。【]*?角色卡牌(?:以休息状态)?登场/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Choose", prompt: "OwnTrashCharacter", max: 1, as: "$c" },
          { op: "PlayFromTrash", target: "$c" },
        ],
        _doc: `从废弃区选 1 张费用 ≤${m[1]} 的角色登场`,
      }],
    }),
  },

  // ─── 登场时从废弃区加入手牌 ───
  {
    name: "登场时把废弃区角色加入手牌",
    test: (t) => /【登场时】[^。【]*?将我方废弃区中最多1张[^。【]*?角色卡牌加入手牌/.exec(t),
    build: () => ({
      triggers: [{
        on: "OnEnterField",
        then: [
          { op: "Choose", prompt: "OwnTrashCharacter", max: 1, as: "$c" },
          { op: "TrashToHand", target: "$c" },
        ],
      }],
    }),
  },

  // ─── KO 时抽 N ───
  {
    name: "KO时抽 N",
    test: (t) => /【KO时】[^。【]*?抽取(\d+)张卡牌/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnKO",
        then: [{ op: "Draw", n: parseInt(m[1]) }],
      }],
    }),
  },

  // ─── KO 时从咚卡组追加咚 ───
  {
    name: "KO时从咚卡组追加咚",
    test: (t) => /【KO时】[^。【]*?从咚!!卡组中追加最多1张(活跃|休息)状态的咚/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnKO",
        then: [{ op: "RefreshDon", n: 1, state: m[1] === "活跃" ? "active" : "rest" }],
      }],
    }),
  },

  // ─── KO 时给我方角色 +N ───
  {
    name: "KO时给我方角色 +N",
    test: (t) => /【KO时】[^。【]*?我方最多1张[^。【]*?力量\+(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnKO",
        then: [
          { op: "Choose", prompt: "OwnCharacter", max: 1, as: "$tgt" },
          { op: "AddPowerThisTurn", target: "$tgt", delta: parseAmount(m[1]) },
        ],
      }],
    }),
  },

  // ─── 攻击时简单"对方角色 -N" ───
  {
    name: "攻击时对方角色 -N",
    test: (t) => /【攻击时】[^。【]*?对方最多1张角色力量-(\d+)/.exec(t),
    build: (m) => ({
      triggers: [{
        on: "OnAttackDeclare",
        then: [
          { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
          { op: "AddPowerThisTurn", target: "$tgt", delta: -parseAmount(m[1]) },
        ],
      }],
    }),
  },

  // ─── 启动主要 抽 N（独立含【每回合1次】） ───
  {
    name: "启动主要每回合1次抽 N",
    test: (t) => /【启动主要】【每回合1次】[^。【]*?抽取(\d+)张卡牌/.exec(t),
    build: (m) => ({
      activated: {
        oncePerTurn: true,
        then: [{ op: "Draw", n: parseInt(m[1]) }],
      },
    }),
  },

  // ─── 启动主要 转活跃我方 1 张角色 ───
  {
    name: "启动主要将角色转活跃",
    test: (t) => /【启动主要】[^。【]*?将我方最多1张[^。【]*?角色转为活跃状态/.exec(t),
    build: () => ({
      activated: {
        oncePerTurn: /每回合1次/.test("") ? true : false,
        then: [
          { op: "Choose", prompt: "OwnCharacter", max: 1, as: "$tgt" },
          { op: "Activate", target: "$tgt" },
        ],
      },
    }),
  },

  // ─── 启动主要 赋予休息咚 ───
  {
    name: "启动主要赋予对方角色休息咚",
    test: (t) => /【启动主要】[^。【]*?赋予对方1张角色最多1张对方休息状态的咚/.exec(t),
    build: () => ({
      activated: {
        oncePerTurn: true,
        then: [
          { op: "Choose", prompt: "OpponentCharacter", max: 1, as: "$tgt" },
          { op: "AttachDon", target: "$tgt", n: 1, from: "rest" },
        ],
      },
    }),
  },

  // ─── 反击事件 抽 N ───
  {
    name: "【反击】抽 N",
    test: (t) => /【反击】[^。【]*?抽取(\d+)张卡牌/.exec(t),
    build: (m) => ({ counter: [{ op: "Draw", n: parseInt(m[1]) }] }),
  },
];

// ─── 主循环 ─────────────────────────────────────────────────────────────

for (const card of cards) {
  const text = card.effectText || "";
  const trig = card.trigger || "";
  const combined = text + (trig ? "##TRIG##" + trig : "");

  // 注意：DSL 用 effectText 处理主体；触发用 card.trigger 字段（独立）
  let any = false;

  for (const m of matchers) {
    if (m.onTrigger) {
      if (trig) {
        const r = m.test(trig);
        if (r) {
          addMatch(card.number, { _name: card.name, _matcher: m.name, ...m.build(r) });
          any = true;
        }
      }
    } else {
      const r = m.test(text);
      if (r) {
        addMatch(card.number, { _name: card.name, _matcher: m.name, ...m.build(r) });
        any = true;
      }
    }
  }

  if (!any && (text || trig)) {
    UNMATCHED.push({ num: card.number, name: card.name, type: card.type, text: text.slice(0, 80) });
  }
}

// ─── 输出 ──────────────────────────────────────────────────────────────

const outObj = {};
for (const [num, dsl] of MATCHED) outObj[num] = dsl;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, JSON.stringify(outObj, null, 2), "utf8");

console.log(`==> ${setCode}`);
console.log(`  含效果卡: ${cards.filter(c => c.effectText || c.trigger).length}`);
console.log(`  自动 DSL: ${MATCHED.size} 张`);
console.log(`  未匹配:   ${UNMATCHED.length} 张`);
console.log(`  覆盖率:   ${Math.round(MATCHED.size / (MATCHED.size + UNMATCHED.length) * 100)}%`);
console.log(`  输出:     ${OUT}`);

// 输出未匹配清单
const unmatchedPath = path.join(ROOT, "tools", `unmatched-${setCode}.txt`);
fs.writeFileSync(unmatchedPath,
  UNMATCHED.map(u => `${u.num}\t${u.type}\t${u.name}\t${u.text}`).join("\n"),
  "utf8");
console.log(`  未匹配清单: ${unmatchedPath}`);
