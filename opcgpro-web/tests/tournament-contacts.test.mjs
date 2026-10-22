import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import test from "node:test";
import vm from "node:vm";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import ts from "typescript";

const require = createRequire(import.meta.url);
const contactSource = await readFile(new URL("../src/components/ui/TournamentPlayerName.tsx", import.meta.url), "utf8");
const scopeSource = await readFile(new URL("../src/components/game/TournamentGameContacts.tsx", import.meta.url), "utf8");
const playerSource = await readFile(new URL("../src/components/ui/PlayerName.tsx", import.meta.url), "utf8");

function load(source, dependencies = {}) {
  const module = { exports: {} };
  const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true } }).outputText;
  vm.runInNewContext(compiled, { module, exports: module.exports, require: name => dependencies[name] ?? require(name) });
  return module.exports;
}

function fixture(kind = "Tournament", roomId = "room-1", clockRoom = "room-1") {
  const contacts = load(contactSource);
  const list = [{ account: "p0", name: "选手甲", qq: "123456789" }, { account: "p1", name: "选手乙", qq: null }];
  const Scope = load(scopeSource, {
    "@/store/gameStore": { useGameStore: selector => selector({ matchKind: kind, roomId }) },
    "@/net/TournamentClient": { useTournamentClock: selector => selector({ clock: { roomId: clockRoom, contacts: list } }) },
    "@/components/ui/TournamentPlayerName": contacts,
  }).default;
  const PlayerName = load(playerSource, {
    "@/store/netStore": { useNetStore: selector => selector({ connState: "disconnected", loggedIn: false }) },
    "@/store/playerIdentityStore": { usePlayerIdentityStore: selector => selector({ identities: {} }) },
    "@/net/publicPlayerIdentities": { watchPublicPlayerIdentity: () => () => {} },
    "./HunterIdentity": { EquippedSeasonTitleBadge: () => null },
    "./PlayerName.module.css": { identity: "identity", name: "name" },
    "./TournamentPlayerName": contacts,
  }).default;
  return { contacts, Scope, PlayerName };
}

test("赛事姓名显示完整QQ，无绑定账号明确显示未绑定", () => {
  const { contacts } = fixture();
  const bound = renderToStaticMarkup(React.createElement(contacts.default, { name: "选手甲", qq: "123456789" }));
  assert.match(bound, /选手甲/); assert.match(bound, /QQ 123456789/); assert.match(bound, /font-size:max\(10px, 0.8em\)/);
  const unbound = renderToStaticMarkup(React.createElement(contacts.default, { name: "test01", qq: null }));
  assert.match(unbound, /未绑定QQ/); assert.doesNotMatch(unbound, /QQ test01/);
});

test("只有匹配房间的赛事对局才在通用姓名组件显示QQ", () => {
  for (const [kind, roomId, clockRoom, expected] of [["Tournament", "room-1", "room-1", true], ["Ranked", "room-1", "room-1", false], ["Tournament", "room-2", "room-1", false], ["Tournament", null, "room-1", false]]) {
    const { Scope, PlayerName } = fixture(kind, roomId, clockRoom);
    const html = renderToStaticMarkup(React.createElement(Scope, null, React.createElement(PlayerName, { name: "选手甲" })));
    assert.equal(html.includes("QQ 123456789"), expected, "QQ显示必须与当前赛事及房间一致。");
  }
});

test("赛事作用域之外不显示QQ，历史重名不猜测绑定归属", () => {
  const { contacts, PlayerName } = fixture();
  assert.doesNotMatch(renderToStaticMarkup(React.createElement(PlayerName, { name: "选手甲" })), /data-tournament-qq/);
  const html = renderToStaticMarkup(React.createElement(contacts.TournamentContactsContext.Provider, {
    value: [{ account: "a", name: "重名", qq: "123456789" }, { account: "b", name: "重名", qq: "234567890" }],
  }, React.createElement(PlayerName, { name: "重名" })));
  assert.match(html, /QQ请见赛事对阵/); assert.doesNotMatch(html, /123456789|234567890/);
});
