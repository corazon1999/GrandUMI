import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";
import { translateText } from "../src/i18n/core.mjs";
import { resolveVisibleCardBackId } from "../src/lib/cardBackVisibility.mjs";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("卡背解析器默认关闭且只屏蔽当前主视角的 opponent 背面资源", () => {
  assert.equal(resolveVisibleCardBackId("custom-7", "opponent", false, "classic"), "custom-7");
  assert.equal(resolveVisibleCardBackId("custom-7", "my", true, "classic"), "custom-7");
  assert.equal(resolveVisibleCardBackId("custom-7", undefined, true, "classic"), "custom-7");
  assert.equal(resolveVisibleCardBackId("custom-7", "opponent", true, "classic"), "classic");
});

test("屏蔽偏好以默认关闭的兼容字段统一持久化", async () => {
  const store = await readSource("../src/store/settingsStore.ts");

  assert.match(store, /hideOpponentCardBack: false/);
  assert.match(store, /typeof parsed\.hideOpponentCardBack === "boolean"/);
  assert.match(store, /setHideOpponentCardBack: \(v: boolean\) => void/);
  assert.match(store, /setHideOpponentCardBack: \(v\) => \{[\s\S]*?persistCurrent\(\)/);
  assert.match(store, /saveToStorage\(\{[\s\S]*?hideOpponentCardBack,[\s\S]*?\}\)/);
  assert.match(store, /localStorage\.setItem\(KEY, JSON\.stringify\(s\)\)/);
});

test("统一 CardBack 入口只替换背面分支，正面与非牌桌预览保持原资源", async () => {
  const [cardBacks, cardBack, cardItem] = await Promise.all([
    readSource("../src/lib/cardBacks.ts"),
    readSource("../src/components/ui/CardBack.tsx"),
    readSource("../src/components/ui/CardItem.tsx"),
  ]);

  assert.match(cardBacks, /export const DEFAULT_CARD_BACK_ID = "classic"/);
  assert.match(cardBack, /useSettingsStore\(\(state\) => state\.hideOpponentCardBack\)/);
  assert.match(cardBack, /resolveVisibleCardBackId\([\s\S]*?DEFAULT_CARD_BACK_ID/);
  assert.match(cardBack, /const id = normalizeCardBackId\(visibleCardBackId\)/);
  assert.match(cardItem, /\{showFaceDown \? \(\s*<CardBack cardBackId=\{cardBackId\} side=\{cardBackSide\} \/>/);
  assert.match(cardItem, /\) : \(\s*<>\s*\{!imageFailed \? \(\s*<NextImage/);
});

test("敌方手牌、牌库、生命区和背面移动动画全部传递归一化 side", async () => {
  const [hand, deck, life, transitions] = await Promise.all([
    readSource("../src/components/game/HandArea.tsx"),
    readSource("../src/components/game/DeckPile.tsx"),
    readSource("../src/components/game/LifeArea.tsx"),
    readSource("../src/components/game/CardZoneTransitionLayer.tsx"),
  ]);

  assert.match(hand, /cardBackId=\{player\.cardBackId\}\s*cardBackSide=\{side\}/);
  assert.match(deck, /<CardBack cardBackId=\{player\?\.cardBackId\} side=\{side\} \/>/);
  assert.match(life, /<CardBack cardBackId=\{player\?\.cardBackId\} side=\{side\}/);
  assert.match(life, /cardBackId=\{player\?\.cardBackId\}\s*cardBackSide=\{side\}/);
  assert.match(transitions, /<CardBack cardBackId=\{flight\.cardBackId\} side=\{flight\.side\} decorative \/>/);

  const gameDir = new URL("../src/components/game/", import.meta.url);
  for (const file of await readdir(gameDir)) {
    if (!file.endsWith(".tsx")) continue;
    const source = await readFile(new URL(file, gameDir), "utf8");
    for (const tag of source.match(/<CardBack\b[^>]*\/>/g) ?? []) {
      assert.match(tag, /\bside=/, `${file} 的牌桌卡背必须显式传递 side`);
    }
  }
});

test("观战和回放继续沿用服务端按主视角归一化的 my/opponent 边界", async () => {
  const [snapshotBuilder, board, replayPage] = await Promise.all([
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/app/replay/[id]/page.tsx"),
  ]);

  assert.match(snapshotBuilder, /var myIdx = isSpectator \? Math\.Clamp\(spectatorPlayerIndex, 0, 1\) : viewerIndex/);
  assert.match(snapshotBuilder, /var oppIdx = 1 - myIdx/);
  assert.match(snapshotBuilder, /var my = BuildPlayerSnapshot\([\s\S]*?myIdx/);
  assert.match(snapshotBuilder, /var opponent = BuildPlayerSnapshot\([\s\S]*?oppIdx/);
  assert.match(board, /<PlayerMat\s*side="opponent"[\s\S]*?<PlayerMat\s*side="my"/);
  assert.match(replayPage, /syncFromServer\(snapshots\[clamped\]\)/);
  assert.match(replayPage, /<GameBoard isObserver=\{false\} isPlayback \/>/);
});

test("设置弹窗在手机竖屏和旋转牌桌中提供至少 44px 的可持久化开关", async () => {
  const settings = await readSource("../src/components/home/SettingsModal.tsx");

  assert.match(settings, /data-opponent-card-back-setting/);
  assert.match(settings, /role="switch"\s*aria-checked=\{hideOpponentCardBack\}/);
  assert.match(settings, /setHideOpponentCardBack\(!hideOpponentCardBack\)/);
  assert.match(settings, /min-h-12 min-w-12/);
  assert.match(settings, /flex flex-col items-stretch[\s\S]*?@\[420px\]:flex-row/);
  assert.match(settings, /敌方手牌、牌库、生命区等背面牌统一显示为经典卡背/);
  assert.equal(translateText("屏蔽对手卡背", "en"), "Hide opponent card backs");
  assert.equal(translateText("屏蔽对手卡背", "ja"), "相手のカード裏面を非表示");
  assert.equal(translateText("屏蔽开启", "en"), "Hiding on");
  assert.equal(translateText("屏蔽关闭", "ja"), "非表示オフ");
});
