import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("对局快照携带锁定规则版本且恢复时读取原版本", async () => {
  const [state, roomManager, replay] = await Promise.all([
    read("../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
    read("../服务端WebSocket/Game/GameRoomManager.cs"),
    read("../服务端WebSocket/Game/MatchReplay.cs"),
  ]);

  assert.match(state, /rulesetId = state\.RulesetId/);
  assert.match(roomManager, /rulesetId = engine\.State\.RulesetId/);
  assert.match(roomManager, /CardRulesetManager\.GetRequired\(rulesetId\)/);
  assert.match(replay, /CardRuleset\? ruleset = null/);
  assert.match(replay, /ruleset: ruleset/);
});

test("旧规则对局结束后客户端明确提示下一局启用新版卡效", async () => {
  const [manager, protocol, types] = await Promise.all([
    read("../服务端WebSocket/Game/GameRoomManager.cs"),
    read("src/net/GameProtocol.ts"),
    read("src/types/net.ts"),
  ]);

  assert.match(manager, /NotifyRulesetUpdateAfterMatch\(r\)/);
  assert.match(manager, /proto = "MsgRulesetUpdated"/);
  assert.match(protocol, /case "MsgRulesetUpdated"/);
  assert.match(protocol, /卡牌效果已更新，将从下一局开始生效/);
  assert.match(types, /interface MsgRulesetUpdated/);
});

test("规则激活只替换新局默认版本并公开旧版本房间计数", async () => {
  const [rulesets, bridge] = await Promise.all([
    read("../服务端WebSocket/Effects/Rules/CardRuleset.cs"),
    read("../服务端WebSocket/WebSocketBridge.cs"),
  ]);

  assert.match(rulesets, /Volatile\.Write\(ref _current, target\)/);
  assert.match(bridge, /case "MsgActivateRuleset"/);
  assert.match(bridge, /activeRoomCounts = GameRoomManager\.RoomCountsByRuleset/);
  assert.match(bridge, /进行中的旧版对局不受影响，新对局立即使用新版/);
});

test("管理员可在移动端安全确认规则激活并查看分版本对局数", async () => {
  const [panel, protocol, store] = await Promise.all([
    read("src/components/home/RulesetControlPanel.tsx"),
    read("src/net/HomeProtocol.ts"),
    read("src/store/netStore.ts"),
  ]);

  assert.match(panel, /activeRoomCounts\[ruleset\.id\]/);
  assert.match(panel, /激活后只影响新建对局/);
  assert.match(panel, /min-h-11/);
  assert.match(panel, /@\[520px\]:flex-row/);
  assert.match(panel, /break-all/);
  assert.match(protocol, /requestRulesetState\(\)/);
  assert.match(protocol, /activateRuleset\(rulesetId: string\)/);
  assert.match(store, /rulesets: RulesetAdminState/);
});
