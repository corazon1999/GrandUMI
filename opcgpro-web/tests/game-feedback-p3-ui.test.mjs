import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { getBattleTargetMarker } from "../src/lib/battleTargetMarker.ts";
import {
  calculateCardHoverPlacement,
  DESKTOP_CARD_HOVER_PREVIEW_HEIGHT_APPROX,
  DESKTOP_CARD_HOVER_PREVIEW_WIDTH,
  shouldShowDesktopCardHoverPreview,
} from "../src/lib/cardHoverPlacement.ts";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("G758：反击阶段只给权威战斗目标显示清晰的被攻击说明", () => {
  const marker = getBattleTargetMarker({
    phase: "Counter",
    isBattleTarget: true,
    isBlocker: false,
  });

  assert.deepEqual(marker, {
    text: "被攻击",
    ariaLabel: "当前被攻击对象，反击值将用于保护此对象",
    tone: "under-attack",
  });
  assert.equal(getBattleTargetMarker({ phase: "Counter", isBattleTarget: false, isBlocker: false }), null);

  const blockerMarker = getBattleTargetMarker({
    phase: "Counter",
    isBattleTarget: true,
    isBlocker: true,
  });
  assert.equal(blockerMarker?.text, "被攻击");
  assert.match(blockerMarker?.ariaLabel ?? "", /阻挡角色/);

  const preCounterMarker = getBattleTargetMarker({
    phase: "Block",
    isBattleTarget: true,
    isBlocker: false,
  });
  assert.equal(preCounterMarker?.text, "目标");
  assert.equal(preCounterMarker?.tone, "target");
});

test("G758：领袖与角色共用可访问且不抢占点击的生产徽标", async () => {
  const [badgeSource, fieldSource, leaderSource] = await Promise.all([
    readSource("../src/components/game/BattleTargetBadge.tsx"),
    readSource("../src/components/game/FieldArea.tsx"),
    readSource("../src/components/game/LeaderCard.tsx"),
  ]);

  assert.match(badgeSource, /role=\{marker\.tone === "under-attack" \? "status"/);
  assert.match(badgeSource, /aria-label=\{marker\.ariaLabel\}/);
  assert.match(badgeSource, /data-battle-target-marker=\{marker\.tone\}/);
  assert.match(badgeSource, /pointer-events-none/);
  assert.match(badgeSource, /bg-rose-700 text-white ring-2 ring-white\/90/);
  assert.match(fieldSource, /<BattleTargetBadge[\s\S]*?phase=\{phase\}[\s\S]*?isBattleTarget=\{isBattleTarget\}/);
  assert.match(leaderSource, /<BattleTargetBadge[\s\S]*?phase=\{phase\}[\s\S]*?isBattleTarget=\{isBattleTarget\}/);
});

test("G925：加大预览只允许桌面鼠标悬停触发", () => {
  assert.ok(DESKTOP_CARD_HOVER_PREVIEW_WIDTH >= 300);
  assert.ok(DESKTOP_CARD_HOVER_PREVIEW_HEIGHT_APPROX >= 560);
  assert.equal(shouldShowDesktopCardHoverPreview("mouse"), true);
  assert.equal(shouldShowDesktopCardHoverPreview("touch"), false);
  assert.equal(shouldShowDesktopCardHoverPreview("pen"), false);
  assert.equal(shouldShowDesktopCardHoverPreview(""), false);
});

test("G925：加大后的预览在桌面及两档手机安全区内都可收敛", () => {
  const rect = { left: 120, right: 180, top: 400, height: 80 };
  const viewports = [
    { width: 1280, height: 720, rotateQuarterTurn: false },
    { width: 390, height: 844, rotateQuarterTurn: true },
    { width: 360, height: 780, rotateQuarterTurn: true },
  ];

  for (const viewport of viewports) {
    const placement = calculateCardHoverPlacement({
      rect,
      viewportWidth: viewport.width,
      viewportHeight: viewport.height,
      previewWidth: DESKTOP_CARD_HOVER_PREVIEW_WIDTH,
      previewHeight: DESKTOP_CARD_HOVER_PREVIEW_HEIGHT_APPROX,
      rotateQuarterTurn: viewport.rotateQuarterTurn,
    });

    assert.ok(placement.left >= 8);
    assert.ok(placement.top >= 8);
    assert.ok(placement.left + placement.footprintWidth <= viewport.width - 8 + Number.EPSILON);
    assert.ok(placement.top + placement.footprintHeight <= viewport.height - 8 + Number.EPSILON);
  }
});
