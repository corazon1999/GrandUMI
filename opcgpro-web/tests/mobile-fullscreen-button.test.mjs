import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("手机竖屏对局显示安全区内的全屏按钮", async () => {
  const [button, route] = await Promise.all([
    read("src/components/game/MobileFullscreenButton.tsx"),
    read("src/components/home/LayoutPreviewRoute.tsx"),
  ]);

  assert.match(route, /isPhonePortrait && <MobileFullscreenButton \/>/);
  assert.match(button, /h-12 w-12/);
  assert.match(button, /var\(--layout-safe-right, env\(safe-area-inset-right\)\)/);
  assert.match(button, /var\(--layout-safe-top, env\(safe-area-inset-top\)\)/);
  assert.match(button, /aria-label=\{label\}/);
});

test("全屏按钮支持标准 API、WebKit API 和 iPhone 主屏幕降级说明", async () => {
  const [button, layout, manifest] = await Promise.all([
    read("src/components/game/MobileFullscreenButton.tsx"),
    read("src/app/layout.tsx"),
    read("src/app/manifest.ts"),
  ]);

  assert.match(button, /root\.requestFullscreen/);
  assert.match(button, /root\.webkitRequestFullscreen/);
  assert.match(button, /document\.exitFullscreen/);
  assert.match(button, /webkitExitFullscreen/);
  assert.match(button, /添加到主屏幕/);
  assert.match(button, /display-mode: standalone/);
  assert.match(layout, /appleWebApp:[\s\S]*capable: true/);
  assert.match(manifest, /display: "fullscreen"/);
});
