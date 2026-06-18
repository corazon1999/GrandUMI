// 生成后处理：扫描 Scripted/*.cs，对指定卡号(_complex_report_gap.json)的脚本，
// 解析其 HandlesTrigger 中的 EffectTrigger，把"需经 CollectListeners 收集"的触发名补进 卡牌数据/{SET}.json 的 effectTags。
// 不需标签的触发(经直接 Resolve)：OnLifeRevealTrigger(走 trigger 字段)、OnGameStart(领袖直注册)、ActivatedMain/EventMain/EventCounter(玩家发起)。
const fs = require('fs');
const ROOT = 'D:/Self/GrandUMI';
const SCR = ROOT + '/服务端WebSocket/Effects/Scripted';
const DATA = ROOT + '/卡牌数据';

const TAG_REQUIRED = new Set(['OnEnterField','OnAttackDeclare','OnOppAttackDeclare','OnBlockDeclare','OnKO','PreKO','OnMyTurnEnd','OnOppTurnEnd','OnTurnStart','OnDonAttached','OnDrawCard','OnPlayCard','OnDamageToLeader','OnAnyCharKOd','OnBattleEnd','OnOppEventPlayed','OnAllyCharEnter','OnAllyWillBeKOd','OnAllyWillLeaveField','OnCharRested','OnCharLeaveField','OnLifeLeaveField','OnDonReturnedToDeck','OnOppBlocker','OnEnterTrash','OnTriggerActivated','OnHandDiscarded']);

// 目标卡号集合(本轮 gap complex)
const target = new Set(JSON.parse(fs.readFileSync(ROOT + '/_complex_report_gap.json', 'utf8')).map(r => r.number));

// 扫描脚本 → number -> Set(triggers)
const byNum = {};
for (const f of fs.readdirSync(SCR).filter(f => f.endsWith('.cs'))) {
  const txt = fs.readFileSync(SCR + '/' + f, 'utf8');
  const numM = txt.match(/CardNumber\s*=>\s*"([A-Z0-9-]+)"/);
  if (!numM) continue;
  const num = numM[1];
  if (!target.has(num)) continue;
  // 提取 HandlesTrigger 那一行(或多行)里的 EffectTrigger.X
  const hM = txt.match(/HandlesTrigger\([^)]*\)\s*=>([^;]+);/s);
  const scope = hM ? hM[1] : '';
  const trigs = new Set();
  for (const m of scope.matchAll(/EffectTrigger\.(\w+)/g)) trigs.add(m[1]);
  byNum[num] = trigs;
}

// 按系列写回 effectTags
const bySet = {};
for (const num of Object.keys(byNum)) { const s = num.replace(/-.*/, ''); (bySet[s] = bySet[s] || []).push(num); }
let totalAdded = 0;
const noScript = [...target].filter(n => !byNum[n]);
for (const set of Object.keys(bySet)) {
  const path = DATA + '/' + set + '.json';
  if (!fs.existsSync(path)) { console.log('跳过(无卡库)', set); continue; }
  const arr = JSON.parse(fs.readFileSync(path, 'utf8'));
  let added = 0;
  for (const c of arr) {
    if (!byNum[c.number]) continue;
    c.effectTags = Array.isArray(c.effectTags) ? c.effectTags : [];
    for (const t of byNum[c.number]) {
      if (!TAG_REQUIRED.has(t)) continue;
      if (!c.effectTags.includes(t)) { c.effectTags.push(t); added++; totalAdded++; }
    }
  }
  fs.writeFileSync(path, JSON.stringify(arr, null, 2));
  console.log(set + ': +' + added + ' 标签 (' + bySet[set].length + ' 张有脚本)');
}
console.log('合计补', totalAdded, '个标签。');
console.log('complex 中无脚本(仍 complex/未写)', noScript.length, '张:', noScript.join(',') || '无');
