import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("海克斯轮抽只展示服务端私密候选并提交轮次令牌", async () => {
  const [overlay, request, store] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/store/gameStore.ts"),
  ]);

  assert.match(overlay, /draft\?\.candidates/);
  assert.match(overlay, /GameRequest\.chooseHex\(draft\.roundId, hexId\)/);
  assert.match(request, /chooseHex: \(roundId: string, hexId: number\) => send\("ChooseHex", \{ roundId, hexId \}\)/);
  assert.match(store, /cloneHexModeSnapshot/);
  assert.doesNotMatch(overlay, /Math\.random|crypto\.getRandomValues/);
});

test("海克斯轮抽使用权威倒计时、超时恢复和四向安全区", async () => {
  const overlay = await readSource("../src/components/game/HexDraftOverlay.tsx");

  assert.match(overlay, /useServerCountdown/);
  assert.match(overlay, /GameRequest\.requestState\(\)/);
  assert.match(overlay, /var\(--layout-safe-top/);
  assert.match(overlay, /var\(--layout-safe-right/);
  assert.match(overlay, /var\(--layout-safe-bottom/);
  assert.match(overlay, /var\(--layout-safe-left/);
  assert.match(overlay, /overflow-y-auto/);
  assert.match(overlay, /grid-cols-1 gap-2\.5 sm:grid-cols-3/);
  assert.ok((overlay.match(/min-h-11/g)?.length ?? 0) >= 1);
});

test("海克斯已拥有列表公开双方结果且双舞台均可单独操作", async () => {
  const [owned, stage, actions, prompt] = await Promise.all([
    readSource("../src/components/game/HexOwnedPanel.tsx"),
    readSource("../src/components/game/StageSlot.tsx"),
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/components/game/PromptOverlay.tsx"),
  ]);

  assert.match(owned, /hexState\.myOwned/);
  assert.match(owned, /hexState\.opponentOwned/);
  assert.match(stage, /player\?\.stages/);
  assert.match(stage, /data-stage-index=\{index\}/);
  assert.match(actions, /my\.stages\.find/);
  assert.match(prompt, /my\?\.stages\.find/);
  assert.match(prompt, /opp\?\.stages\.find/);
});

test("大厅、协议与对局页面完整挂接海克斯模式", async () => {
  const [lobby, types, page] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../src/app/game/page.tsx"),
  ]);

  assert.match(lobby, /setMatchQueueKind\("hex"\)/);
  assert.match(lobby, /候选仅本人可见/);
  assert.match(types, /\| "ChooseHex"/);
  assert.match(types, /\| "hex"/);
  assert.match(page, /<HexDraftOverlay \/>/);
});
