/**
 * 可重复迁移：将卡牌 JSON 中的官方效果原文 effectText 降维为结构化字段，
 * 并以“卡牌数据_含原文”为基准重建所有旧卡的基础能力。
 *
 *  - 新增 effectTags[]：该卡响应的触发时机枚举名（精确复刻服务端
 *    EffectRuntime.HasEffectForTrigger 的 switch，行为按构造一致）。
 *    注意 OnLifeRevealTrigger 由运行时按 trigger 字段单独判定，不在此列。
 *  - 重建 abilities[]：只保留卡面无条件基础能力与规则限制，条件获得的能力交给卡效实现；
 *    （阻挡者/速攻/双重攻击/可攻击活跃/不可阻挡/流放/此角色无法攻击）。
 *  - 删除 effectText 键。
 *
 * 目标目录：卡牌数据/  与  opcgpro-web/public/data/
 * 默认 dry-run（只统计、打样例，不写盘）；加 --write 才真正落盘。
 *
 * 运行：
 *   node tools/strip-effecttext.mjs            # 预演
 *   node tools/strip-effecttext.mjs --write    # 落盘
 *   node tools/strip-effecttext.mjs --write ST30 ST31  # 仅处理指定卡集
 */

import { readFile, writeFile, readdir } from 'fs/promises'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.resolve(__dirname, '..')

const TARGET_DIRS = [
  path.join(ROOT, '卡牌数据'),
  path.join(ROOT, 'opcgpro-web', 'public', 'data'),
]
const ORIGINAL_DIR = path.join(ROOT, '卡牌数据_含原文')

const WRITE = process.argv.includes('--write')
const SET_FILTER = new Set(
  process.argv.slice(2)
    .filter(arg => !arg.startsWith('--'))
    .map(set => `${set.toUpperCase()}.json`),
)

// ── 触发时机判定（逐行复刻 EffectRuntime.HasEffectForTrigger，去掉 OnLifeRevealTrigger）──
const TRIGGER_PREDICATES = {
  OnEnterField:        t => t.includes('【登场时】'),
  OnAttackDeclare:     t => t.includes('【攻击时】'),
  OnOppAttackDeclare:  t => t.includes('【对方的攻击时】'),
  OnBlockDeclare:      t => t.includes('【阻挡时】'),
  OnKO:                t => t.includes('【K.O.时】') || t.includes('【KO时】'),
  PreKO:               t => t.includes('不会被KO') || t.includes('不会被K.O.') || t.includes('将要被KO的场合') || t.includes('将要被K.O.的场合') || t.includes('将要被KO时') || t.includes('被KO的场合'),
  OnMyTurnEnd:         t => t.includes('【我方的回合结束时】'),
  OnOppTurnEnd:        t => t.includes('【对方的回合结束时】'),
  ActivatedMain:       t => t.includes('【启动主要】'),
  EventMain:           t => t.includes('【主要】'),
  EventCounter:        t => t.includes('【反击】'),
  OnDrawCard:          t => t.includes('抽取卡牌时') || t.includes('抽卡阶段以外'),
  OnDonReturnedToDeck: t => t.includes('放回咚!!卡组时') || t.includes('放回咚卡组时'),
  OnCharRested:        t => t.includes('转为休息状态时'),
  OnCharLeaveField:    t => t.includes('离开场上时') || t.includes('离开场上的场合'),
  OnLifeLeaveField:    t => t.includes('生命区变为0') || t.includes('生命卡牌离开场上时') || t.includes('生命卡牌加入手牌时') || t.includes('生命的卡牌加入手牌时'),
  OnAllyCharEnter:     t => t.includes('角色登场时') && !t.includes('【登场时】'),
  OnOppEventPlayed:    t => t.includes('对方发动事件时') || t.includes('对方发动事件或'),
  OnOppBlocker:        t => t.includes('对方发动【阻挡者】'),
  OnAllyWillBeKOd:     t => t.includes('将要被KO的场合') || t.includes('将要被K.O.的场合') || t.includes('将要离开场上的场合') || t.includes('代替被KO') || t.includes('使该角色不离场'),
}

// ── 能力关键字（服务端 ActionValidator.HasKeyword 等按 【kw】 扫描的全集）──
const ABILITY_KEYWORDS = ['阻挡者', '速攻', '速攻：角色', '双重攻击', '可攻击活跃', '不可阻挡', '流放']

// 无法仅靠通用中文句式推导、或需给运行时读取的精确规则能力。
const ABILITY_OVERRIDES = new Map([
  ['OP12-036', ['无法通过效果登场']],
  ['OP04-001', ['此角色无法攻击']],
  ['OP04-039', ['此角色无法攻击']],
])

const ALSO_NAME_OVERRIDES = new Map([
  ['EB02-016', ['托尼托尼·乔巴']],
  ['OP02-042', ['光月御殿']],
  ['OP03-122', ['撒谎布']],
])

// 手写脚本/监听器所需的触发连线。这里只补充，不删除已有人工标签。
const EFFECT_TAG_OVERRIDES = new Map([
  ['OP18-031', ['OnAllyWillLeaveField']],
  ['EB02-030', ['EventCounter']],
  ['OP12-021', ['OnEnterField']],
  ['OP12-036', ['OnEnterField']],
  ['OP12-072', ['OnDonReturnedToDeck']],
  ['OP12-081', ['OnAttackDeclare', 'OnAllyCharEnter']],
  ['ST36-001', ['OnKO']],
  ['ST36-002', ['OnEnterField']],
  ['ST36-004', ['OnEnterField']],
  ['ST36-005', ['OnOppAttackDeclare', 'ActivatedMain']],
  ['EB04-029', ['EventMain', 'EventCounter']],
  ['OP04-093', ['EventMain']],
  ['OP05-096', ['EventMain']],
  ['EB03-008', ['OnEnterField', 'OnAttackDeclare', 'ActivatedMain']],
  ['EB04-016', ['ActivatedMain', 'OnAttackDeclare']],
  ['OP11-028', ['OnEnterField']],
  ['OP11-031', ['OnEnterField', 'ActivatedMain']],
  ['OP11-084', ['OnEnterField', 'OnAttackDeclare']],
  ['OP11-119', ['OnEnterField', 'OnAttackDeclare']],
  ['OP12-117', ['EventMain', 'EventCounter']],
  ['OP15-003', ['PreKO', 'ActivatedMain']],
  ['OP15-012', ['OnAttackDeclare', 'OnKO']],
  ['OP15-037', ['EventMain']],
  ['OP15-038', ['EventMain', 'EventCounter']],
  ['OP15-041', ['OnKO', 'ActivatedMain']],
  ['OP15-056', ['EventMain']],
  ['OP15-057', ['OnEnterField', 'OnOppAttackDeclare']],
  ['OP15-084', ['OnEnterField', 'OnKO']],
  ['OP15-115', ['EventMain']],
  ['OP16-057', ['EventCounter']],
  ['OP16-068', ['OnEnterField', 'OnAttackDeclare']],
  ['ST05-010', ['OnEnterField', 'ActivatedMain']],
  ['EB04-001', ['OnGameStart']],
  ['OP01-024', ['OnEnterField']],
  ['OP04-082', ['PreKO']],
  ['OP07-071', ['OnEnterField']],
  ['OP08-084', ['OnEnterField']],
  ['OP13-080', ['OnEnterField']],
  ['OP13-109', ['OnAllyWillLeaveField']],
  ['OP14-045', ['OnHandDiscarded']],
  ['OP14-049', ['OnHandDiscarded']],
  ['OP15-060', ['OnEnterField']],

  ['OP03-001', ['OnAttackDeclare', 'OnOppAttackDeclare']],
  ['OP04-021', ['OnOppAttackDeclare']],
  ['OP04-025', ['OnOppAttackDeclare']],
  ['OP04-030', ['OnOppAttackDeclare']],
  ['OP04-059', ['OnOppAttackDeclare']],
  ['OP04-060', ['OnOppAttackDeclare']],
  ['OP04-063', ['OnOppAttackDeclare']],
  ['OP04-069', ['OnOppAttackDeclare']],
  ['OP04-070', ['OnOppAttackDeclare']],
  ['OP04-071', ['OnOppAttackDeclare']],
  ['OP04-072', ['OnOppAttackDeclare']],
  ['OP07-098', ['PreKO']],
  ['OP10-037', ['PreKO']],
  ['OP10-118', ['PreKO']],
  ['OP12-024', ['PreKO']],
  ['OP13-084', ['PreKO']],
  ['ST02-001', ['ActivatedMain']],
  ['P-133', ['OnEnterField']],
  ['ST19-004', ['OnEnterField', 'ActivatedMain']],
  ['OP18-060', ['OnAllyCharEnter']],
  ['EB05-010', ['OnAnyCharKOd']],
])

function computeEffectTags(text) {
  const tags = []
  for (const [name, pred] of Object.entries(TRIGGER_PREDICATES)) {
    if (pred(text)) tags.push(name)
  }
  return tags
}

function isDeclaredBaseKeyword(text, keyword) {
  const escaped = keyword.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const anyKeyword = ABILITY_KEYWORDS.map(value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')
  const prefix = `(?:【(?:${anyKeyword})】(?:\\s*（[^）]*）)?\\s*)*`
  return new RegExp(`(^|[。\\r\\n])\\s*${prefix}【${escaped}】(?=\\s*(?:（|【|$))`).test(text)
}

function computeAbilities(text, number) {
  const abilities = ABILITY_KEYWORDS.filter(keyword => isDeclaredBaseKeyword(text, keyword))
  if (/(^|[。\r\n])\s*此角色无法攻击。/.test(text)) abilities.push('此角色无法攻击')
  for (const ability of ABILITY_OVERRIDES.get(number) ?? []) abilities.push(ability)
  return [...new Set(abilities)]
}

/** 重建卡对象：保留键顺序，在 effectText 原位换成 effectTags + abilities */
function transformCard(card, original) {
  const hasCurrentText = Object.hasOwn(card, 'effectText')
  const text = typeof original?.effectText === 'string'
    ? original.effectText
    : typeof card.effectText === 'string' ? card.effectText : ''
  const existingTags = Array.isArray(card.effectTags) ? card.effectTags : []
  const inferredTags = hasCurrentText ? computeEffectTags(text) : existingTags
  const effectTags = [...new Set([...inferredTags, ...(EFFECT_TAG_OVERRIDES.get(card.number) ?? [])])]
  const abilities = text ? computeAbilities(text, card.number) : (card.abilities ?? [])
  const out = {}
  for (const [k, v] of Object.entries(card)) {
    if (k === 'effectText') {
      out.effectTags = effectTags
      out.abilities = abilities
    } else if (k === 'effectTags') {
      out.effectTags = effectTags
    } else if (k === 'abilities') {
      out.abilities = abilities
    } else {
      out[k] = v
    }
  }
  // 尚无结构化键的新卡也在原 effectText 位置补齐。
  if (!('effectTags' in out)) { out.effectTags = effectTags; out.abilities = abilities }
  if (ALSO_NAME_OVERRIDES.has(card.number))
    out.alsoNames = ALSO_NAME_OVERRIDES.get(card.number)
  return out
}

async function loadOriginals() {
  const originals = new Map()
  for (const file of (await readdir(ORIGINAL_DIR)).filter(name => name.endsWith('.json'))) {
    const parsed = JSON.parse(await readFile(path.join(ORIGINAL_DIR, file), 'utf8'))
    if (!Array.isArray(parsed)) continue
    for (const card of parsed) if (card?.number) originals.set(card.number, card)
  }
  return originals
}

async function processDir(dir, originals) {
  let files
  try {
    files = (await readdir(dir)).filter(
      f => f.endsWith('.json')
        && !f.startsWith('_')
        && !['allCards.json', 'imageManifest.json'].includes(f)
        && (SET_FILTER.size === 0 || SET_FILTER.has(f)),
    )
  } catch {
    console.log(`  ⚠ 跳过（目录不存在）: ${dir}`)
    return { files: 0, cards: 0, withText: 0 }
  }
  let cards = 0, withText = 0
  const samples = []
  for (const f of files) {
    const fp = path.join(dir, f)
    const raw = await readFile(fp, 'utf8')
    const arr = JSON.parse(raw)
    if (!Array.isArray(arr)) { console.log(`  ⚠ 非数组，跳过: ${f}`); continue }
    const next = arr.map(c => {
      cards++
      if (typeof c.effectText === 'string' && c.effectText.length > 0) withText++
      const t = transformCard(c, originals.get(c.number))
      if (samples.length < 3 && typeof c.effectText === 'string' && c.effectText.length > 0
          && (t.effectTags.length || t.abilities.length)) {
        samples.push({ number: c.number, effectText: c.effectText, effectTags: t.effectTags, abilities: t.abilities })
      }
      return t
    })
    if (WRITE) {
      const ends = raw.endsWith('\n') ? '\n' : ''
      await writeFile(fp, JSON.stringify(next, null, 2) + ends, 'utf8')
    }
  }
  console.log(`  ${path.relative(ROOT, dir)}: ${files.length} 文件, ${cards} 卡, ${withText} 含原文`)
  if (samples.length) {
    console.log('    样例:')
    for (const s of samples) {
      console.log(`      ${s.number}`)
      console.log(`        原文 : ${s.effectText}`)
      console.log(`        tags : [${s.effectTags.join(', ')}]`)
      console.log(`        abils: [${s.abilities.join(', ')}]`)
    }
  }
  return { files: files.length, cards, withText }
}

/**
 * 规则运行时读取“卡牌数据”，客户端读取 public/data；两份副本的规则元数据必须完全一致。
 * 以服务端副本为权威源同步 effectTags / abilities / alsoNames，避免分别迁移后发生漂移。
 */
async function syncRuleMetadata() {
  const primaryDir = TARGET_DIRS[0]
  const publicDir = TARGET_DIRS[1]
  const files = (await readdir(primaryDir)).filter(
    f => f.endsWith('.json')
      && !f.startsWith('_')
      && !['allCards.json', 'imageManifest.json'].includes(f)
      && (SET_FILTER.size === 0 || SET_FILTER.has(f)),
  )
  let changedCards = 0
  for (const file of files) {
    const primaryPath = path.join(primaryDir, file)
    const publicPath = path.join(publicDir, file)
    let publicRaw
    try { publicRaw = await readFile(publicPath, 'utf8') }
    catch { continue }
    const primary = JSON.parse(await readFile(primaryPath, 'utf8'))
    const publicCards = JSON.parse(publicRaw)
    if (!Array.isArray(primary) || !Array.isArray(publicCards)) continue
    const canonical = new Map(primary.filter(card => card?.number).map(card => [card.number, card]))
    for (const card of publicCards) {
      const source = canonical.get(card?.number)
      if (!source) continue
      const before = JSON.stringify([card.effectTags ?? [], card.abilities ?? [], card.alsoNames ?? []])
      card.effectTags = [...(source.effectTags ?? [])]
      card.abilities = [...(source.abilities ?? [])]
      if (Object.hasOwn(source, 'alsoNames')) card.alsoNames = [...(source.alsoNames ?? [])]
      else delete card.alsoNames
      const after = JSON.stringify([card.effectTags, card.abilities, card.alsoNames ?? []])
      if (before !== after) changedCards++
    }
    if (WRITE)
      await writeFile(publicPath, JSON.stringify(publicCards, null, 2) + (publicRaw.endsWith('\n') ? '\n' : ''), 'utf8')
  }
  console.log(`  规则元数据同步: ${changedCards} 张卡${WRITE ? '已更新' : '待更新'}`)
}

console.log(`模式: ${WRITE ? '✍ 写盘 (--write)' : '🔍 预演 (dry-run，不写盘)'}`)
const originals = await loadOriginals()
for (const dir of TARGET_DIRS) {
  await processDir(dir, originals)
}
await syncRuleMetadata()
console.log(WRITE ? '\n完成：已写盘。' : '\n完成：预演结束，未改动任何文件。加 --write 落盘。')
