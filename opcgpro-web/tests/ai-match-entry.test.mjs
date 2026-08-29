import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("AI 对战入口复用 Bot 协议并明确 synthetic 边界", async () => {
  const [lobby, protocol, translations] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/i18n/core.mjs"),
  ]);

  assert.match(lobby, /HomeRequest\.enterBotMatch\(selectedDeck\.cards, botGoFirst, selectedDeck\.name\)/);
  assert.match(protocol, /enterBotMatch\(deck: string, goFirst: boolean = true, deckName\?: string\)/);
  assert.match(lobby, /AI 对战（实验）/);
  assert.match(lobby, /synthetic/);
  assert.match(lobby, /此模型未使用真人对局训练，只用于体验与工程验证。/);
  assert.match(lobby, />\s*创建 AI 对局\s*</);
  assert.match(translations, /"AI 对战（实验）": "AI Battle \(Experimental\)"/);
  assert.match(translations, /"AI 对战（实验）": "AI対戦（実験）"/);
});

test("AI 对战入口在手机竖屏保持可触控且不使用固定定位", async () => {
  const lobby = await readSource("../src/components/home/LobbyPanel.tsx");
  const section = lobby.match(/\{playMode === "bot" && \([\s\S]*?\{playMode === "friend" && \(/)?.[0];

  assert.ok(section, "应存在独立的 AI 对战区块");
  assert.match(section, /grid grid-cols-2/);
  assert.match(section, /aria-pressed=\{botGoFirst\}/);
  assert.match(section, /aria-pressed=\{!botGoFirst\}/);
  assert.match(section, /min-h-11/);
  assert.match(section, /h-12 w-full/);
  assert.doesNotMatch(section, /\bfixed\b|\bsticky\b|\babsolute\b/);
});
