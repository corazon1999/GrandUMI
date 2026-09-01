import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("海克斯私密选秀提交轮次令牌并支持一次单候选刷新", async () => {
  const [overlay, request, store, types] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/net/GameRequest.ts"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(overlay, /draft\?\.candidates/);
  assert.match(overlay, /GameRequest\.chooseHex\(draft\.roundId, hexId\)/);
  assert.match(overlay, /GameRequest\.refreshHex\(draft\.roundId, candidateIndex, expectedHexId\)/);
  assert.match(request, /chooseHex: \(roundId: string, hexId: number\) => send\("ChooseHex", \{ roundId, hexId \}\)/);
  assert.match(request, /send\("RefreshHex", \{ roundId, candidateIndex, expectedHexId \}\)/);
  assert.match(types, /\| "RefreshHex"/);
  assert.match(types, /refreshAvailable: boolean/);
  assert.match(types, /refreshedCandidateIndex: number \| null/);
  assert.match(store, /cloneHexModeSnapshot/);
  assert.doesNotMatch(overlay, /Math\.random|crypto\.getRandomValues/);
});

test("海克斯选秀使用权威倒计时、超时恢复、容器布局和四向安全区", async () => {
  const [overlay, frameCss] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/components/game/HexDraftOverlay.module.css"),
  ]);

  assert.match(overlay, /useServerCountdown/);
  assert.match(overlay, /GameRequest\.requestState\(\)/);
  assert.match(overlay, /var\(--layout-safe-top/);
  assert.match(overlay, /var\(--layout-safe-right/);
  assert.match(overlay, /var\(--layout-safe-bottom/);
  assert.match(overlay, /var\(--layout-safe-left/);
  assert.match(overlay, /overflow-y-auto/);
  assert.match(overlay, /className="@container fixed inset-0/);
  assert.match(overlay, /grid-cols-1 gap-2\.5 @\[640px\]:grid-cols-3/);
  assert.ok((overlay.match(/min-h-12/g)?.length ?? 0) >= 1);
  assert.match(frameCss, /\.candidate::before/);
  assert.match(frameCss, /\.candidate::after/);
  assert.match(frameCss, /\.silver/);
  assert.match(frameCss, /\.gold/);
  assert.match(frameCss, /\.rainbow/);
  assert.match(frameCss, /min-height: 3rem/);
});

test("右侧海克斯详情公开双方完整效果并兼容移动安全区", async () => {
  const [owned, board, stage, actions, prompt] = await Promise.all([
    readSource("../src/components/game/HexOwnedPanel.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/game/StageSlot.tsx"),
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/components/game/PromptOverlay.tsx"),
  ]);

  assert.match(owned, /hexState\.myOwned/);
  assert.match(owned, /hexState\.opponentOwned/);
  assert.match(owned, /<details/);
  assert.match(owned, /hex\.name/);
  assert.match(owned, /TIER_META\[hex\.tier\]/);
  assert.match(owned, /hex\.description/);
  assert.match(owned, /min-h-12/);
  assert.match(owned, /data-hex-details-panel/);
  assert.match(board, /data-game-right-rail/);
  assert.match(board, /var\(--layout-safe-top/);
  assert.match(board, /var\(--layout-safe-right/);
  assert.match(board, /var\(--layout-safe-bottom/);
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
  assert.match(lobby, /第 1、3、6 回合开始前/);
  assert.match(lobby, /可重复/);
  assert.match(types, /\| "ChooseHex"/);
  assert.match(types, /tierSequence: HexTierSnapshot\[\]/);
  assert.match(types, /\| "hex"/);
  assert.match(page, /<HexDraftOverlay \/>/);
});
