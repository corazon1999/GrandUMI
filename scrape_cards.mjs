/**
 * 航海王卡牌对战 — 卡图批量下载
 * 依赖: npm install playwright
 *       npx playwright install chromium
 * 运行: node scrape_cards.mjs
 */

import { chromium } from 'playwright'
import { writeFile, mkdir, stat } from 'fs/promises'
import { existsSync } from 'fs'
import { createWriteStream } from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname  = path.dirname(fileURLToPath(import.meta.url))
const BASE_URL   = 'https://www.onepiece-cardgame.cn'
const OUTPUT_DIR = path.join(__dirname, 'CardImages')
const HEADLESS   = true   // 调试时改 false 可看到浏览器
const DELAY_MS   = 400    // 每次请求间隔，避免被限速

// 卡号正则：OP01-001 / ST01-001 / EB01-001 / P-001
const CARD_NO_RE = /\b([A-Z]{1,4}\d{0,2}-\d{3,})\b/i

// ─────────────────────────────────────────────────────────
// 工具
// ─────────────────────────────────────────────────────────

function getSetId(cardNo) {
  const m = cardNo.match(/^([A-Z]{1,4}\d{0,2})-/i)
  return m ? m[1].toUpperCase() : 'UNKNOWN'
}

function parseCardNoFromUrl(url) {
  const m = CARD_NO_RE.exec(url)
  return m ? m[1].toUpperCase() : null
}

function isCardImageUrl(url) {
  const low = url.toLowerCase()
  if (/\.(png|jpg|jpeg|webp)/.test(low)) {
    // 过滤掉明显的 UI 资源
    if (/(icon|logo|bg_|banner|sprite|button|ui\/|\.svg|\.gif|font)/.test(low)) return false
    // 路径里包含 card / image / img 关键词
    if (/(card|image|img|picture|cards|cardimg)/.test(low)) return true
    // URL 里直接有卡号格式
    if (CARD_NO_RE.test(url)) return true
  }
  return false
}

async function ensureDir(dir) {
  if (!existsSync(dir)) await mkdir(dir, { recursive: true })
}

function sleep(ms) {
  return new Promise(r => setTimeout(r, ms))
}

// ─────────────────────────────────────────────────────────
// 阶段 1：Playwright 爬取，收集所有卡图 URL
// ─────────────────────────────────────────────────────────

async function collectImageUrls() {
  // card_no → Set<url>
  const imageMap   = new Map()
  const unknownSet = new Set()
  const apiData    = []

  function addImage(cardNo, url) {
    if (!imageMap.has(cardNo)) imageMap.set(cardNo, new Set())
    imageMap.get(cardNo).add(url)
  }

  function parseApiJson(obj, depth = 0) {
    if (depth > 6 || !obj) return
    if (Array.isArray(obj)) {
      obj.forEach(item => parseApiJson(item, depth + 1))
    } else if (typeof obj === 'object') {
      // 常见图片字段名
      for (const key of ['img','image','imageUrl','img_url','card_image','picture','src','thumbnail','imgSrc']) {
        if (typeof obj[key] === 'string' && /\.(png|jpg|jpeg|webp)/i.test(obj[key])) {
          let url = obj[key].startsWith('http') ? obj[key] : BASE_URL + obj[key]
          const no = parseCardNoFromUrl(url)
          no ? addImage(no, url) : unknownSet.add(url)
        }
      }
      // 同时递归卡号字段作为辅助键
      const no = obj.cardNo || obj.card_no || obj.number || obj.cardNumber
      Object.values(obj).forEach(v => parseApiJson(v, depth + 1))
      _ = no // suppress lint
    }
  }

  // 已安装的是完整 Chrome for Testing，直接指定路径跳过 headless-shell 检测
  const chromiumExe = 'C:\\Users\\admin\\AppData\\Local\\ms-playwright\\chromium-1223\\chrome-win64\\chrome.exe'
  const browser = await chromium.launch({
    headless: HEADLESS,
    executablePath: chromiumExe,
  })
  const ctx     = await browser.newContext({
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
  })
  const page = await ctx.newPage()

  // ── 拦截所有请求 ──
  page.on('request', req => {
    const url = req.url()
    if (isCardImageUrl(url)) {
      const no = parseCardNoFromUrl(url)
      no ? addImage(no, url) : unknownSet.add(url)
    }
  })

  // ── 拦截 API 响应 ──
  page.on('response', async res => {
    const url = res.url()
    const ct  = res.headers()['content-type'] || ''
    if (ct.includes('json') && /\/(api|card|list|data)\//i.test(url)) {
      try {
        const json = await res.json()
        apiData.push({ url, data: json })
        parseApiJson(json)
        console.log(`  [API] ${url}`)
      } catch {}
    }
  })

  // ── 打开首页 ──
  console.log('\n[1/5] 打开首页...')
  await page.goto(BASE_URL, { waitUntil: 'domcontentloaded', timeout: 30000 })
  await sleep(5000)  // 等待 JS 框架初始化完成

  // ── 找卡牌列表页入口 ──
  console.log('[2/5] 定位卡牌列表页...')
  const listPageFound = await tryNavigateToCardList(page)
  if (!listPageFound) {
    console.log('  ⚠ 未找到卡牌列表导航，尝试直接访问 /cardlist')
  }
  await sleep(2000)

  // ── 收集所有系列选项 ──
  console.log('[3/5] 收集卡集系列...')
  const series = await collectSeriesOptions(page)
  console.log(`  找到 ${series.length} 个系列`)

  // ── 逐系列加载并滚动 ──
  console.log('[4/5] 遍历各系列，触发图片加载...')
  if (series.length > 0) {
    for (const s of series) {
      console.log(`  → 系列: ${s.label}`)
      await s.select()
      await sleep(1500)
      await scrollToBottom(page)
      await sleep(800)
    }
  } else {
    console.log('  未找到系列选项，滚动当前页面...')
    await scrollToBottom(page)
  }

  // ── 最终等待懒加载 ──
  console.log('[5/5] 等待剩余懒加载...')
  await sleep(3000)
  await scrollToBottom(page)
  await sleep(2000)

  await browser.close()

  // 保存 API 响应供调试
  if (apiData.length > 0) {
    await ensureDir(OUTPUT_DIR)
    await writeFile(
      path.join(OUTPUT_DIR, '_api_debug.json'),
      JSON.stringify(apiData, null, 2),
      'utf-8'
    )
    console.log(`\n  API 响应已存至 CardImages/_api_debug.json（调试用）`)
  }

  return { imageMap, unknownSet }
}

// ─────────────────────────────────────────────────────────
// 辅助：导航到卡牌列表页
// ─────────────────────────────────────────────────────────

async function tryNavigateToCardList(page) {
  // 先试点击导航链接
  for (const sel of ['a[href*="cardlist"]','a[href*="card_list"]','a[href*="cards"]']) {
    const el = page.locator(sel).first()
    if (await el.count() > 0) {
      await el.click()
      await page.waitForLoadState('domcontentloaded').catch(() => {}); await sleep(1500)
      return true
    }
  }
  // 再试直接访问路径
  for (const p of ['/cardlist', '/cardlist/', '/card_list', '/cards']) {
    try {
      const res = await page.goto(BASE_URL + p, { waitUntil: 'domcontentloaded', timeout: 12000 })
      if (res && res.status() < 400) return true
    } catch {}
  }
  return false
}

// ─────────────────────────────────────────────────────────
// 辅助：收集系列/卡集选项
// ─────────────────────────────────────────────────────────

async function collectSeriesOptions(page) {
  const results = []

  // 方案 A：<select> 下拉框
  const selectEl = page.locator('select').first()
  if (await selectEl.count() > 0) {
    const opts = await selectEl.locator('option').all()
    for (const opt of opts) {
      const val  = await opt.getAttribute('value')
      const text = (await opt.innerText()).trim()
      if (val && val !== '' && val !== '0') {
        results.push({
          label: text,
          select: async () => {
            await selectEl.selectOption({ value: val })
            await page.waitForLoadState('domcontentloaded').catch(() => {}); await sleep(1500)
          },
        })
      }
    }
    if (results.length > 0) return results
  }

  // 方案 B：列表/标签形式的系列选项
  for (const sel of [
    '.series-list li', '.expansion-list li',
    '[class*="series"] li', '[class*="pack"] li',
    '.filter-series a', '.tab-list li',
  ]) {
    const items = page.locator(sel)
    const cnt   = await items.count()
    if (cnt > 1) {
      for (let i = 0; i < cnt; i++) {
        const item = items.nth(i)
        const text = (await item.innerText()).trim()
        results.push({
          label: text,
          select: async () => {
            await item.click()
            await page.waitForLoadState('domcontentloaded').catch(() => {}); await sleep(1500)
          },
        })
      }
      return results
    }
  }

  return results
}

// ─────────────────────────────────────────────────────────
// 辅助：滚动到底部（触发懒加载）
// ─────────────────────────────────────────────────────────

async function scrollToBottom(page) {
  let prev = 0
  for (let i = 0; i < 25; i++) {
    const h = await page.evaluate(() => document.body.scrollHeight)
    if (h === prev) break
    prev = h
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
    await sleep(600)
  }
}

// ─────────────────────────────────────────────────────────
// 阶段 2：下载图片
// ─────────────────────────────────────────────────────────

async function downloadAll(imageMap, unknownSet) {
  const total = [...imageMap.values()].reduce((s, v) => s + v.size, 0)
  console.log(`\n收集到 ${imageMap.size} 张卡，共 ${total} 个 URL，未知卡号 ${unknownSet.size} 个`)

  if (total === 0 && unknownSet.size === 0) {
    console.log('\n⚠  未捕获到任何图片 URL。')
    console.log('   建议：')
    console.log('   1. 将 HEADLESS = false 重新运行，手动浏览几张卡')
    console.log('   2. 查看 CardImages/_api_debug.json 了解数据结构')
    console.log('   3. 根据实际 API 路径调整 isCardImageUrl() 函数')
    return
  }

  await ensureDir(OUTPUT_DIR)

  let ok = 0, skip = 0, fail = 0

  // 已知卡号的图片
  for (const [cardNo, urls] of imageMap.entries()) {
    const setId  = getSetId(cardNo)
    const setDir = path.join(OUTPUT_DIR, setId)
    await ensureDir(setDir)

    let ver = 1
    for (const url of urls) {
      const ext      = (url.match(/\.(png|jpg|jpeg|webp)/i) || ['','png'])[1].toLowerCase()
      const filename = `${cardNo}_${ver}.${ext}`
      const savePath = path.join(setDir, filename)
      ver++

      const res = await downloadOne(url, savePath)
      if (res === 'ok')   { ok++;   console.log(`  [OK  ] ${setId}/${filename}`) }
      if (res === 'skip') { skip++; }
      if (res === 'fail') { fail++; }
      await sleep(DELAY_MS)
    }
  }

  // 未知卡号图片 → UNKNOWN/
  if (unknownSet.size > 0) {
    const unkDir = path.join(OUTPUT_DIR, 'UNKNOWN')
    await ensureDir(unkDir)
    let idx = 1
    for (const url of unknownSet) {
      const ext      = (url.match(/\.(png|jpg|jpeg|webp)/i) || ['','png'])[1].toLowerCase()
      const filename = `unknown_${String(idx).padStart(4,'0')}.${ext}`
      const savePath = path.join(unkDir, filename)
      idx++
      const res = await downloadOne(url, savePath)
      if (res === 'ok')   { ok++;   console.log(`  [OK  ] UNKNOWN/${filename}`) }
      if (res === 'skip') { skip++; }
      if (res === 'fail') { fail++; }
      await sleep(DELAY_MS)
    }
  }

  console.log('\n' + '='.repeat(50))
  console.log(`  ✅ 下载完成`)
  console.log(`     成功: ${ok}  跳过(已存在): ${skip}  失败: ${fail}`)
  console.log(`     保存目录: ${OUTPUT_DIR}`)
  console.log('='.repeat(50))
}

async function downloadOne(url, savePath) {
  // 断点续传：文件已存在且大于 1KB 则跳过
  if (existsSync(savePath)) {
    try {
      const s = await stat(savePath)
      if (s.size > 1024) return 'skip'
    } catch {}
  }

  try {
    const res = await fetch(url, {
      headers: {
        'Referer': BASE_URL,
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
      },
    })
    if (!res.ok) {
      console.log(`  [FAIL] HTTP ${res.status} → ${url}`)
      return 'fail'
    }
    const buf = Buffer.from(await res.arrayBuffer())
    await writeFile(savePath, buf)
    return 'ok'
  } catch (e) {
    console.log(`  [ERR ] ${path.basename(savePath)}: ${e.message}`)
    return 'fail'
  }
}

// ─────────────────────────────────────────────────────────
// 入口
// ─────────────────────────────────────────────────────────

console.log('='.repeat(50))
console.log('  航海王卡牌对战 — 卡图批量下载')
console.log(`  输出目录: ${OUTPUT_DIR}`)
console.log('='.repeat(50))

const { imageMap, unknownSet } = await collectImageUrls()
await downloadAll(imageMap, unknownSet)
