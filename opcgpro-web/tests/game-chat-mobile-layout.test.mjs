import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { calculateLayoutScale } from "../src/lib/gameLayout.ts";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("旋转手机对局的聊天弹层固定在按钮组内侧且不再挤动按钮", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");

  assert.match(panel, /data-game-chat-root/);
  assert.match(panel, /data-game-chat-popovers/);
  assert.match(panel, /absolute flex flex-col gap-2/);
  assert.match(panel, /bottom-0 right-\[calc\(100%\+0\.5rem\)\] items-end/);
  assert.match(panel, /data-game-chat-dialog/);
  assert.match(panel, /100cqw - 5\.25rem/);
  assert.match(panel, /100cqh - 1\.5rem/);
  assert.doesNotMatch(panel, /max-w-\[calc\(100vw-1\.5rem\)\]/);
});

test("旋转手机聊天记录自适应剩余高度且快捷语只横向滚动", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");

  assert.match(panel, /data-game-chat-message-list/);
  assert.match(panel, /min-h-0 flex-1/);
  assert.match(panel, /data-game-chat-presets/);
  assert.match(panel, /touch-pan-x overflow-x-auto overscroll-x-contain/);
  assert.match(panel, /min-h-12 min-w-12 rounded-full shrink-0/);
  assert.ok((panel.match(/min-h-12/g)?.length ?? 0) >= 7);
});

test("旋转手机的局内聊天提示保留可读宽度", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");

  assert.match(panel, /data-game-chat-toast/);
  assert.match(panel, /min\(15rem, calc\(100cqw - 5\.25rem/);
  assert.match(panel, /var\(--layout-safe-left, 0px\)/);
  assert.match(panel, /var\(--layout-safe-right, 0px\)/);
});

test("两档手机竖屏旋转后聊天按钮仍保留至少44像素触控尺寸", () => {
  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const scale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });

    assert.ok(48 * scale >= 44, `${hostWidth}×${hostHeight} 的触控尺寸不足44px`);
  }
});

test("局内手动装饰发送入口已移除且旧服务端拒绝回包仍可提示", async () => {
  const [panel, request, protocol, events] = await Promise.all([
    read("src/components/game/GameChatPanel.tsx"),
    read("src/net/GameRequest.ts"),
    read("src/net/GameProtocol.ts"),
    read("src/net/eventBus.ts"),
  ]);

  assert.doesNotMatch(request, /sendChatDecoration/);
  assert.doesNotMatch(request, /proto: "MsgChatDecorationSend"/);
  assert.doesNotMatch(panel, /data-chat-decoration-quickbar/);
  assert.doesNotMatch(panel, /data-chat-decoration-slot/);
  assert.match(protocol, /displaySide: m\.displaySide \?\? null/);
  assert.match(protocol, /case "MsgChatDecorationSend"/);
  assert.match(protocol, /response\.result === false/);
  assert.match(protocol, /聊天装饰发送失败，请稍后重试/);
  assert.match(events, /displaySide\?: "self" \| "opponent" \| null/);
  assert.doesNotMatch(events, /styleToken: string/);
});

test("开场与胜利气泡锚定双方领袖并跟随旋转后的固定牌桌缩放", async () => {
  const [board, cinematic, css] = await Promise.all([
    read("src/components/game/GameBoard.tsx"),
    read("src/components/game/GameCinematicLayer.tsx"),
    read("src/app/globals.css"),
  ]);

  assert.match(board, /LeaderCinematicAnchor side=\{side === "my" \? "self" : "opponent"\}/);
  assert.match(board, /data-game-cinematic-board/);
  assert.match(cinematic, /data-game-cinematic-leader-anchor=\{side\}/);
  assert.match(cinematic, /right-\[calc\(100%\+0\.875rem\)\]/);
  assert.match(cinematic, /w-\[20rem\]/);
  assert.match(cinematic, /line-clamp-4/);
  assert.match(cinematic, /data-game-cinematic-bubble=\{victory \? "victory" : "opening"\}/);
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)/);
  assert.match(css, /game-cinematic-bubble--victory/);
});

test("装饰语录不再受聊天静音控制且普通聊天静音语义保持不变", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");

  assert.doesNotMatch(panel, /decorationBubbles/);
  assert.doesNotMatch(panel, /message\.decoration/);
  assert.match(panel, /mutedRef\.current && !isSelf/);
  assert.match(panel, /if \(!isSelf && !openRef\.current\)/);
});
