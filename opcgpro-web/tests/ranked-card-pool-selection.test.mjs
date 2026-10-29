import assert from "node:assert/strict";
import test from "node:test";
import {
  STANDARD_RANKED_CARD_POOLS, deckUsesPreviewCards, loadRankedCardPool,
  rankedCardPoolStorageKey, saveRankedCardPool,
} from "../src/lib/rankedCardPool.ts";

function storage() {
  const values = new Map();
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
}

test("三个环境按实卡、新卡、全环境排列，首次和损坏偏好默认实卡", () => {
  assert.deepEqual(STANDARD_RANKED_CARD_POOLS.map(pool => pool.value), ["real", "preview", "all"]);
  const saved = storage();
  assert.equal(loadRankedCardPool("玩家", saved), "real");
  saved.setItem(rankedCardPoolStorageKey("玩家"), "未知环境");
  assert.equal(loadRankedCardPool("玩家", saved), "real");
});

test("保存三种环境后可以恢复，按账号隔离且不受账号大小写影响", () => {
  const saved = storage();
  for (const pool of ["preview", "all", "real"]) {
    saveRankedCardPool(" Player ", pool, saved);
    assert.equal(loadRankedCardPool("player", saved), pool);
    assert.equal(loadRankedCardPool("另一个玩家", saved), "real");
  }
  saveRankedCardPool("", "all", saved);
  assert.equal(loadRankedCardPool("", saved), "all");
  assert.equal(loadRankedCardPool("guest", saved), "real");
});

test("浏览器存储被禁用或写入失败时，偏好读取仍安全回退", () => {
  const blocked = { getItem() { throw new Error("禁止读取"); }, setItem() { throw new Error("禁止写入"); } };
  assert.equal(loadRankedCardPool("玩家", blocked), "real");
  assert.doesNotThrow(() => saveRankedCardPool("玩家", "all", blocked));
  assert.equal(loadRankedCardPool("玩家", null), "real");
});

test("前置实卡检查识别领航和主卡，忽略注释、异画地址及相近系列", () => {
  for (const number of ["OP18-003", "ST37-001", "ST38-001"]) {
    assert.equal(deckUsesPreviewCards(`OP15-001\n${number}`), true);
    assert.equal(deckUsesPreviewCards(`领航: OP15-001\n1 ${number.toLowerCase()}`), true);
    assert.equal(deckUsesPreviewCards(`1x${number}`), true);
    assert.equal(deckUsesPreviewCards("", number), true);
  }
  assert.equal(deckUsesPreviewCards("# OP18-003\n领航: OP15-001\n4 EB05-002 | /cards/st38/ST38-001.png"), false);
  assert.equal(deckUsesPreviewCards("OP180-003\nST370-001\nST380-001\nEB05-002"), false);
});
