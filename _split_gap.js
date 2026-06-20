// 从 _audit_missing.json 的缺口卡号，拉含原文完整字段(含 trigger)，按 ≤10/批 切批到 _wf_gap/，输出 manifest。
const fs = require('fs');
const path = require('path');
const ROOT = 'D:/Self/GrandUMI';
const OUT = path.join(ROOT, '_归档_卡效工作流/批次输入/_wf_gap');
fs.mkdirSync(OUT, { recursive: true });

const miss = JSON.parse(fs.readFileSync(ROOT + '/_audit_missing.json', 'utf8'));
const wantBySet = {};
for (const c of miss) { const s = c.set; (wantBySet[s] = wantBySet[s] || new Set()).add(c.number); }

const KEEP = ['number', 'name', 'type', 'color', 'cost', 'power', 'counter', 'keyWords', 'effectText', 'trigger'];
const manifest = [];
const setsSorted = Object.keys(wantBySet).sort();
for (const set of setsSorted) {
  const raw = JSON.parse(fs.readFileSync(ROOT + '/卡牌数据_含原文/' + set + '.json', 'utf8'));
  const arr = Array.isArray(raw) ? raw : Object.values(raw);
  const want = wantBySet[set];
  const cards = arr.filter(c => want.has(c.number)).map(c => {
    const o = {}; for (const k of KEEP) if (k in c) o[k] = c[k]; return o;
  });
  for (let i = 0; i < cards.length; i += 10) {
    const slice = cards.slice(i, i + 10);
    const file = path.join(OUT, `${set}_${i}.json`);
    fs.writeFileSync(file, JSON.stringify(slice, null, 2));
    manifest.push({ set, idx: i, file: file.replace(/\\/g, '/'), count: slice.length });
  }
}
fs.writeFileSync(path.join(OUT, '_manifest.json'), JSON.stringify(manifest, null, 2));
let tot = 0; for (const m of manifest) tot += m.count;
console.log('缺口', tot, '张, 切', manifest.length, '批');
console.log('MANIFEST_START'); console.log(JSON.stringify(manifest)); console.log('MANIFEST_END');
