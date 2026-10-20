import assert from "node:assert/strict";

export async function verifyAudioIdle(page, baseUrl, viewport) {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.routeWebSocket("**", socket => socket.onMessage(() => {}));
  await page.addInitScript(() => {
    const NativeContext = window.AudioContext;
    window.__audioPowerCheck = { contexts: [], starts: 0, resumes: 0 };
    window.AudioContext = class extends NativeContext {
      constructor(...args) { super(...args); window.__audioPowerCheck.contexts.push(this); }
      resume() { window.__audioPowerCheck.resumes += 1; return super.resume(); }
      createBufferSource() {
        const source = super.createBufferSource();
        const start = source.start.bind(source);
        source.start = (...args) => { window.__audioPowerCheck.starts += 1; return start(...args); };
        return source;
      }
    };
  });
  await page.goto(`${baseUrl}/home`, { waitUntil: "domcontentloaded" });
  await page.getByRole("button", { name: "打开设置", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "设置", exact: true });
  const silent = () => page.waitForFunction(() => window.__audioPowerCheck.contexts.length > 0 && window.__audioPowerCheck.contexts.every(c => c.state === "suspended"));
  const sample = () => page.evaluate(() => ({ resumes: window.__audioPowerCheck.resumes, time: window.__audioPowerCheck.contexts[0].currentTime }));
  await silent();
  const before = await sample(); await page.waitForTimeout(300); const after = await sample();
  assert.ok(Math.abs(after.time - before.time) < 0.01, "空闲音频时钟仍持续推进。");
  const preview = dialog.getByRole("button", { name: "试听", exact: true });
  const play = async () => {
    // 试听复用消息音效，先越过它的 300ms 防连播冷却，避免把正常限流误判为恢复失败。
    await page.waitForTimeout(350);
    const count = await page.evaluate(() => window.__audioPowerCheck.starts);
    await preview.scrollIntoViewIfNeeded(); await preview.click();
    await page.waitForFunction(previous => window.__audioPowerCheck.starts > previous, count);
  };
  await play(); await silent();
  const mute = dialog.getByRole("button", { name: "音效开启", exact: true });
  await mute.scrollIntoViewIfNeeded(); await mute.click(); await silent();
  const muted = await sample();
  await dialog.getByRole("button", { name: "已静音", exact: true }).click();
  await page.waitForTimeout(200);
  assert.equal((await sample()).resumes, muted.resumes, "取消静音时无声唤醒音频。");
  await play();
  await page.evaluate(() => {
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => "hidden" });
    document.dispatchEvent(new Event("visibilitychange"));
  });
  await silent(); const hidden = await sample();
  await page.evaluate(() => {
    delete document.visibilityState;
    document.dispatchEvent(new Event("visibilitychange"));
  });
  await page.waitForTimeout(200);
  assert.equal((await sample()).resumes, hidden.resumes, "回到前台时无声唤醒音频。");
  await play(); await silent();
  const volume = dialog.getByLabel("音效音量", { exact: true });
  await volume.scrollIntoViewIfNeeded(); await volume.focus(); await volume.press("Home");
  await silent();
  assert.equal(await volume.inputValue(), "0");
  const zero = await sample(); await volume.press("End"); await page.waitForTimeout(200);
  assert.equal((await sample()).resumes, zero.resumes, "恢复音量时无声唤醒音频。");
  await play(); await silent();
  assert.deepEqual(errors, [], "音频生命周期出现浏览器异常。");
  await page.close();
  console.log(`音频空闲挂起通过：${viewport.width}×${viewport.height}`);
}
