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

test("特殊聊天只发送槽位且由服务端回包决定文案样式与显示侧", async () => {
  const [panel, request, protocol, events] = await Promise.all([
    read("src/components/game/GameChatPanel.tsx"),
    read("src/net/GameRequest.ts"),
    read("src/net/GameProtocol.ts"),
    read("src/net/eventBus.ts"),
  ]);

  const sendMethod = request.match(/sendChatDecoration:[\s\S]*?\),\n/)?.[0] ?? "";
  assert.match(sendMethod, /proto: "MsgChatDecorationSend", slot/);
  assert.doesNotMatch(sendMethod, /text|styleToken|decorationId/);
  assert.match(protocol, /displaySide: m\.displaySide \?\? null/);
  assert.match(protocol, /decoration: m\.decoration \?\? null/);
  assert.match(events, /displaySide\?: "self" \| "opponent" \| null/);
  assert.match(events, /styleToken: string/);
  assert.match(panel, /chatDecorationBubbleClass\(bubble\.styleToken\)/);
  assert.match(panel, /data-style-token=\{bubble\.styleToken\}/);
});

test("六槽快捷装饰与双方限时气泡兼容旋转安全区和44像素触控", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");

  for (const slot of ["greeting", "praise", "thanks", "surprise", "mistake", "threat"]) {
    assert.match(panel, new RegExp(`slot: "${slot}"`));
  }
  assert.match(panel, /DECORATION_BUBBLE_MS = 4200/);
  assert.match(panel, /data-chat-decoration-bubble-layer/);
  assert.match(panel, /data-display-side=\{side\}/);
  assert.match(panel, /\["opponent", "self"\]/);
  assert.match(panel, /var\(--layout-safe-right,0px\)/);
  assert.match(panel, /var\(--layout-safe-bottom,0px\)/);
  assert.match(panel, /var\(--layout-safe-top,0px\)/);
  assert.match(panel, /data-chat-decoration-quickbar/);
  assert.match(panel, /min-h-12 min-w-\[5\.25rem\]/);
  assert.match(panel, /文案与样式由服务器确认/);
  assert.match(panel, /mutedRef\.current && !isSelf/);
  assert.match(panel, /else if \(!isSelf && !openRef\.current\)/);
});
