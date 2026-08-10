import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");
const [chooser, plaza, protocol, types] = await Promise.all([
  readSource("../src/components/home/DeckChoosePanel.tsx"),
  readSource("../src/components/home/DeckPlazaPanel.tsx"),
  readSource("../src/net/HomeProtocol.ts"),
  readSource("../src/types/net.ts"),
]);

test("卡组页以内部分页承载我的卡组和卡组广场", () => {
  assert.match(chooser, />我的卡组</);
  assert.match(chooser, />卡组广场</);
  assert.match(chooser, /<DeckPlazaPanel/);
  assert.match(chooser, /onPublishDeck\(name\)/);
});

test("卡组广场提供筛选、详情、点赞、复制和作者管理闭环", () => {
  for (const text of ["搜索卡组或作者", "全部颜色", "最多复制", "只看我的", "查看构筑", "复制到我的卡组", "更新投稿", "删除投稿"]) {
    assert.match(plaza, new RegExp(text));
  }
  assert.match(plaza, /HomeRequest\.toggleDeckPlazaLike/);
  assert.match(plaza, /HomeRequest\.copyDeckPlaza/);
  assert.match(plaza, /HomeRequest\.publishDeckPlaza/);
});

test("卡组广场详情复用一图流预览并保留投稿异画", () => {
  assert.match(plaza, /generateDeckImage/);
  assert.match(plaza, /item\.spriteMap\[number\] \|\| card\.sprite/);
  assert.match(plaza, /item\.leaderSprite \|\| leader\.sprite/);
  assert.match(plaza, /URL\.createObjectURL\(generated\.blob\)/);
  assert.match(plaza, /URL\.revokeObjectURL\(previewUrl\)/);
  assert.match(plaza, /data-testid="deck-plaza-image-preview"/);
  assert.match(plaza, /max-h-\[calc\(100dvh-15rem\)\]/);
  assert.match(plaza, /max-w-6xl/);
});

test("卡组广场协议支持分页读取和全部写操作", () => {
  for (const proto of ["MsgDeckPlazaList", "MsgPublishDeckPlaza", "MsgLikeDeckPlaza", "MsgCopyDeckPlaza", "MsgDeleteDeckPlaza"]) {
    assert.match(types, new RegExp(proto));
    assert.match(protocol, new RegExp(proto));
  }
  assert.match(types, /cards: string\[\]/);
  assert.match(types, /spriteMap: Record<string, string>/);
  assert.match(protocol, /setDeckPlazaPage/);
  assert.match(protocol, /refreshDeckPlaza/);
});
