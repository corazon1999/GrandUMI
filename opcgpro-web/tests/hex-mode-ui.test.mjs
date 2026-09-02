import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("海克斯私密选秀提交轮次令牌并支持三个槽位各刷新一次", async () => {
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
  assert.match(types, /refreshRemaining\?: number/);
  assert.match(types, /refreshAvailableByCandidate\?: boolean\[\]/);
  assert.match(types, /refreshedCandidateIndices\?: number\[\]/);
  assert.match(types, /tierLabel\?: HexTierLabelSnapshot/);
  assert.match(store, /cloneHexModeSnapshot/);
  assert.match(store, /refreshAvailableByCandidate:[\s\S]*\.\.\.hexState\.activeDraft\.refreshAvailableByCandidate/);
  assert.match(store, /refreshedCandidateIndices:[\s\S]*\.\.\.hexState\.activeDraft\.refreshedCandidateIndices/);
  assert.match(overlay, /type HexRefreshState = "available" \| "refreshed" \| "locked"/);
  assert.match(overlay, /const refreshRemaining = draft\?\.refreshRemaining \?\?/);
  assert.match(overlay, /new Set\(refreshedCandidateIndices\)/);
  assert.match(overlay, /draft\?\.refreshAvailableByCandidate\?\.\[candidateIndex\]/);
  assert.match(overlay, /refreshStateFor\(candidateIndex\) !== "available"/);
  assert.match(overlay, /每个候选可各刷新一次，本轮还可刷新/);
  assert.match(overlay, /refreshState=\{refreshStateFor\(index\)\}/);
  assert.doesNotMatch(overlay, /if \(!draft\?\.refreshAvailable/);
  assert.doesNotMatch(overlay, /Math\.random|crypto\.getRandomValues/);
});

test("海克斯选秀使用权威倒计时、超时恢复、竖向三卡布局和四向安全区", async () => {
  const [overlay, frameCss] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/components/game/HexDraftOverlay.module.css"),
  ]);

  assert.match(overlay, /useServerCountdown/);
  assert.match(overlay, /GameRequest\.requestState\(\)/);
  assert.match(frameCss, /var\(--layout-safe-top/);
  assert.match(frameCss, /var\(--layout-safe-right/);
  assert.match(frameCss, /var\(--layout-safe-bottom/);
  assert.match(frameCss, /var\(--layout-safe-left/);
  assert.match(frameCss, /overflow: auto/);
  assert.match(overlay, /className=\{`\$\{styles\.overlay\} \$\{rotateQuarterTurn \? styles\.quarterTurn : ""\} @container`\}/);
  assert.match(frameCss, /width: min\(57rem, 100%\)/);
  assert.match(frameCss, /\.candidates[\s\S]*grid-template-columns: repeat\(3, minmax\(0, 16\.5rem\)\)/);
  assert.match(frameCss, /\.candidate[\s\S]*aspect-ratio: 0\.65/);
  assert.match(frameCss, /\.candidate::before/);
  assert.match(frameCss, /\.candidate::after/);
  assert.match(frameCss, /\.silver/);
  assert.match(frameCss, /\.gold/);
  assert.match(frameCss, /\.rainbow/);
  assert.ok((frameCss.match(/min-height: 3\.125rem/g)?.length ?? 0) >= 4);
  assert.match(frameCss, /@container \(max-height: 31rem\)/);
  assert.match(frameCss, /height: 14\.5rem/);
  assert.match(frameCss, /grid-template-columns: repeat\(3, minmax\(0, 10rem\)\)/);
  assert.match(frameCss, /@container \(max-width: 39rem\) and \(min-height: 39\.01rem\)/);
  assert.match(frameCss, /@media \(prefers-reduced-motion: reduce\)/);
});

test("海克斯选秀可隐藏查看牌桌并按轮次自动重开且不改变权威状态", async () => {
  const [overlay, frameCss] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/components/game/HexDraftOverlay.module.css"),
  ]);

  assert.match(overlay, /const \[isHidden, setIsHidden\] = useState\(false\)/);
  assert.match(overlay, /previous\.roundId !== draft\.roundId/);
  assert.match(overlay, /setIsHidden\(false\)/);
  assert.match(overlay, /if \(!draft \|\| isGameOver\) \{[\s\S]*setIsHidden\(false\)/);
  assert.match(overlay, /event\.key !== "Escape"/);
  assert.match(overlay, /data-private-hex-draft-hidden/);
  assert.match(overlay, /隐藏海克斯选择面板并查看场上局势/);
  assert.match(overlay, /重新打开\$\{tierHeading\}选择面板/);
  assert.match(overlay, /useLayoutQuarterTurn\(\)/);
  assert.match(overlay, /styles\.reopenQuarterTurn/);
  assert.match(overlay, /styles\.quarterTurn/);
  assert.doesNotMatch(overlay, /setInterval\([^)]*setIsHidden|clearHex|resetHex/);
  assert.match(frameCss, /\.reopen[\s\S]*var\(--layout-safe-top/);
  assert.match(frameCss, /\.reopen[\s\S]*var\(--layout-safe-right/);
  assert.match(frameCss, /right: calc\(4\.5rem \+ var\(--layout-safe-right/);
  assert.match(frameCss, /\.quarterTurn \.hide[\s\S]*left: 0/);
  assert.match(frameCss, /\.reopenQuarterTurn[\s\S]*var\(--layout-safe-left/);
  assert.match(frameCss, /\.reopen[\s\S]*min-height: 3\.25rem/);
});

test("海克斯选秀通过统一音频引擎播放原创出现、权威刷新与锁定反馈", async () => {
  const [overlay, types, manifest, audioCheck] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/audio/types.ts"),
    readSource("../src/audio/audioManifest.ts"),
    readSource("../scripts/check-audio-assets.mjs"),
  ]);

  assert.match(overlay, /const \{ play \} = useAudio\(\)/);
  assert.match(overlay, /play\("hexDraftOpen"\)/);
  assert.match(overlay, /previous\.refreshSignature !== refreshSignature[\s\S]*refreshedCandidateIndices\.length > 0[\s\S]*!isHidden[\s\S]*play\("hexDraftRefresh"\)/);
  assert.match(overlay, /!previous\.locked && draft\.myLocked && !isHidden[\s\S]*play\("hexDraftConfirm"\)/);
  assert.match(overlay, /previousDraftAudioRef\.current = \{[\s\S]*refreshSignature,[\s\S]*locked: draft\.myLocked/);
  assert.match(types, /\| "hexDraftOpen"/);
  assert.match(types, /\| "hexDraftRefresh"/);
  assert.match(types, /\| "hexDraftConfirm"/);
  assert.match(manifest, /hexDraftOpen:[\s\S]*hex-draft-open\.ogg/);
  assert.match(manifest, /hexDraftRefresh:[\s\S]*hex-draft-refresh\.ogg/);
  assert.match(manifest, /hexDraftConfirm:[\s\S]*hex-draft-confirm\.ogg/);
  assert.match(audioCheck, /hex-draft-open\.ogg/);
  assert.match(audioCheck, /hex-draft-refresh\.ogg/);
  assert.match(audioCheck, /hex-draft-confirm\.ogg/);
});

test("海克斯选秀仅展示当前轮次品质，不展示本局共享品质序列", async () => {
  const [overlay, types] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(overlay, /const tier = draft\?\.tier \?\? "Silver"/);
  assert.match(overlay, /\{tierHeading\}/);
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
  assert.match(board, /<HexOwnedSlots side="opponent" label="对方" items=\{hexState\?\.opponentOwned \?\? \[\]\} \/>/);
  assert.match(board, /<HexOwnedSlots side="my" label="我方" items=\{hexState\?\.myOwned \?\? \[\]\} \/>/);
  assert.match(owned, /const MAX_OWNED_HEXES = 3/);
  assert.match(owned, /Array\.from\(\{ length: MAX_OWNED_HEXES \}/);
  assert.match(owned, /items\.slice\(0, MAX_OWNED_HEXES\)/);
  assert.match(owned, /data-hex-slot-index=\{index \+ 1\}/);
  assert.match(owned, /data-hex-slot-state=\{hex \? "owned" : "empty"\}/);
  assert.match(owned, /data-hex-slot-visible-label=\{hex \? "name" : "empty"\}/);
  assert.match(owned, /className="line-clamp-3 w-full break-all/);
  assert.match(owned, /\{hex\.name\}/);
  assert.doesNotMatch(owned, /\{tierLabel \?\? "·"\}/);
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
  assert.match(owned, /activeTierLabel/);
  assert.match(owned, /Silver:/);
  assert.match(owned, /Gold:/);
  assert.match(owned, /Rainbow:/);
  assert.match(owned, /label: "棱彩"/);
  assert.match(owned, /hex\?\.tierLabel \?\? tier\?\.label/);
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
  assert.match(lobby, /三个候选各可刷新 1 次/);
  assert.match(lobby, /展示过的海克斯整局不会再次出现/);
  assert.match(lobby, /第 1、3、6 回合开始前/);
  assert.match(lobby, /可重复/);
  assert.match(lobby, /银\/金\/棱彩品质序列/);
  assert.match(types, /\| "ChooseHex"/);
  assert.match(types, /tierSequence: HexTierSnapshot\[\]/);
  assert.match(types, /\| "hex"/);
  assert.match(page, /<HexDraftOverlay \/>/);
});

test("玩家可见品质统一为棱彩且保留 Rainbow 协议兼容值", async () => {
  const [overlay, owned, lobby, types, catalog, modeLog, arenaLog, slotsLog] = await Promise.all([
    readSource("../src/components/game/HexDraftOverlay.tsx"),
    readSource("../src/components/game/HexOwnedPanel.tsx"),
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Hex/HexCatalog.cs"),
    readSource("../../changelog-cache/published/2026.09.02.1/2026-09-01-hex-mode.md"),
    readSource("../../changelog-cache/published/2026.09.02.1/2026-09-02-hex-draft-arena-presentation.md"),
    readSource("../../changelog-cache/published/2026.09.02.1/2026-09-02-hex-slots-in-player-cards.md"),
  ]);

  assert.match(types, /HexTierSnapshot = "Silver" \| "Gold" \| "Rainbow"/);
  assert.match(types, /HexTierLabelSnapshot = "银色" \| "金色" \| "棱彩"/);
  assert.match(overlay, /label: "棱彩海克斯"/);
  assert.match(overlay, /shortLabel: "棱彩"/);
  assert.match(overlay, /draft\?\.tierLabel \?\? tierStyle\.shortLabel/);
  assert.match(owned, /label: "棱彩"/);
  assert.match(catalog, /HexTier\.Rainbow => "棱彩"/);
  assert.match(catalog, /协议继续使用 Rainbow/);
  assert.match(lobby, /银\/金\/棱彩品质序列/);
  assert.match(modeLog, /银色、金色或棱彩品质序列/);
  assert.match(arenaLog, /银色、金色和棱彩品质/);
  assert.match(slotsLog, /银、金、棱彩品质辨识/);
  assert.doesNotMatch(
    [overlay, owned, lobby, catalog, modeLog, arenaLog, slotsLog].join("\n"),
    /彩色/,
  );
});
