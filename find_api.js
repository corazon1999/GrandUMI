const fs = require('fs');
const content = fs.readFileSync('D:/Self/GrandUMI/opcg_app.js', 'utf8');

// 找 API 路径
const rawApis = content.match(/["']\/api[^"'\s<>]{2,80}/g) || [];
const apis = Array.from(new Set(rawApis.map(function(s){ return s.slice(1); })));
console.log('=== API PATHS ===');
apis.forEach(function(a){ console.log(a); });

// 找 baseURL
const baseMatch = content.match(/baseURL[^,;\n\]]{0,150}/g) || [];
console.log('\n=== BASE URLS ===');
baseMatch.slice(0,10).forEach(function(b){ console.log(b); });

// 找所有 xxxAPI 函数调用
const cardApis = content.match(/[a-zA-Z]{3,30}API\b/g) || [];
const uniq = Array.from(new Set(cardApis));
console.log('\n=== API FUNCTIONS ===');
uniq.forEach(function(c){ console.log(c); });

// 找包含 card 的字符串路径
const cardPaths = content.match(/["'][^"']{0,20}[Cc]ard[^"']{0,40}["']/g) || [];
const uniqPaths = Array.from(new Set(cardPaths)).filter(function(p){
  return p.includes('/') || p.includes('Api') || p.includes('api');
});
console.log('\n=== CARD PATHS ===');
uniqPaths.slice(0, 30).forEach(function(p){ console.log(p); });
