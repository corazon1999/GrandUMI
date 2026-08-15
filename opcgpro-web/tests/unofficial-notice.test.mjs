import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("登录页不再渲染非官方项目说明面板", async () => {
  const source = await readSource("../src/components/home/LoginPanel.tsx");

  assert.doesNotMatch(source, /aria-label="非官方项目声明"/);
  assert.doesNotMatch(source, /非官方玩家项目/);
  assert.doesNotMatch(source, /GrandUMI 未获得万代、集英社、东映动画或其他相关权利方的授权、认可或赞助/);
});
