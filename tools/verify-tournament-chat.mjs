import assert from "node:assert/strict";
import path from "node:path";

export async function verifyTournamentChatChannels(page, baseUrl, viewport) {
  for (const role of ["player", "spectator", "manager"]) {
    await page.goto(`${baseUrl}/layout-verification/tournaments?view=chat-${role}`, { waitUntil: "domcontentloaded" });
    await page.locator('[data-tournament-game-ready="true"]').waitFor();
    await page.locator("[data-layout-preview]").evaluate(frame => {
      for (const side of ["top", "right", "bottom", "left"]) frame.style.setProperty(`--layout-safe-${side}`, "12px");
    });
    // 参赛者、普通观众和管理观战均应看到本桌计时，手机需检查旋转后的实际位置。
    const clock = page.locator("[data-tournament-clock]");
    await clock.waitFor({ state: "visible" });
    assert.match(await clock.innerText(), /瑞士轮第1轮 · 第4桌[\s\S]*本桌 \d{2}:\d{2}/);
    assert.equal(await page.locator("[data-layout-rotated]").first().getAttribute("data-layout-rotated"), String(viewport.width < viewport.height), "赛事计时应随手机竖屏画布自动旋转。");
    const clockBox = await clock.boundingBox();
    assert.ok(clockBox && clockBox.x >= -1 && clockBox.y >= -1
      && clockBox.x + clockBox.width <= viewport.width + 1 && clockBox.y + clockBox.height <= viewport.height + 1, "赛事计时必须完整处于可视区内。");
    const chat = page.getByRole("button", { name: "打开局内聊天", exact: true });
    const chatBox = await chat.boundingBox();
    assert.ok(chatBox && chatBox.width >= 44 && chatBox.height >= 44, "聊天入口触控区不得小于44×44px。");
    assert.ok(clockBox.x + clockBox.width <= chatBox.x + 1 || chatBox.x + chatBox.width <= clockBox.x + 1
      || clockBox.y + clockBox.height <= chatBox.y + 1 || chatBox.y + chatBox.height <= clockBox.y + 1, "赛事计时不得与聊天入口重叠。");
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournament-clock-${role}-${viewport.width}.png`) });
    await chat.click();
    // 注入按钮仅为测试夹具提供数据，不占用真实牌桌的操作区。
    await page.getByRole("button", { name: "注入频道验证消息", exact: true }).evaluate(button => button.click());
    const dialog = page.locator("[data-game-chat-dialog]");
    const list = dialog.locator("[data-game-chat-message-list]");
    assert.equal(await list.locator('[data-game-chat-channel="players"]').count(), role === "spectator" ? 0 : 1);
    assert.equal(await list.locator('[data-game-chat-channel="spectators"]').count(), role === "player" ? 0 : 1);
    const rect = await dialog.boundingBox();
    assert.ok(rect && rect.x >= -1 && rect.y >= -1 && rect.x + rect.width <= viewport.width + 1 && rect.y + rect.height <= viewport.height + 1, "赛事聊天弹层应完整处于可视区内。");
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, "双频道布局导致横向溢出。");
    const rotated = await page.locator("[data-game-chat-root]").getAttribute("data-layout-rotated");
    assert.equal(rotated, viewport.width < viewport.height ? "true" : "false", "聊天布局应使用实际竖屏旋转结果。");
    if (role === "manager") {
      assert.match(await list.innerText(), /对战频道[\s\S]*对战频道验证消息[\s\S]*观战频道[\s\S]*观战频道验证消息/);
      for (const channel of ["观战频道", "对战频道"]) {
        const target = dialog.getByRole("button", { name: `发至${channel}`, exact: true });
        await target.click({ trial: true });
        const box = await target.boundingBox();
        assert.ok(box && box.width >= 44 && box.height >= 44, "管理频道切换按钮触控区不得小于44×44px。");
        await target.click();
        assert.equal(await target.getAttribute("aria-pressed"), "true");
        const input = dialog.getByPlaceholder(`输入${channel}消息…`, { exact: true });
        await input.fill(`管理发送至${channel}`);
        await dialog.getByRole("button", { name: "发送", exact: true }).click();
        await list.getByText(`管理发送至${channel}`, { exact: true }).waitFor();
        await page.waitForTimeout(1400);
      }
      assert.equal(await list.locator('[data-game-chat-channel="players"]').count(), 2);
      assert.equal(await list.locator('[data-game-chat-channel="spectators"]').count(), 2);
    } else {
      assert.equal(await dialog.locator("[data-game-chat-channel-controls]").count(), 0);
      assert.match(await dialog.innerText(), role === "player" ? /对战频道/ : /观战频道/);
    }
    for (const control of [dialog.getByRole("button", { name: "收起局内聊天", exact: true }), dialog.locator("[data-game-chat-preset-next]")]) {
      await control.click({ trial: true });
      const box = await control.boundingBox();
      assert.ok(box && box.width >= 44 && box.height >= 44, "赛事聊天主要操作应完整可点且触控区至少44×44px。");
    }
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({ path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `tournament-chat-${role}-${viewport.width}.png`) });
  }
  console.log(`赛事选手/观战频道隔离及管理双频道显示发送通过：${viewport.width}×${viewport.height}`);
}
