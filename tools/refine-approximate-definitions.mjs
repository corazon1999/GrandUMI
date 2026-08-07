/** 对 45 张历史近似定义做可机械校验的精确字段修正。默认预演，--write 写盘。 */
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const DIR = path.join(ROOT, '服务端WebSocket', 'Effects', 'Definitions')
const WRITE = process.argv.includes('--write')

const changes = new Map()
function edit(file, number, mutate) {
  if (!changes.has(file)) changes.set(file, [])
  changes.get(file).push([number, mutate])
}

edit('OP02_wf.json', 'OP02-010', d => { d.activated.then[0].filter.color = '红' })
edit('OP02_wf.json', 'OP02-048', d => {
  d.activated.cost.handDiscard = { n: 1, keyword: '和之国', text: '丢弃1张拥有《和之国》特征的手牌（可放弃）' }
})
edit('OP02_wf.json', 'OP02-058', d => { d.triggers[0].then[0].match.color = '蓝' })
edit('OP03_wf.json', 'OP03-042', d => { d.triggers[0].then[0].filter.color = '蓝' })
edit('OP04_wf.json', 'OP04-091', d => {
  d.triggers[0].cost = { restOwnKeyworded: { keyword: '' } }
})
edit('OP05_wf.json', 'OP05-068', d => { d.triggers[0].then[0].filter.color = '紫' })
edit('OP05_wf.json', 'OP05-091', d => {
  d.triggers[0].then[0].filter.color = '黑'
  d.triggers[0].then[2].filter.color = '黑'
})
edit('OP06_wf.json', 'OP06-029', d => { d.triggers[0].oncePerTurn = true })
edit('OP06_wf.json', 'OP06-077', d => { d.main.if = { selfDonNotMoreThanOpp: true } })
edit('OP07_wf.json', 'OP07-022', d => { d.triggers[0].then[0].match.color = '绿' })
edit('OP07_wf.json', 'OP07-072', d => { d.triggers[0].then[1].filter.color = '紫' })
for (const number of ['OP09-065', 'OP09-068', 'OP09-070', 'OP09-073', 'OP09-076'])
  edit('OP09_wf.json', number, d => { d.triggers[0].cost = { donReturnAtLeastOne: true } })
edit('OP09_wf.json', 'OP09-050', d => { d.triggers[0].then[0].match.color = '蓝' })
edit('OP09_wf.json', 'OP09-115', d => { d.main.then[0].filter = { hasTrigger: true } })
edit('OP10_wf.json', 'OP10-067', d => { d.triggers[0].then[0].filter.color = '紫' })
edit('OP11_wf.json', 'OP11-018', d => {
  d.main.then[2].filter = { currentPowerLte: 6000 }
  d.trigger[0].filter = { currentPowerLte: 6000 }
})
edit('OP15_wf.json', 'OP15-018', d => { d.triggers[0].then[0].filter = { currentPowerLte: 3000 } })
edit('PRB02.json', 'PRB02-016', d => {
  delete d.activated.cost.lifeToHand
  d.activated.cost.lifeToHandChoice = true
})
edit('PRB02.json', 'PRB02-018', d => { d.triggers[0].then[0].op = 'PlayCharFromHandOrTrash' })

let changed = 0
for (const [file, edits] of changes) {
  const filename = path.join(DIR, file)
  const raw = await readFile(filename, 'utf8')
  const definitions = JSON.parse(raw)
  for (const [number, mutate] of edits) {
    if (!definitions[number]) throw new Error(`${file} 缺少 ${number}`)
    mutate(definitions[number])
    changed++
  }
  if (WRITE) await writeFile(filename, JSON.stringify(definitions, null, 2) + (raw.endsWith('\n') ? '\n' : ''), 'utf8')
}

console.log(`${WRITE ? '已写入' : '将写入'} ${changed} 张近似定义的精确字段。`)
