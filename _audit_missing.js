// 全卡池审计：哪些"有效果文字"的卡尚未实现（既无 DSL 定义、也无 C# 脚本）。
// 实现源：Definitions/*.json 顶层键(卡号) + Scripted/*.cs 里的 CardNumber。
// 纯关键词卡(effectText 仅【阻挡者】【速攻】【双重攻击】【不可阻挡】【流放】等)由引擎自带，不算缺口。
const fs = require('fs');
const path = require('path');
const ROOT = 'D:/Self/GrandUMI';

// 1. DSL 已实现卡号（所有 Definitions/*.json 顶层键）
const dsl = new Set();
const defDir = ROOT + '/服务端WebSocket/Effects/Definitions';
for (const f of fs.readdirSync(defDir).filter(f => f.endsWith('.json'))) {
  try {
    const obj = JSON.parse(fs.readFileSync(path.join(defDir, f), 'utf8'));
    for (const k of Object.keys(obj)) if (/^[A-Z]/.test(k)) dsl.add(k);
  } catch (e) { console.error('解析失败', f, e.message); }
}

// 2. 脚本已实现卡号（预先 grep 到 _scripted_nums.txt）
const scripted = new Set(fs.readFileSync(ROOT + '/_scripted_nums.txt', 'utf8').split(/\r?\n/).filter(Boolean));
// 2b. HandStaticCost.cs 里 case "OPxx-yyy" 形式实现的手牌静态减费卡，也算已实现
try {
  const hsc = fs.readFileSync(ROOT + '/服务端WebSocket/Effects/HandStaticCost.cs', 'utf8');
  for (const m of hsc.matchAll(/case\s+"([A-Z0-9-]+)"/g)) scripted.add(m[1]);
} catch (e) {}

// 纯关键词判定：去掉关键词【…】及其后的（…）说明、标点空白后为空
const KW = ['阻挡者', '速攻', '双重攻击', '不可阻挡', '流放'];
function isPureKeyword(text) {
  let t = text.trim();
  if (t === '-' || t === '') return false; // 无文字不在统计范围
  // 去掉"规则上，……。"建卡/规则文（非战场效果，如"可放任意张入卡组""卡名也视为X"）
  t = t.replace(/规则上[，,][^。]*。?/g, '');
  // 去掉 【关键词】 及紧随的（半角或全角说明）
  for (const k of KW) {
    const re = new RegExp('【' + k + '】\\s*[（(][^）)]*[）)]|【' + k + '】', 'g');
    t = t.replace(re, '');
  }
  t = t.replace(/[\s　、。，,.“”"'：:]/g, '');
  return t === '';
}

const SETS = [];
for (const f of fs.readdirSync(ROOT + '/卡牌数据_含原文').filter(f => /^(OP|EB|ST|P|PRB)/.test(f) && f.endsWith('.json'))) SETS.push(f.replace('.json', ''));
SETS.sort();

const report = {};
let totalMissing = 0, totalPureKw = 0;
const missingList = [];
for (const set of SETS) {
  const raw = JSON.parse(fs.readFileSync(ROOT + '/卡牌数据_含原文/' + set + '.json', 'utf8'));
  const arr = Array.isArray(raw) ? raw : Object.values(raw);
  let withEff = 0, impl = 0, pureKw = 0, miss = 0;
  const missNums = [];
  for (const c of arr) {
    const txt = (c.effectText || '').trim();
    const hasTrigger = (c.trigger || '').trim() && c.trigger.trim() !== '-';
    if ((!txt || txt === '-') && !hasTrigger) continue; // 无任何效果文字
    withEff++;
    if (dsl.has(c.number) || scripted.has(c.number)) { impl++; continue; }
    if (!hasTrigger && isPureKeyword(txt)) { pureKw++; continue; } // 纯关键词无需实现
    miss++;
    missNums.push(c.number);
    missingList.push({ set, number: c.number, name: c.name, effectText: txt || ('【触发】' + (c.trigger || '')) });
  }
  report[set] = { withEff, impl, pureKw, miss, missNums };
  totalMissing += miss; totalPureKw += pureKw;
}

console.log('系列 | 有效果 | 已实现 | 纯关键词 | 未实现');
for (const set of SETS) {
  const r = report[set];
  if (r.miss > 0) console.log(`${set} | ${r.withEff} | ${r.impl} | ${r.pureKw} | **${r.miss}** → ${r.missNums.join(',')}`);
  else console.log(`${set} | ${r.withEff} | ${r.impl} | ${r.pureKw} | 0`);
}
console.log('\n=== 合计未实现', totalMissing, '张 (另纯关键词', totalPureKw, '张无需实现) ===');
fs.writeFileSync(ROOT + '/_audit_missing.json', JSON.stringify(missingList, null, 2));
console.log('明细已写 _audit_missing.json');
