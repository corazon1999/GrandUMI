/**
 * 全卡效果完整性审计（只读）。
 *
 * 默认只输出摘要；--verbose 输出卡号；--strict 在仍有已知缺口时返回非 0。
 * 本工具不会写入任何文件，适合在提交前和 CI 中重复运行。
 */

import { readFile, readdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const VERBOSE = process.argv.includes('--verbose')
const STRICT = process.argv.includes('--strict')

const KNOWN_CLEAR_GAPS = new Set([
  'EB02-030',
  'OP12-021', 'OP12-036', 'OP12-072', 'OP12-081',
  'ST36-001', 'ST36-002', 'ST36-004', 'ST36-005',
  'EB04-029', 'OP04-093', 'OP05-096',
  'EB03-008', 'EB04-016', 'OP11-028', 'OP11-031', 'OP11-084', 'OP11-119',
  'OP12-117', 'OP15-003', 'OP15-012', 'OP15-037', 'OP15-038', 'OP15-041',
  'OP15-056', 'OP15-057', 'OP15-084', 'OP15-115', 'OP16-057', 'OP16-068', 'ST05-010',
  'OP01-060',
  'P-057', 'P-058', 'P-059', 'P-060', 'P-120', 'P-121', 'P-122', 'P-126', 'P-128',
  'P-129', 'P-130', 'P-132', 'P-133', 'P-134', 'P-155',
  'ST19-003', 'ST19-004', 'ST19-005', 'ST20-004', 'ST20-005',
])

const DECLARED_OMISSIONS = new Set([
  'EB01-027', 'EB01-056',
  'EB02-003', 'EB02-016', 'EB02-019', 'EB02-021',
  'EB03-008', 'EB03-020', 'EB03-054',
  'EB04-001', 'EB04-010', 'EB04-016', 'EB04-017', 'EB04-048',
  'OP01-024', 'OP01-029', 'OP01-034', 'OP01-035', 'OP01-040', 'OP01-118', 'OP01-119',
  'OP02-005', 'OP02-034', 'OP02-042',
  'OP03-078', 'OP03-096', 'OP03-109',
  'OP04-001', 'OP04-039', 'OP04-041', 'OP04-074', 'OP04-075', 'OP04-076', 'OP04-082',
  'OP05-093', 'OP05-101',
  'OP06-033', 'OP06-115',
  'OP07-004', 'OP07-035', 'OP07-056', 'OP07-057', 'OP07-071', 'OP07-075', 'OP07-105', 'OP07-111', 'OP07-117',
  'OP08-075', 'OP08-076', 'OP08-084', 'OP08-106',
  'OP09-028', 'OP09-039', 'OP09-097', 'OP09-116',
  'OP10-097',
  'OP11-028', 'OP11-084',
  'OP12-109',
  'OP13-080', 'OP13-109',
  'OP14-045', 'OP14-049',
  'OP15-060', 'OP15-095',
])

const APPROXIMATE_EFFECTS = new Set([
  'OP02-010', 'OP02-048', 'OP02-057', 'OP02-058',
  'OP03-017', 'OP03-036', 'OP03-042', 'OP03-049', 'OP03-121', 'OP03-122',
  'OP04-091', 'OP04-115',
  'OP05-059', 'OP05-068', 'OP05-091',
  'OP06-029', 'OP06-077',
  'OP07-022', 'OP07-072', 'OP07-104', 'OP07-107', 'OP07-113',
  'OP08-053', 'OP08-095', 'OP08-103',
  'OP09-050', 'OP09-065', 'OP09-068', 'OP09-070', 'OP09-073', 'OP09-076', 'OP09-078', 'OP09-098', 'OP09-101', 'OP09-115',
  'OP10-067',
  'OP11-018', 'OP11-040',
  'OP14-096',
  'OP15-018', 'OP15-103', 'OP15-116',
  'PRB02-007', 'PRB02-016', 'PRB02-018',
])

const REQUIRED_TAGS = new Map([
  ['EB02-030', ['EventCounter']],
  ['EB04-029', ['EventCounter']],
  ['OP03-001', ['OnAttackDeclare', 'OnOppAttackDeclare']],
  ...['OP04-021', 'OP04-025', 'OP04-030', 'OP04-059', 'OP04-060', 'OP04-063', 'OP04-069', 'OP04-070', 'OP04-071', 'OP04-072']
    .map(number => [number, ['OnOppAttackDeclare']]),
  ...['OP07-098', 'OP10-037', 'OP10-118', 'OP12-024', 'OP13-084']
    .map(number => [number, ['PreKO']]),
  ['ST02-001', ['ActivatedMain']],
])

const BASE_KEYWORDS = ['阻挡者', '速攻', '双重攻击', '可攻击活跃', '不可阻挡', '流放', '速攻：角色']

async function loadCards(dir) {
  const map = new Map()
  for (const file of (await readdir(dir)).filter(name => name.endsWith('.json') && name !== 'allCards.json' && name !== 'imageManifest.json')) {
    const parsed = JSON.parse(await readFile(path.join(dir, file), 'utf8'))
    if (!Array.isArray(parsed)) continue
    for (const card of parsed) if (card?.number && !map.has(card.number)) map.set(card.number, card)
  }
  return map
}

async function loadDefinitions() {
  const result = new Map()
  const dir = path.join(ROOT, '服务端WebSocket', 'Effects', 'Definitions')
  for (const file of (await readdir(dir)).filter(name => name.endsWith('.json'))) {
    const parsed = JSON.parse(await readFile(path.join(dir, file), 'utf8'))
    for (const [number, definition] of Object.entries(parsed)) {
      if (!result.has(number)) result.set(number, [])
      result.get(number).push({ file, definition })
    }
  }
  return result
}

async function loadScriptedIds() {
  const result = new Set()
  const dir = path.join(ROOT, '服务端WebSocket', 'Effects', 'Scripted')
  for (const file of (await readdir(dir)).filter(name => name.endsWith('.cs'))) {
    const source = await readFile(path.join(dir, file), 'utf8')
    for (const match of source.matchAll(/CardNumber\s*=>\s*"([A-Z0-9-]+)"/g)) result.add(match[1])
  }
  return result
}

function isRealDefinition(entry) {
  const value = entry.definition
  return Array.isArray(value?.triggers) || Array.isArray(value?.counter) || value?.continuous != null
}

function hasImplementation(number, definitions, scripted) {
  return scripted.has(number) || (definitions.get(number) ?? []).some(isRealDefinition)
}

function isDeclaredBaseKeyword(text, keyword) {
  const escaped = keyword.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const anyKeyword = BASE_KEYWORDS.map(value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')
  const prefix = `(?:【(?:${anyKeyword})】(?:\\s*（[^）]*）)?\\s*)*`
  return new RegExp(`(^|[。\\r\\n])\\s*${prefix}【${escaped}】(?=\\s*(?:（|【|$))`).test(text)
}

function expectedBaseAbilities(text) {
  const abilities = BASE_KEYWORDS.filter(keyword => isDeclaredBaseKeyword(text, keyword))
  if (/(^|[。\r\n])此角色无法攻击。/.test(text)) abilities.push('此角色无法攻击')
  return abilities
}

function sameSet(left, right) {
  const a = [...new Set(left)].sort()
  const b = [...new Set(right)].sort()
  return a.length === b.length && a.every((value, index) => value === b[index])
}

function printGroup(label, values) {
  console.log(`${label}: ${values.length}`)
  if (VERBOSE && values.length) console.log(`  ${values.join('、')}`)
}

const cards = await loadCards(path.join(ROOT, '卡牌数据'))
const originals = await loadCards(path.join(ROOT, '卡牌数据_含原文'))
const definitions = await loadDefinitions()
const scripted = await loadScriptedIds()

const missingImplementations = [...KNOWN_CLEAR_GAPS]
  .filter(number => !hasImplementation(number, definitions, scripted))
  .sort()

const disconnectedTags = []
for (const [number, required] of REQUIRED_TAGS) {
  const tags = cards.get(number)?.effectTags ?? []
  const missing = required.filter(tag => !tags.includes(tag))
  if (missing.length) disconnectedTags.push(`${number}[${missing.join(',')}]`)
}

const staleOmissionMarkers = []
for (const number of DECLARED_OMISSIONS) {
  const entries = definitions.get(number) ?? []
  if (entries.some(({ definition }) => /忽略|省略|未实现|只实现|仅实现|暂不|略去/.test(definition?._matcher ?? '')))
    staleOmissionMarkers.push(number)
}

const staleApproximationMarkers = []
for (const number of APPROXIMATE_EFFECTS) {
  const entries = definitions.get(number) ?? []
  if (entries.some(({ definition }) => /近似|简化|无法|缺少/.test(definition?._matcher ?? '')))
    staleApproximationMarkers.push(number)
}

const abilityMismatches = []
for (const [number, original] of originals) {
  const current = cards.get(number)
  if (!current) continue
  const expected = expectedBaseAbilities(String(original.effectText ?? ''))
  if (number === 'OP12-036') expected.push('无法通过效果登场')
  if (number === 'OP04-001' || number === 'OP04-039') expected.push('此角色无法攻击')
  const actual = Array.isArray(current.abilities) ? current.abilities : []
  if (!sameSet(expected, actual)) abilityMismatches.push(number)
}

console.log(`卡牌总数: ${cards.size}`)
printGroup('明确缺失实现', missingImplementations)
printGroup('触发标签断连', disconnectedTags)
printGroup('仍含明确省略标记', staleOmissionMarkers.sort())
printGroup('仍含近似实现标记', staleApproximationMarkers.sort())
printGroup('基础关键词不一致', abilityMismatches.sort())

const issueCount = missingImplementations.length + disconnectedTags.length
  + staleOmissionMarkers.length + staleApproximationMarkers.length + abilityMismatches.length
if (STRICT && issueCount > 0) process.exitCode = 1
