import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("电脑与手机对局均显示安全区内的全屏按钮", async () => {
  const [button, route] = await Promise.all([
    read("src/components/game/MobileFullscreenButton.tsx"),
    read("src/components/home/LayoutPreviewRoute.tsx"),
  ]);

  assert.match(route, /<MobileFullscreenButton \/>/);
  assert.doesNotMatch(route, /isPhonePortrait && <MobileFullscreenButton \/>/);
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

test("iPhone 降级提示在旋转画布门户中仍可强制关闭", async () => {
  const [button, modal] = await Promise.all([
    read("src/components/game/MobileFullscreenButton.tsx"),
    read("src/components/ui/Modal.tsx"),
  ]);

  assert.match(modal, /layerClassName = "z-50"/);
  assert.match(modal, /pointer-events-auto fixed inset-0 \$\{layerClassName\}/);
  assert.match(button, /layerClassName="z-\[11000\]"/);
  assert.match(button, /!helpOpen &&/);
  assert.match(button, /onClose=\{closeHelp\}/);
  assert.match(button, /onClick=\{closeHelp\}/);
  assert.match(button, /min-h-12 w-full/);
  assert.match(button, /我知道了/);
});
