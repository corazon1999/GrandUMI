import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/LobbyPanel.tsx", import.meta.url),
  "utf8",
);

test("大厅展示平台用途声明，且声明位于可滚动内容区", () => {
  assert.match(source, /aria-label="平台声明"/);
  assert.match(source, /本平台仅供技术学习与交流使用，不提供商品、服务或任何商业交易，亦不参与或支持任何形式的商业行为。/);
  assert.match(source, /overflow-y-auto[\s\S]*aria-label="平台声明"/);
  assert.match(source, /text-xs leading-5/);
});
