/**
 * 一次性迁移：将卡牌 JSON 中的官方效果原文 effectText 降维为结构化字段。
 *
 *  - 新增 effectTags[]：该卡响应的触发时机枚举名（精确复刻服务端
 *    EffectRuntime.HasEffectForTrigger 的 switch，行为按构造一致）。
 *    注意 OnLifeRevealTrigger 由运行时按 trigger 字段单独判定，不在此列。
 *  - 新增 abilities[]：卡面能力关键字 + 攻击限制
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
const ABILITY_KEYWORDS = ['阻挡者', '速攻', '双重攻击', '可攻击活跃', '不可阻挡', '流放']

function computeEffectTags(text) {
  const tags = []
  for (const [name, pred] of Object.entries(TRIGGER_PREDICATES)) {
    if (pred(text)) tags.push(name)
  }
  return tags
}

function computeAbilities(text) {
  const abilities = []
  for (const kw of ABILITY_KEYWORDS) {
    if (text.includes(`【${kw}】`)) abilities.push(kw)
  }
  if (text.includes('此角色无法攻击')) abilities.push('此角色无法攻击')
  return abilities
}

/** 重建卡对象：保留键顺序，在 effectText 原位换成 effectTags + abilities */
function transformCard(card) {
  // 已迁移文件不再带 effectText；重复运行时必须保留人工补过的标签。
  if (!Object.hasOwn(card, 'effectText')) return card

  const text = typeof card.effectText === 'string' ? card.effectText : ''
  const effectTags = computeEffectTags(text)
  const abilities = computeAbilities(text)
  const out = {}
  for (const [k, v] of Object.entries(card)) {
    if (k === 'effectText') {
      out.effectTags = effectTags
      out.abilities = abilities
    } else {
      out[k] = v
    }
  }
  // 若原本无 effectText 键，仍补上结构化字段（理论上不会发生，稳妥处理）
  if (!('effectTags' in out)) { out.effectTags = effectTags; out.abilities = abilities }
  return out
}

async function processDir(dir) {
  let files
  try {
    files = (await readdir(dir)).filter(
      f => f.endsWith('.json') && (SET_FILTER.size === 0 || SET_FILTER.has(f)),
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
      const t = transformCard(c)
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

console.log(`模式: ${WRITE ? '✍ 写盘 (--write)' : '🔍 预演 (dry-run，不写盘)'}`)
for (const dir of TARGET_DIRS) {
  await processDir(dir)
}
console.log(WRITE ? '\n完成：已写盘。' : '\n完成：预演结束，未改动任何文件。加 --write 落盘。')
