#!/usr/bin/env node
/**
 * OP15 网络对战端到端测试
 *
 * 启动两个 WebSocket 客户端，模拟一局完整对战：
 *   登录 → 携带 OP15 卡组匹配 → 互相 Mulligan → 推进到第 1 回合主要阶段 →
 *   一方投降 → 双方收到 MsgDuelOver
 *
 * 使用方法：
 *   1. 先启动服务端：cd 服务端WebSocket && dotnet run
 *   2. 在另一个终端执行：node tools/e2e/op15-match.mjs
 *
 * 依赖：Node 18+（内置 WebSocket）
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..", "..");
const OP15_PATH = path.join(ROOT, "卡牌数据", "OP15.json");

const WS_URL = process.env.WS_URL ?? "ws://localhost:8080/ws";
const RUN_ID = (process.env.E2E_RUN_ID ?? Date.now().toString(36)).replace(/[^0-9A-Za-z_-]/g, "");

const op15 = JSON.parse(fs.readFileSync(OP15_PATH, "utf8"));

// 选一个领航（OP15-001 克里克），构造一份合法的 50 张卡组
function buildDeck(leaderNumber) {
  const leader = op15.find((c) => c.number === leaderNumber);
  if (!leader) throw new Error(`未找到领航: ${leaderNumber}`);
  const leaderColors = new Set(leader.color.split("/"));

  // 主卡组：从 OP15 中选 50 张能与领航颜色匹配的非领航卡（同名 ≤4）
  const candidates = op15.filter((c) =>
    c.type !== "领航" &&
    c.number.startsWith("OP15-") &&
    c.color.split("/").some((co) => leaderColors.has(co))
  );

  const picks = [];
  const counts = {};
  let i = 0;
  while (picks.length < 50 && i < candidates.length * 4) {
    const c = candidates[i % candidates.length];
    const cnt = counts[c.number] || 0;
    if (cnt < 4) {
      picks.push(c.number);
      counts[c.number] = cnt + 1;
    }
    i++;
  }
  if (picks.length < 50) throw new Error(`OP15-${leaderNumber} 颜色范围内不足 50 张`);
  return [leader.number, ...picks].join("\n");
}

function makeClient(name, deck) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(WS_URL);
    const inbox = [];
    const waiters = [];
    ws.onopen    = () => { ws.send(JSON.stringify({ proto: "MsgSecret", vesion: "0.998" })); };
    ws.onmessage = (e) => {
      const msg = JSON.parse(e.data.toString());
      inbox.push(msg);
      const idx = waiters.findIndex(w => w.predicate(msg));
      if (idx >= 0) {
        const w = waiters[idx];
        waiters.splice(idx, 1);
        w.resolve(msg);
      }
    };
    ws.onerror = (e) => { console.error(`[${name}] ws error`, e); };
    ws.onclose = () => { console.log(`[${name}] ws closed`); };

    const client = {
      ws,
      name,
      deck,
      send: (msg) => ws.send(JSON.stringify(msg)),
      wait: (predicate, timeoutMs = 10000) => new Promise((res, rej) => {
        // 已在 inbox 中？
        const idx = inbox.findIndex(predicate);
        if (idx >= 0) { res(inbox.splice(idx, 1)[0]); return; }
        const w = { predicate, resolve: res };
        waiters.push(w);
        setTimeout(() => {
          const i = waiters.indexOf(w);
          if (i >= 0) { waiters.splice(i, 1); rej(new Error(`[${name}] timeout: ${predicate.name || "wait"}`)); }
        }, timeoutMs);
      }),
      close: () => ws.close(),
    };

    // 等 MsgSecret 回包 = 握手完成
    client.wait(m => m.proto === "MsgSecret", 5000)
      .then(() => resolve(client))
      .catch(reject);
  });
}

async function login(client, account) {
  client.send({ proto: "MsgLogin", account, password: "1" });
  const r = await client.wait(m => m.proto === "MsgLogin");
  if (!r.result) throw new Error(`[${client.name}] login fail: ${r.logStr}`);
  console.log(`[${client.name}] 登录成功`);
}

async function startMatch(client) {
  client.send({ proto: "MsgEnterMatch", deck: client.deck });
  const r = await client.wait(m => m.proto === "MsgEnterMatch");
  if (!r.result) throw new Error(`[${client.name}] enter match fail: ${r.logStr}`);
}

async function main() {
  console.log("=== OP15 端到端对战测试 ===");
  console.log(`服务端: ${WS_URL}`);

  const deck = buildDeck("OP15-001");
  console.log(`生成卡组 OP15-001 克里克，共 51 行`);

  const [a, b] = await Promise.all([
    makeClient("A", deck),
    makeClient("B", deck),
  ]);
  await login(a, `test_alice_${RUN_ID}`);
  await login(b, `test_bob_${RUN_ID}`);

  await Promise.all([startMatch(a), startMatch(b)]);
  console.log("匹配中...");

  await Promise.all([
    a.wait(m => m.proto === "MsgMatchFound"),
    b.wait(m => m.proto === "MsgMatchFound"),
  ]);
  console.log("匹配成功");

  await Promise.all([
    a.wait(m => m.proto === "MsgGameStart"),
    b.wait(m => m.proto === "MsgGameStart"),
  ]);

  // 等收到首份 GameState
  const [aInitial, bInitial] = await Promise.all([
    a.wait(m => m.proto === "MsgGameState"),
    b.wait(m => m.proto === "MsgGameState"),
  ]);
  console.log("收到初始 GameState");

  // 当前规则先由骰点胜者选择先后手，再进入调度手牌阶段。
  const chooser = aInitial.canChooseFirstPlayer ? a : bInitial.canChooseFirstPlayer ? b : null;
  if (chooser) {
    chooser.send({ proto: "MsgGameAction", action: "ChooseFirstPlayer", data: { goFirst: true } });
    await Promise.all([
      a.wait(m => m.proto === "MsgGameState" && m.firstPlayerChosen === true),
      b.wait(m => m.proto === "MsgGameState" && m.firstPlayerChosen === true),
    ]);
    console.log(`${chooser.name} 已选择先手`);
  }

  // 双方 mulligan 不重抽
  a.send({ proto: "MsgGameAction", action: "Mulligan", data: { redraw: false } });
  b.send({ proto: "MsgGameAction", action: "Mulligan", data: { redraw: false } });

  // 等 mulliganBothDone
  await Promise.all([
    a.wait(m => m.proto === "MsgGameState" && m.mulliganBothDone === true),
    b.wait(m => m.proto === "MsgGameState" && m.mulliganBothDone === true),
  ]);
  console.log("进入第 1 回合");

  // A 投降，应触发 GameOver
  a.send({ proto: "MsgGameAction", action: "Surrender", data: {} });

  const [aEnd, bEnd] = await Promise.all([
    a.wait(m => m.proto === "MsgGameState" && m.isGameOver === true),
    b.wait(m => m.proto === "MsgGameState" && m.isGameOver === true),
  ]);

  console.log(`A 收到 GameOver, winnerIsMe=${aEnd.winnerIsMe}`);
  console.log(`B 收到 GameOver, winnerIsMe=${bEnd.winnerIsMe}`);

  if (aEnd.winnerIsMe || !bEnd.winnerIsMe) {
    console.error("胜负判定异常");
    process.exit(1);
  }

  a.close();
  b.close();
  console.log("✓ 测试通过");
  setTimeout(() => process.exit(0), 100);
}

main().catch((e) => {
  console.error("× 测试失败:", e.message);
  process.exit(1);
});
