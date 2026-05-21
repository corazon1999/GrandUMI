const fs = require('fs');
const content = fs.readFileSync('D:/Self/GrandUMI/opcg_app.js', 'utf8');

// 找所有 url: 的配置
var urlMatches = content.match(/url:"[^"]{5,80}"/g) || [];
var uniq = Array.from(new Set(urlMatches));
console.log('=== ALL API URLS ===');
uniq.forEach(function(u){ console.log(u); });

// 找 axios 实例的 baseURL
var axiosIdx = content.indexOf('axios');
while (axiosIdx !== -1) {
  var ctx = content.slice(axiosIdx, axiosIdx + 300);
  if (ctx.includes('baseURL') || ctx.includes('http')) {
    console.log('\n--- AXIOS CTX ---');
    console.log(ctx);
  }
  axiosIdx = content.indexOf('axios', axiosIdx + 5);
  if (axiosIdx > 100000) break;
}

// 找 http 开头的 URL
var httpUrls = content.match(/https?:\/\/[^"'\s<>]{10,80}/g) || [];
var uniqHttp = Array.from(new Set(httpUrls));
console.log('\n=== HTTP URLS ===');
uniqHttp.forEach(function(u){ console.log(u); });
