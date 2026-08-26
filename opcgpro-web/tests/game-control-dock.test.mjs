import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { calculateLayoutScale } from "../src/lib/gameLayout.ts";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("对局浮控统一到视觉左下控制坞且不再占用右侧操作区", async () => {
  const [page, board, chat, menu, extension] = await Promise.all([
    read("src/app/game/page.tsx"),
    read("src/components/game/GameBoard.tsx"),
    read("src/components/game/GameChatPanel.tsx"),
    read("src/components/game/GameMenu.tsx"),
    read("src/components/game/MobileTurnExtensionButton.tsx"),
  ]);

  assert.doesNotMatch(page, /<GameMenu|<PlayerSafetyActions/);
  assert.match(chat, /data-game-control-dock/);
  assert.match(chat, /data-visual-anchor="left-bottom"/);
  assert.match(chat, /rotateQuarterTurn[\s\S]*?right:[\s\S]*?max\(0\.75rem, var\(--layout-safe-right/);
  assert.match(chat, /left: "calc\(0\.75rem \+ var\(--layout-safe-left/);
  assert.match(chat, /activeControl === "chat"/);
  assert.match(chat, /activeControl === "friends"/);
  assert.match(chat, /activeControl === "spectators"/);
  assert.match(chat, /activeControl === "more"/);
  assert.match(chat, /<GameMenu/);
  assert.match(chat, /<GameMenu[\s\S]*?<MobileTurnExtensionButton \/>/);
  assert.match(chat, /!isObserver && <MobileTurnExtensionButton \/>/);
  assert.match(chat, /playerToolsEnabled=\{!isObserver\}/);
  assert.doesNotMatch(page, /MobileTurnExtensionButton/);
  assert.doesNotMatch(extension, /\bfixed\b|--layout-safe-right|top:|right:/);
  assert.match(menu, /data-game-more-trigger/);
  assert.match(menu, /\{playerToolsEnabled && \(/);
  assert.doesNotMatch(menu, /className="fixed/);
  assert.match(board, /data-game-right-rail/);
  assert.match(board, /data-game-actions-panel/);
  assert.match(board, /<GameActions \/>/);
  for (const action of ["攻击", "出牌", "启动效果", "贴咚", "结束回合"]) {
    assert.doesNotMatch(menu, new RegExp(`>\\s*${action}\\s*<`));
  }
});

test("移除对手加好友入口并保留聊天、好友及按需观战入口", async () => {
  const chat = await read("src/components/game/GameChatPanel.tsx");

  assert.match(chat, /aria-label="打开局内聊天"/);
  assert.match(chat, /打开好友中心/);
  assert.match(chat, /showSpectatorIndicator &&/);
  assert.match(chat, /data-mobile-spectator-trigger/);
  assert.doesNotMatch(chat, /sendOpponentFriendRequest/);
  assert.doesNotMatch(chat, /data-opponent-friend-action/);
  assert.doesNotMatch(chat, /添加交战对手为好友/);
});

test("两档手机竖屏旋转后透明触控容器至少44像素且视觉图标更小", async () => {
  const [chat, menu, extension] = await Promise.all([
    read("src/components/game/GameChatPanel.tsx"),
    read("src/components/game/GameMenu.tsx"),
    read("src/components/game/MobileTurnExtensionButton.tsx"),
  ]);

  assert.match(chat, /h-12 w-12/);
  assert.match(chat, /h-9 w-9/);
  assert.match(menu, /h-12 w-12 min-h-12 min-w-12/);
  assert.match(menu, /h-9 w-9/);
  assert.match(extension, /h-12 w-12 min-h-12 min-w-12/);
  assert.match(extension, /h-9 w-9/);

  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const scale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });

    assert.ok(48 * scale >= 44, `${hostWidth}×${hostHeight} 的触控容器不足44px`);
    assert.ok(36 * scale < 44, `${hostWidth}×${hostHeight} 的视觉按钮没有缩小`);
  }
});

test("旋转画布的左下控制坞与牌桌 RightRail 保持独立几何区域", () => {
  const canvasWidth = 844;
  const canvasHeight = 390;
  const stageScale = Math.min(canvasWidth / 1280, canvasHeight / 720);
  const stageLeft = (canvasWidth - 1280 * stageScale) / 2;
  const rightRailRight = stageLeft + (1280 - 12) * stageScale;

  for (const safeRight of [0, 34]) {
    const dockLeft = canvasWidth - Math.max(12, safeRight) - 48;
    assert.ok(
      dockLeft >= rightRailRight - 1,
      `安全区 ${safeRight}px 时控制坞侵入 RightRail：${dockLeft} < ${rightRailRight}`,
    );
  }
});

test("存在观战者时移动加时与控制坞全部按钮保持间距且位于390宽度内", () => {
  const itemSize = 48;
  const gap = 4;
  const dockInset = 12;
  // DOM 顺序在顺时针旋转后会映射为从物理右侧到左侧；加时放在末尾可稳定贴近左边安全区。
  const sourceOrder = ["chat", "friends", "spectator", "more", "extension"];

  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const scale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });
    const rects = sourceOrder.map((name, sourceIndex) => {
      const reversedIndex = sourceOrder.length - 1 - sourceIndex;
      const left = (dockInset + reversedIndex * (itemSize + gap)) * scale;
      return { name, left, right: left + itemSize * scale };
    });
    const extension = rects.find((rect) => rect.name === "extension");

    assert.ok(extension, "缺少移动加时按钮几何数据");
    assert.ok(extension.left >= 0, `${hostWidth}×${hostHeight} 加时按钮越过物理左边界`);
    for (const rect of rects) {
      assert.ok(rect.right <= hostWidth, `${hostWidth}×${hostHeight} 的 ${rect.name} 越过物理右边界`);
      if (rect === extension) continue;
      assert.ok(
        extension.right <= rect.left || extension.left >= rect.right,
        `${hostWidth}×${hostHeight} 加时按钮与 ${rect.name} 重叠`,
      );
    }
    const physicalOrder = [...rects].sort((left, right) => left.left - right.left);
    for (let index = 1; index < physicalOrder.length; index += 1) {
      assert.ok(
        physicalOrder[index - 1].right < physicalOrder[index].left,
        `${hostWidth}×${hostHeight} 的 ${physicalOrder[index - 1].name} 与 ${physicalOrder[index].name} 未保留间距`,
      );
    }
  }
});
