import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import {
  DRAW_REQUEST_DESCRIPTION_MAX_LENGTH,
  prepareDrawRequestDescription,
} from "../src/lib/drawRequest.ts";

const root = path.resolve(import.meta.dirname, "..");

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), "utf8");
}

test("Bug 描述在客户端必填、去除首尾空白并遵守长度边界", () => {
  assert.deepEqual(prepareDrawRequestDescription(" \n\t "), {
    ok: false,
    error: "请填写发生了什么 Bug",
  });
  assert.deepEqual(prepareDrawRequestDescription("  效果结算卡住了  \n"), {
    ok: true,
    description: "效果结算卡住了",
  });
  assert.equal(prepareDrawRequestDescription("甲".repeat(DRAW_REQUEST_DESCRIPTION_MAX_LENGTH)).ok, true);
  const tooLong = prepareDrawRequestDescription("甲".repeat(DRAW_REQUEST_DESCRIPTION_MAX_LENGTH + 1));
  assert.equal(tooLong.ok, false);
  assert.match(tooLong.error, /不能超过 500 个字符/);
});

test("游戏菜单收集并传递描述，同时提供同意与拒绝操作", () => {
  const source = read("src/components/game/GameMenu.tsx");
  const request = read("src/net/GameRequest.ts");

  assert.match(source, /出bug了，请求平局（已拒绝 \$\{drawRequestRejectionCount\}\/\$\{drawRequestRejectionLimit\} 次）/);
  assert.match(source, /drawRequestRejectionCount >= drawRequestRejectionLimit/);
  assert.match(source, /发生了什么 Bug？/);
  assert.match(source, /prepareDrawRequestDescription\(drawDescription\)/);
  assert.match(source, /GameRequest\.requestDraw\(prepared\.description\)/);
  assert.match(request, /requestDraw:\s+\(description: string\) => send\("RequestDraw", \{ description \}\)/);
  assert.match(source, /对方请求平局/);
  assert.match(source, /对方填写的 Bug 描述/);
  assert.match(source, /\{drawRequestDescription \|\| "未收到 Bug 描述，请谨慎处理。"\}/);
  assert.match(source, /whitespace-pre-wrap break-words/);
  assert.match(source, />\s*不同意\s*</);
  assert.match(source, />\s*同意平局\s*</);
  assert.match(source, /平局不会改变双方赏金，也不会影响连胜或连败/);
  assert.match(source, /min-h-12/);
  assert.ok((source.match(/min-h-\[52px\]/g)?.length ?? 0) >= 4);
  assert.match(source, /lastAction === "DrawRequestRejected"/);
  assert.match(source, /disabled=\{isPending\}/);
});

test("权威快照同步和离局清理都不会保留旧描述", () => {
  const store = read("src/store/gameStore.ts");
  const types = read("src/types/net.ts");

  assert.match(types, /drawRequestDescription\?: string \| null/);
  assert.match(store, /s\.drawRequestDescription = msg\.drawRequestDescription \?\? null/);
  assert.match(store, /drawRequestDescription: null/);
  assert.match(store, /s\.drawRequestDescription = null/);
});

test("平局终局不会渲染胜负或排位分数变化", () => {
  const overlay = read("src/components/game/GameOverOverlay.tsx");
  const audio = read("src/hooks/useGameAudio.ts");
  const history = read("src/components/home/HistoryPanel.tsx");

  assert.match(overlay, /isDraw \? "本局平局"/);
  assert.match(overlay, /!isDraw && \(matchKind === "Ranked" \|\| matchKind === "RankedWild"\) && rankResult/);
  assert.match(audio, /if \(!isDraw\) play\(winnerIsMe \? "win" : "lose"\)/);
  assert.match(history, /m\.isDraw \? "平"/);
});
