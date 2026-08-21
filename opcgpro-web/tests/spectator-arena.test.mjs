import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("电脑端观战席从左下向左上排列并避开系统安全区", async () => {
  const arena = await read("src/components/game/SpectatorArena.tsx");

  assert.match(arena, /data-spectator-arena/);
  assert.match(arena, /hidden md:flex md:flex-col-reverse/);
  assert.match(arena, /data-seat-order=\{index \+ 1\}/);
  assert.match(arena, /const MAX_VISIBLE_SPECTATORS = 10/);
  assert.match(arena, /h-12 w-12/);
  assert.match(
    arena,
    /var\(--layout-safe-left, env\(safe-area-inset-left\)\)/,
  );
  assert.match(
    arena,
    /var\(--layout-safe-bottom, env\(safe-area-inset-bottom\)\)/,
  );
  assert.match(arena, /data-spectator-overflow/);
});

test("观战者发言按账号或名称映射到对应席位并显示限时文字气泡", async () => {
  const arena = await read("src/components/game/SpectatorArena.tsx");

  assert.match(arena, /message\.fromRole !== "spectator"/);
  assert.match(arena, /normalizeIdentity\(message\.fromAccount\)/);
  assert.match(arena, /normalizeIdentity\(candidate\.account\) === account/);
  assert.match(arena, /normalizeIdentity\(candidate\.name\) === name/);
  assert.match(arena, /data-spectator-chat-bubble/);
  assert.match(arena, /bubble\.text/);
  assert.match(arena, /const BUBBLE_DURATION_MS = 4000/);
  assert.match(arena, /min-h-12 w-full/);
});

test("电脑端使用竞技场观战席，手机端保留紧凑观战人数入口", async () => {
  const panel = await read("src/components/game/GameChatPanel.tsx");

  assert.match(panel, /<SpectatorArena/);
  assert.match(panel, /spectatorNames=\{spectatorNames\}/);
  assert.match(panel, /spectatorDetails=\{spectatorDetails\}/);
  assert.match(panel, /className="relative md:hidden"/);
  assert.match(panel, /data-mobile-spectator-trigger/);
  assert.match(
    panel,
    /toast\.fromRole === "spectator" && !isObserver \? "md:hidden"/,
  );
});
