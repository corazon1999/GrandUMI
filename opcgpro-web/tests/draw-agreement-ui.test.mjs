import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

test("游戏菜单展示平局申请次数并提供同意与拒绝操作", () => {
  const source = read("src/components/game/GameMenu.tsx");

  assert.match(source, /出bug了，请求平局（已拒绝 \$\{drawRequestRejectionCount\}\/\$\{drawRequestRejectionLimit\} 次）/);
  assert.match(source, /drawRequestRejectionCount >= drawRequestRejectionLimit/);
  assert.match(source, /对方请求平局/);
  assert.match(source, />\s*不同意\s*</);
  assert.match(source, />\s*同意平局\s*</);
  assert.match(source, /平局不会改变双方赏金，也不会影响连胜或连败/);
  assert.match(source, /min-h-12/);
  assert.equal(source.match(/min-h-\[52px\]/g)?.length, 2);
  assert.match(source, /lastAction === "DrawRequestRejected"/);
});

test("平局终局不会渲染胜负或排位分数变化", () => {
  const page = read("src/app/game/page.tsx");
  const audio = read("src/hooks/useGameAudio.ts");
  const history = read("src/components/home/HistoryPanel.tsx");

  assert.match(page, /isDraw \? "本局平局"/);
  assert.match(page, /!isDraw && matchKind === "Ranked" && rankResult/);
  assert.match(audio, /if \(!isDraw\) play\(winnerIsMe \? "win" : "lose"\)/);
  assert.match(history, /m\.isDraw \? "平"/);
});
