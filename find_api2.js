const fs = require('fs');
const content = fs.readFileSync('D:/Self/GrandUMI/opcg_app.js', 'utf8');

// 找 baseURL 周围的上下文
var idx = 0;
while (true) {
  idx = content.indexOf('baseURL', idx);
  if (idx === -1) break;
  console.log('--- pos', idx, '---');
  console.log(content.slice(Math.max(0, idx-100), idx+200));
  console.log();
  idx += 7;
}

// 找 weblist 周围的上下文
console.log('\n=== weblist CONTEXT ===');
idx = content.indexOf('weblist');
if (idx >= 0) {
  console.log(content.slice(Math.max(0, idx-300), idx+400));
}
