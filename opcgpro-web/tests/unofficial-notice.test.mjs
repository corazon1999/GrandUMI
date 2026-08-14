import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { translateText } from "../src/i18n/core.mjs";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

const notice =
  "GrandUMI 未获得万代、集英社、东映动画或其他相关权利方的授权、认可或赞助，与上述权利方不存在隶属、合作或其他关联关系。相关名称、角色及卡牌素材的权利归各自权利方所有。";

test("登录页在进入项目前明确展示非官方未授权声明", async () => {
  const source = await readSource("../src/components/home/LoginPanel.tsx");

  assert.match(source, /<aside[\s\S]*?aria-label="非官方项目声明"/);
  assert.match(source, /非官方玩家项目/);
  assert.match(source, /GrandUMI 未获得万代、集英社、东映动画或其他相关权利方的授权、认可或赞助/);
  assert.match(source, /相关名称、角色及卡牌素材的权利归各自权利方所有/);
});

test("非官方声明提供英文和日文版本", () => {
  assert.match(translateText(notice, "en"), /not authorized, endorsed, or sponsored/);
  assert.match(translateText(notice, "ja"), /許諾、承認または協賛を受けておらず/);
  assert.equal(translateText("非官方玩家项目", "en"), "Unofficial fan project");
  assert.equal(translateText("非官方玩家项目", "ja"), "非公式ファンプロジェクト");
});
