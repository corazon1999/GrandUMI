/**
 * 在对应实现与验证均完成后，清理历史定义中的“省略/近似”说明。
 * 默认只预演；--write 才写盘。脚本不会改动任何效果步骤。
 */
import { readFile, writeFile, readdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const DIR = path.join(ROOT, '服务端WebSocket', 'Effects', 'Definitions')
const WRITE = process.argv.includes('--write')

const COMPLETED_OMISSIONS = new Set([
  'EB01-027', 'EB01-056', 'EB02-003', 'EB02-016', 'EB02-019', 'EB02-021',
  'EB03-008', 'EB03-020', 'EB03-054', 'EB04-001', 'EB04-010', 'EB04-016', 'EB04-017', 'EB04-048',
  'OP01-024', 'OP01-029', 'OP01-034', 'OP01-035', 'OP01-040', 'OP01-118', 'OP01-119',
  'OP02-005', 'OP02-034', 'OP02-042', 'OP03-078', 'OP03-096', 'OP03-109',
  'OP04-001', 'OP04-039', 'OP04-041', 'OP04-074', 'OP04-075', 'OP04-076', 'OP04-082',
  'OP05-093', 'OP05-101', 'OP06-033', 'OP06-115',
  'OP07-004', 'OP07-035', 'OP07-056', 'OP07-057', 'OP07-071', 'OP07-075', 'OP07-105', 'OP07-111', 'OP07-117',
  'OP08-075', 'OP08-076', 'OP08-084', 'OP08-106', 'OP09-028', 'OP09-039', 'OP09-097', 'OP09-116',
  'OP10-097', 'OP11-028', 'OP11-084', 'OP12-109', 'OP13-080', 'OP13-109',
  'OP14-045', 'OP14-049', 'OP15-060', 'OP15-095',
])

const COMPLETED_OP12_GAPS = new Set([
  'OP12-021', 'OP12-036', 'OP12-072', 'OP12-081', 'OP12-109', 'OP12-117',
])

const COMPLETED_APPROXIMATIONS = new Set([
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

let markers = 0
let approximationMarkers = 0
let placeholders = 0
for (const file of (await readdir(DIR)).filter(name => name.endsWith('.json'))) {
  const filename = path.join(DIR, file)
  const raw = await readFile(filename, 'utf8')
  const definitions = JSON.parse(raw)
  let changed = false
  for (const [number, definition] of Object.entries(definitions)) {
    if (COMPLETED_OMISSIONS.has(number) && typeof definition?._matcher === 'string') {
      definition._matcher = '已按官方文本逐项精确补齐；组合式实现保留原 DSL 主体并覆盖完整成本、条件与后续结算。'
      markers++
      changed = true
    }
    if (COMPLETED_APPROXIMATIONS.has(number) && typeof definition?._matcher === 'string') {
      definition._matcher = '已按官方文本逐项精确实现并通过自动化校验。'
      approximationMarkers++
      changed = true
    }
    if (file === 'OP12_gap.json' && COMPLETED_OP12_GAPS.has(number) && definition?.complex === true) {
      delete definitions[number]
      placeholders++
      changed = true
    }
  }
  if (WRITE && changed)
    await writeFile(filename, JSON.stringify(definitions, null, 2) + (raw.endsWith('\n') ? '\n' : ''), 'utf8')
}

console.log(`${WRITE ? '已清理' : '将清理'}明确省略标记 ${markers} 处、近似实现标记 ${approximationMarkers} 处、已完成 OP12 占位 ${placeholders} 处。`)
