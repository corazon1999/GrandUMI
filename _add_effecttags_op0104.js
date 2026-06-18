// 给 OP01-04 的 watcher 监听卡补 EffectTags（CollectListeners 据此收集，否则脚本不被触发）。
// 仅改服务端卡库 卡牌数据/（效果执行的事实来源）。这些标签本应由 strip-effecttext 预计算，
// 但其谓词未识别这些文本模式，故手工补齐。
const fs = require('fs');
const ROOT = 'D:/Self/GrandUMI/卡牌数据/';

// 卡号 → 需补充的 EffectTags
const ADD = {
  'OP01-061': ['OnAnyCharKOd'],
  'OP02-002': ['OnDonAttached'],
  'OP03-040': ['OnDamageToLeader'],
  'OP03-041': ['OnDamageToLeader'],
  'OP03-043': ['OnDamageToLeader'],
  'OP03-076': ['OnAnyCharKOd'],
  'OP04-024': ['OnAllyCharEnter'],   // 已有 OnEnterField，追加监听"对方角色登场"
  'OP04-047': ['OnBattleEnd'],
  'OP04-053': ['OnOppEventPlayed'],
  'OP04-086': ['OnAnyCharKOd'],
  'OP04-096': ['OnEnterField'],      // 舞台登场注册持续授予关键词
};

const bySet = {};
for (const num of Object.keys(ADD)) { const s = num.slice(0, 4); (bySet[s] = bySet[s] || []).push(num); }

for (const set of Object.keys(bySet)) {
  const path = ROOT + set + '.json';
  const arr = JSON.parse(fs.readFileSync(path, 'utf8'));
  let changed = 0;
  for (const c of arr) {
    if (!ADD[c.number]) continue;
    c.effectTags = Array.isArray(c.effectTags) ? c.effectTags : [];
    for (const t of ADD[c.number]) if (!c.effectTags.includes(t)) { c.effectTags.push(t); changed++; }
  }
  fs.writeFileSync(path, JSON.stringify(arr, null, 2));
  console.log(set + ': 补充', changed, '个标签 →', bySet[set].join(','));
}
console.log('完成。');
