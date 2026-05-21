/**
 * 航海王卡牌对战 — 全量卡图下载
 * 数据来源: https://onepieceserve.windoent.com/cardList/cardlist/weblist
 * 运行: node download_cards.mjs
 */

import https from 'https'
import { writeFile, mkdir, stat } from 'fs/promises'
import { existsSync } from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// ── 配置 ─────────────────────────────────────────────
const API_BASE    = 'https://onepieceserve.windoent.com'
const PAGE_SIZE   = 100        // 每次拉取数量
const CONCURRENCY = 5          // 同时下载并发数
const DELAY_MS    = 200        // 每批次下载间隔（ms）
const OUTPUT_DIR  = path.join(__dirname, 'CardImages')
// ─────────────────────────────────────────────────────

function get(url) {
  return new Promise((resolve, reject) => {
    const req = https.get(url, {
      headers: {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
        'Referer': 'https://www.onepiece-cardgame.cn/',
      },
      timeout: 30000,
    }, res => {
      // 处理重定向
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        return resolve(get(res.headers.location))
      }
      const chunks = []
      res.on('data', c => chunks.push(c))
      res.on('end', () => resolve({ status: res.statusCode, body: Buffer.concat(chunks) }))
    })
    req.on('error', reject)
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')) })
  })
}

/** 从图片 URL 文件名里提取卡号，如 "1774257094607OP15-001.png" → "OP15-001" */
function parseCardNo(imgUrl) {
  const filename = imgUrl.split('/').pop().split('?')[0]
  // 去掉时间戳前缀（纯数字）
  const m = filename.match(/^(\d+)([A-Z]{1,4}\d{0,2}-\d{3,}(?:[A-Z_-][A-Z0-9_-]*)?)\.(\w+)$/i)
  if (m) return { cardNo: m[2].toUpperCase(), ext: m[3].toLowerCase() }
  // 没有数字前缀的情况
  const m2 = filename.match(/^([A-Z]{1,4}\d{0,2}-\d{3,}(?:[A-Z_-][A-Z0-9_-]*)?)\.(\w+)$/i)
  if (m2) return { cardNo: m2[1].toUpperCase(), ext: m2[2].toLowerCase() }
  return null
}

/** 从卡号提取弹数 ID（小写），用作目录名 */
function getSetId(cardNo) {
  // P-001 → p，OP01-001 → op01，ST01-001 → st01，EB01-001 → eb01
  const m = cardNo.match(/^([A-Z]{1,4}\d{0,2})-/i)
  return m ? m[1].toLowerCase() : 'unknown'
}

/** 拉取指定页的卡牌列表 */
async function fetchPage(page) {
  const url = `${API_BASE}/cardList/cardlist/weblist?limit=${PAGE_SIZE}&page=${page}`
  const res = await get(url)
  if (res.status !== 200) throw new Error(`HTTP ${res.status} on page ${page}`)
  return JSON.parse(res.body.toString())
}

/** 下载单张图片，断点续传 */
async function downloadOne(imgUrl, savePath) {
  if (existsSync(savePath)) {
    try {
      const s = await stat(savePath)
      if (s.size > 2048) return 'skip'
    } catch {}
  }
  try {
    const res = await get(imgUrl)
    if (res.status !== 200) return `fail:${res.status}`
    await writeFile(savePath, res.body)
    return 'ok'
  } catch (e) {
    return `fail:${e.message}`
  }
}

function sleep(ms) {
  return new Promise(r => setTimeout(r, ms))
}

/** 并发控制：将任务切成批次执行 */
async function runBatches(tasks, concurrency) {
  let ok = 0, skip = 0, fail = 0
  for (let i = 0; i < tasks.length; i += concurrency) {
    const batch = tasks.slice(i, i + concurrency)
    const results = await Promise.all(batch.map(t => t()))
    for (const r of results) {
      if (r === 'ok')   ok++
      else if (r === 'skip') skip++
      else              fail++
    }
    if (i + concurrency < tasks.length) await sleep(DELAY_MS)
  }
  return { ok, skip, fail }
}

// ── 主流程 ───────────────────────────────────────────
console.log('='.repeat(55))
console.log('  航海王卡牌对战 — 全量卡图下载')
console.log(`  输出目录: ${OUTPUT_DIR}`)
console.log('='.repeat(55))

// 1. 先拉第一页，知道总页数
process.stdout.write('\n[1/3] 正在获取卡牌总数... ')
const first = await fetchPage(1)
if (first.code !== 0) { console.error('API 错误:', first.msg); process.exit(1) }
const { totalPage, totalCount } = first.page
console.log(`共 ${totalCount} 张，${totalPage} 页`)

// 2. 拉取所有页的卡牌数据
console.log(`[2/3] 拉取所有页数据（每页 ${PAGE_SIZE} 条）...`)
const allCards = []  // { id, cardImg }

// 第一页已经有了
allCards.push(...first.page.list)

// 并发拉取剩余页（分批，避免太猛）
const FETCH_BATCH = 10
for (let p = 2; p <= totalPage; p += FETCH_BATCH) {
  const end = Math.min(p + FETCH_BATCH - 1, totalPage)
  const pages = Array.from({ length: end - p + 1 }, (_, i) => p + i)
  const results = await Promise.all(pages.map(async pg => {
    try { return (await fetchPage(pg)).page.list }
    catch (e) { console.error(`  页 ${pg} 失败: ${e.message}`); return [] }
  }))
  for (const list of results) allCards.push(...list)
  process.stdout.write(`\r  已获取 ${Math.min(allCards.length, totalCount)}/${totalCount} 条...`)
}
console.log(`\n  获取完成，共 ${allCards.length} 条记录`)

// 3. 建目录 + 构建下载任务
console.log('[3/3] 开始下载...')
await mkdir(OUTPUT_DIR, { recursive: true })

// 统计每个卡号出现次数（用于命名异画）
const cardNoCounts = new Map()

const tasks = []
const skippedParse = []

for (const card of allCards) {
  const parsed = parseCardNo(card.cardImg)
  if (!parsed) {
    skippedParse.push(card.cardImg)
    continue
  }
  const { cardNo, ext } = parsed
  const setId = getSetId(cardNo)

  // 统计出现次数（同一卡号多次 = 异画）
  const count = (cardNoCounts.get(cardNo) || 0) + 1
  cardNoCounts.set(cardNo, count)

  // 文件名：第1张用卡号本身，之后的加 _alt2, _alt3...
  const filename = count === 1 ? `${cardNo}.${ext}` : `${cardNo}_alt${count}.${ext}`
  const setDir   = path.join(OUTPUT_DIR, setId)
  const savePath = path.join(setDir, filename)

  tasks.push(async () => {
    await mkdir(setDir, { recursive: true })
    const result = await downloadOne(card.cardImg, savePath)
    if (result === 'ok')   console.log(`  [OK  ] ${setId}/${filename}`)
    if (result.startsWith('fail')) console.log(`  [FAIL] ${setId}/${filename} — ${result}`)
    return result
  })
}

if (skippedParse.length) {
  console.log(`  ⚠ ${skippedParse.length} 条记录无法解析卡号，已跳过`)
}

console.log(`  共 ${tasks.length} 张图片待下载（并发 ${CONCURRENCY}）\n`)

const { ok, skip, fail } = await runBatches(tasks, CONCURRENCY)

// 统计各弹数量
const setSummary = {}
for (const [cardNo, cnt] of cardNoCounts) {
  const setId = getSetId(cardNo)
  setSummary[setId] = (setSummary[setId] || 0) + cnt
}

console.log('\n' + '='.repeat(55))
console.log('  下载完成')
console.log(`  成功: ${ok}   跳过(已存在): ${skip}   失败: ${fail}`)
console.log(`  保存目录: ${OUTPUT_DIR}`)
console.log('\n  各弹张数:')
Object.entries(setSummary).sort((a, b) => a[0].localeCompare(b[0])).forEach(([set, cnt]) => {
  console.log(`    ${set.padEnd(8)} ${cnt} 张`)
})
console.log('='.repeat(55))
