/**
 * 用 Playwright 加载日文官网，找那3张卡的图片 URL
 * 目标卡: ST14-002, ST03-013(_04变体), EB02-006(_03变体)
 */
import { chromium } from 'playwright'

const chromiumExe = 'C:\\Users\\admin\\AppData\\Local\\ms-playwright\\chromium-1223\\chrome-win64\\chrome.exe'

const browser = await chromium.launch({
  headless: true,
  executablePath: chromiumExe,
})
const ctx = await browser.newContext({
  userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
})
const page = await ctx.newPage()

const foundUrls = new Set()

// 拦截图片请求
page.on('request', req => {
  const url = req.url()
  if (/\.(png|jpg|webp|jpeg)/i.test(url) && /(ST14|ST03|EB02)/i.test(url)) {
    foundUrls.add(url)
    console.log('[请求] ', url)
  }
})
page.on('response', async res => {
  const url = res.url()
  if (/\.(png|jpg|webp|jpeg)/i.test(url) && /(ST14|ST03|EB02)/i.test(url)) {
    foundUrls.add(url)
    console.log('[响应]', res.status(), url)
  }
})

console.log('[1] 打开日文官网卡牌列表...')
try {
  await page.goto('https://www.onepiece-cardgame.com/cardlist/', {
    waitUntil: 'domcontentloaded',
    timeout: 45000,
  })
} catch (e) {
  console.log('  加载超时或错误:', e.message)
}
await page.waitForTimeout(3000)

console.log('\n[2] 当前 URL:', page.url())

// 找页面内所有图片 src 含 ST14/ST03/EB02 的
const imgs = await page.$$eval('img', els =>
  els.map(e => e.src).filter(s => /(ST14|ST03|EB02)/i.test(s))
)
console.log('[页面内图片]', imgs)

// 找 API 请求模式（捕获 XHR/fetch）
const apiUrls = []
page.on('response', async res => {
  const url = res.url()
  const ct = res.headers()['content-type'] || ''
  if (ct.includes('json') && url.includes('cardlist')) {
    try {
      const json = await res.json()
      apiUrls.push({ url, data: JSON.stringify(json).slice(0, 300) })
    } catch {}
  }
})

// 尝试搜索卡牌
console.log('\n[3] 查找卡牌搜索接口...')
// 找页面 HTML 结构
const html = await page.content()
console.log('页面长度:', html.length)
// 找所有 input
const inputs = await page.$$eval('input,select', els => els.map(e => ({ tag: e.tagName, name: e.name, id: e.id, type: e.type })))
console.log('表单元素:', inputs.slice(0, 10))

// 找含 card 字样的 script
const scriptContent = html.match(/(cardlist|card_number|cardNo|card_id)[^<]{0,200}/gi)?.slice(0,5)
console.log('卡牌相关JS片段:', scriptContent)

await browser.close()

console.log('\n=== 捕获到的图片URL ===')
;[...foundUrls].forEach(u => console.log(u))
console.log('API URLs:', apiUrls)
