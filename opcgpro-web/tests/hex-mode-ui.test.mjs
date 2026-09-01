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

test("海克斯选秀仅展示当前轮次品质，不展示本局共享品质序列", async () => {
  const [overlay, types] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(overlay, /const tier = draft\?\.tier \?\? "Silver"/);
  assert.match(overlay, /\{tierStyle\.label\}/);
  assert.doesNotMatch(overlay, /tierSequence|本局共享品质序列/);
  assert.match(types, /tierSequence: HexTierSnapshot\[\]/);
});

test("双方玩家信息卡内各有三个海克斯槽位且不再挂载独立详情面板", async () => {
  const [owned, board, stage, actions, prompt] = await Promise.all([
    readSource("../src/components/game/HexOwnedPanel.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/game/StageSlot.tsx"),
    readSource("../src/components/game/GameActions.tsx"),
    readSource("../src/components/game/PromptOverlay.tsx"),
  ]);

  assert.equal(board.match(/<HexOwnedSlots /g)?.length, 2);
  assert.match(board, /data-player-info-card="opponent"/);
  assert.match(board, /data-player-info-card="my"/);
  assert.equal(board.match(/grid min-h-11 grid-cols-\[minmax\(0,1fr\)_auto\] items-start gap-1/g)?.length, 2);
  assert.match(board, /items=\{hexState\?\.opponentOwned \?\? \[\]\}/);
  assert.match(board, /items=\{hexState\?\.myOwned \?\? \[\]\}/);
  assert.match(owned, /const MAX_OWNED_HEXES = 3/);
  assert.match(owned, /Array\.from\(\{ length: MAX_OWNED_HEXES \}/);
  assert.match(owned, /items\.slice\(0, MAX_OWNED_HEXES\)/);
  assert.match(owned, /data-hex-slot-index=\{index \+ 1\}/);
  assert.match(owned, /data-hex-slot-state=\{hex \? "owned" : "empty"\}/);
  assert.doesNotMatch(board, /<HexOwnedPanel/);
  assert.doesNotMatch(owned, /data-hex-details-panel|data-hex-owned-panel|tierSequence|candidates/);
  assert.doesNotMatch(owned, /title=/);

  assert.match(owned, /onPointerEnter=/);
  assert.match(owned, /onFocus=/);
  assert.match(owned, /onClick=/);
  assert.match(owned, /role=\{pinnedIndex === activeIndex \? "dialog" : "tooltip"\}/);
  assert.match(owned, /aria-describedby=\{isActive \? popoverId : undefined\}/);
  assert.match(owned, /hex\.name/);
  assert.match(owned, /TIER_META\[hex\.tier\]/);
  assert.match(owned, /activeHex\.description/);
  assert.match(owned, /activeTier\.label/);
  assert.match(owned, /Silver:/);
  assert.match(owned, /Gold:/);
  assert.match(owned, /Rainbow:/);
  assert.match(owned, /aria-label=\{`关闭\$\{activeHex\.name\}海克斯详情`\}/);
  assert.match(owned, /event\.key === "Escape"/);
  assert.match(owned, /document\.addEventListener\("pointerdown"/);

  assert.ok((owned.match(/h-11 min-h-11 w-11 min-w-11/g)?.length ?? 0) >= 2);
  assert.match(owned, /absolute inset-x-0/);
  assert.match(owned, /overflow-y-auto overscroll-contain/);
  assert.match(owned, /100cqh/);
  assert.match(owned, /var\(--layout-safe-top/);
  assert.match(owned, /var\(--layout-safe-bottom/);
  assert.match(board, /data-game-right-rail/);
  assert.match(board, /overflow-x-clip/);
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
