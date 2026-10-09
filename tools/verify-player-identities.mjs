import assert from "node:assert/strict";
import path from "node:path";

export async function verifyPublicPlayerIdentities(page, baseUrl, viewport) {
  await page.routeWebSocket("**", socket => { socket.onMessage(() => {}); });
  for (const view of ["names", "players", "friends", "chat", "game", "intro"]) {
    await page.goto(`${baseUrl}/layout-verification/player-identities?view=${view}`, { waitUntil: "domcontentloaded" });
    const scope = view === "intro" ? page.locator('[data-leader-clash="playing"]') : page.locator("body");
    await scope.locator('[data-equipped-season-title="S1 海贼王"]').first().waitFor({ state: "visible" });
    // 等开场或弹窗动画落定后检查实际可交互布局。
    await page.waitForTimeout(view === "intro" ? 1800 : 450);
    const layout = await scope.evaluate(root => {
      const box = element => {
        const r = element.getBoundingClientRect();
        return { x: r.x, y: r.y, right: r.right, bottom: r.bottom, width: r.width, height: r.height };
      };
      const intersects = (a, b) => Math.min(a.right, b.right) - Math.max(a.x, b.x) > 1
        && Math.min(a.bottom, b.bottom) - Math.max(a.y, b.y) > 1;
      // 开场卡片整体旋转，轴对齐外接框会产生假重叠；同一布局父级使用未旋转坐标。
      const localBox = element => ({ x: element.offsetLeft, y: element.offsetTop,
        right: element.offsetLeft + element.offsetWidth, bottom: element.offsetTop + element.offsetHeight });
      const overlap = (a, b) => a.offsetParent === b.offsetParent
        ? intersects(localBox(a), localBox(b)) : intersects(box(a), box(b));
      const visible = element => { const r = element.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
      const champions = [...root.querySelectorAll("[data-leader-champion]")].filter(visible);
      const versus = root.querySelector("[data-leader-clash-versus]");
      const names = [...root.querySelectorAll("[data-player-nameplate]")].filter(visible).map(element => {
        const name = element.querySelector("[data-player-name-text]");
        const title = element.querySelector("[data-equipped-season-title]");
        const bounds = box(element), text = box(name), badge = title ? box(title) : null;
        return { name: name.textContent, bounds, badge,
          overlap: title ? overlap(name, title) : false,
          championOverlap: champions.some(champion => overlap(element, champion)),
          versusOverlap: versus ? intersects(bounds, box(versus)) : false,
          overflow: element.scrollWidth > element.clientWidth + 1 };
      });
      const playerActions = [...root.querySelectorAll('[aria-label$="的玩家操作"] button')].filter(visible).map(box);
      return { names, playerActions, documentWidth: document.documentElement.scrollWidth, viewportWidth: innerWidth };
    });
    if (process.env.GRANDUMI_TEST_TEMP_ROOT) await page.screenshot({
      path: path.join(process.env.GRANDUMI_TEST_TEMP_ROOT, `player-titles-${view}-${viewport.width}.png`),
    });
    assert.ok(layout.names.length >= 2, `${view} 缺少真实玩家名字展示。`);
    assert.ok(layout.documentWidth <= layout.viewportWidth, `${view} 页面发生横向溢出。`);
    assert.ok(layout.names.every(item => item.badge && !item.overlap && !item.championOverlap && !item.versusOverlap && !item.overflow),
      `${view} 昵称、赛季称号或冠军称号重叠/溢出：${JSON.stringify(layout.names)}`);
    assert.ok(layout.names.every(({ bounds }) => bounds.x >= -1 && bounds.y >= -1
      && bounds.right <= viewport.width + 1 && bounds.bottom <= viewport.height + 1),
    `${view} 玩家称号超出可视区：${JSON.stringify(layout.names)}`);
    assert.ok(layout.playerActions.every(bounds => bounds.width >= 43.9 && bounds.height >= 43.9
      && bounds.x >= 0 && bounds.y >= 0 && bounds.right <= viewport.width && bounds.bottom <= viewport.height),
    `${view} 玩家操作按钮太小或超出可视区：${JSON.stringify(layout.playerActions)}`);
    if (view === "names") {
      await page.locator("[data-title-fixture-change]").click();
      await page.locator('[data-equipped-season-title="S1 五老星"]').first().waitFor();
      assert.equal(await page.locator('[data-equipped-season-title="S1 五老星"]').count(), 2, "同名玩家的所有展示未同步更新。");
      assert.equal(await page.locator('[data-equipped-season-title="S1 海贼王"]').count(), 0, "切换后仍残留旧称号。");
      await page.locator("[data-title-fixture-clear]").click();
      assert.equal(await page.locator('[data-equipped-season-title="S1 五老星"]').count(), 0, "取消佩戴后称号未消失。");
      assert.equal(await page.locator('[data-equipped-season-title="S1 海军元帅"]').count(), 2, "取消自己的称号影响了其他玩家。");
    }
  }
  await page.close();
}
