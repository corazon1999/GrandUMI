const fs = require('fs');
const path = require('path');
const ROOT = 'D:/Self/GrandUMI';
const OUT = ROOT + '/_归档_卡效工作流/批次输入/_wf_cs_gap';
fs.mkdirSync(OUT, { recursive: true });
const report = JSON.parse(fs.readFileSync(ROOT + '/_complex_report_gap.json', 'utf8'));
const reason = {};
for (const r of report) reason[r.number] = r.reason || '';
const orig = {};
for (const f of fs.readdirSync(ROOT + '/卡牌数据_含原文').filter(f => f.endsWith('.json'))) {
  try { for (const c of JSON.parse(fs.readFileSync(ROOT + '/卡牌数据_含原文/' + f, 'utf8'))) orig[c.number] = c; } catch (e) {}
}
const KEEP = ['number', 'name', 'type', 'color', 'cost', 'power', 'counter', 'keyWords', 'effectText', 'trigger'];
const cards = report.map(r => {
  const c = orig[r.number] || {}; const o = {};
  for (const k of KEEP) if (k in c) o[k] = c[k];
  o.wfReason = reason[r.number]; return o;
});
const bySet = {};
for (const c of cards) { const s = c.number.replace(/-.*/, ''); (bySet[s] = bySet[s] || []).push(c); }
const manifest = [];
for (const set of Object.keys(bySet).sort()) {
  const arr = bySet[set];
  for (let i = 0; i < arr.length; i += 10) {
    const slice = arr.slice(i, i + 10);
    const file = path.join(OUT, set + '_cs_' + i + '.json');
    fs.writeFileSync(file, JSON.stringify(slice, null, 2));
    manifest.push({ label: set + '-cs-' + i, file: file.replace(/\\/g, '/'), count: slice.length });
  }
}
fs.writeFileSync(path.join(OUT, '_manifest.json'), JSON.stringify(manifest, null, 2));
console.log('complex', cards.length, '张, 切', manifest.length, '批');
console.log(JSON.stringify(manifest));
