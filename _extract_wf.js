const fs = require('fs');
const p = process.argv[2];
let raw = fs.readFileSync(p, 'utf8');
const start = raw.indexOf('[{');
if (start < 0) { console.error('找不到JSON起点'); process.exit(1); }
let depth = 0, inStr = false, esc = false, end = -1;
for (let i = start; i < raw.length; i++) {
  const ch = raw[i];
  if (esc) { esc = false; continue; }
  if (ch === '\\') { esc = true; continue; }
  if (ch === '"') { inStr = !inStr; continue; }
  if (inStr) continue;
  if (ch === '[') depth++;
  else if (ch === ']') { depth--; if (depth === 0) { end = i; break; } }
}
if (end < 0) { console.error('括号未配平,文件可能截断'); process.exit(1); }
const data = JSON.parse(raw.slice(start, end + 1));
fs.writeFileSync('_wf_result_op0104.json', JSON.stringify(data, null, 2));
let total = 0; for (const b of data) total += (b.results || []).length;
console.log('解析成功,批次', data.length, '条目', total);
