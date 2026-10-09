import assert from "node:assert/strict";
import test from "node:test";
import { usePlayerIdentityStore } from "../src/store/playerIdentityStore.ts";

test("公开称号缓存按昵称隔离，佩戴、取消和重连清理都不残留", () => {
  const store = usePlayerIdentityStore.getState();
  store.reset();
  store.merge([{ name: "甲", equippedSeasonTitle: "S1 海贼王" }, { name: "乙", equippedSeasonTitle: "S1 五老星" }]);
  store.merge([{ name: "甲", equippedSeasonTitle: "S1 四皇" }]);
  assert.equal(usePlayerIdentityStore.getState().identities.甲.equippedSeasonTitle, "S1 四皇");
  assert.equal(usePlayerIdentityStore.getState().identities.乙.equippedSeasonTitle, "S1 五老星");
  store.merge([{ name: "甲", equippedSeasonTitle: null }]);
  assert.equal(usePlayerIdentityStore.getState().identities.甲.equippedSeasonTitle, null);
  store.merge([{ name: "__proto__", equippedSeasonTitle: "S1 海军大将" }, { name: "坏数据", equippedSeasonTitle: 3 }]);
  assert.equal(usePlayerIdentityStore.getState().identities.__proto__.equippedSeasonTitle, "S1 海军大将");
  assert.equal(Object.getPrototypeOf(usePlayerIdentityStore.getState().identities), null);
  assert.equal(usePlayerIdentityStore.getState().identities.坏数据, undefined);
  store.reset();
  assert.equal(Object.keys(usePlayerIdentityStore.getState().identities).length, 0);
});
