import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const expectedFiles = [
  "match-start.ogg",
  "turn-self.ogg",
  "turn-opponent.ogg",
  "card-play-character.ogg",
  "card-play-event.ogg",
  "card-play-stage.ogg",
  "attach-don.ogg",
  "attack.ogg",
  "block.ogg",
  "counter.ogg",
  "effect.ogg",
  "reveal.ogg",
  "damage.ogg",
  "ko.ogg",
  "win.ogg",
  "lose.ogg",
  "prompt.ogg",
  "error.ogg",
  "disconnect.ogg",
  "reconnect.ogg",
  "message.ogg",
];

const audioDir = path.resolve(process.cwd(), "public/audio/sfx/v1");
const maxFileBytes = 400 * 1024;
const maxTotalBytes = 2.5 * 1024 * 1024;
const errors = [];
let totalBytes = 0;

for (const file of expectedFiles) {
  const fullPath = path.join(audioDir, file);
  try {
    const info = await stat(fullPath);
    totalBytes += info.size;
    if (info.size > maxFileBytes) errors.push(`${file} 超过 400KB：${info.size} 字节`);
    const header = await readFile(fullPath, { encoding: null });
    if (header.subarray(0, 4).toString("ascii") !== "OggS") {
      errors.push(`${file} 不是有效的 Ogg 容器`);
    }
  } catch {
    errors.push(`缺少音效文件：${file}`);
  }
}

try {
  const actualFiles = (await readdir(audioDir)).filter((file) => file.endsWith(".ogg"));
  for (const file of actualFiles) {
    if (!expectedFiles.includes(file)) errors.push(`存在未登记的音效文件：${file}`);
  }
} catch {
  errors.push(`音效目录不存在：${audioDir}`);
}

if (totalBytes > maxTotalBytes) {
  errors.push(`音效总大小超过 2.5MB：${totalBytes} 字节`);
}

if (errors.length > 0) {
  console.error("音效资源检查失败：");
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

console.log(`音效资源检查通过：${expectedFiles.length} 个文件，共 ${totalBytes} 字节。`);
