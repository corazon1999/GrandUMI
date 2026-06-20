// 批量把 public/sprites 下的卡图 PNG 转成小尺寸 webp 缩略图
// 输出到 public/sprites-thumb,目录结构镜像;网格与悬停预览用它,体积降约 96%
import sharp from 'sharp'
import { readdir, mkdir } from 'fs/promises'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const SRC = path.join(__dirname, 'public', 'sprites')
const DST = path.join(__dirname, 'public', 'sprites-thumb')
const WIDTH = 400
const QUALITY = 80
const CONC = 8

async function* walk(dir) {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name)
    if (e.isDirectory()) yield* walk(p)
    else if (/\.png$/i.test(e.name)) yield p
  }
}

let done = 0, ok = 0, fail = 0
async function main() {
  const all = []
  for await (const f of walk(SRC)) all.push(f)
  console.log(`共 ${all.length} 张待转 → ${DST}`)
  let i = 0
  async function worker() {
    while (i < all.length) {
      const f = all[i++]
      const rel = path.relative(SRC, f)
      const out = path.join(DST, rel.replace(/\.png$/i, '.webp'))
      try {
        await mkdir(path.dirname(out), { recursive: true })
        await sharp(f).resize({ width: WIDTH, withoutEnlargement: true }).webp({ quality: QUALITY }).toFile(out)
        ok++
      } catch (e) {
        fail++
        console.error('FAIL', rel, e.message)
      }
      if (++done % 200 === 0) console.log(`${done}/${all.length}`)
    }
  }
  await Promise.all(Array.from({ length: CONC }, worker))
  console.log(`完成: ok=${ok} fail=${fail}`)
}
main()
