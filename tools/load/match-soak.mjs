#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const WS_URL = process.env.WS_URL ?? "ws://127.0.0.1:8080/ws";
const METRICS_URL = process.env.METRICS_URL ?? "http://127.0.0.1:8080/metrics";
const CLIENTS = evenPositiveInt("CLIENTS", 200);
const DURATION_SECONDS = positiveInt("DURATION_SECONDS", 20);
const PHASE_TIMEOUT_MS = positiveInt("PHASE_TIMEOUT_MS", 180_000);
const RUN_ID = (process.env.RUN_ID ?? Date.now().toString(36)).replace(/[^0-9A-Za-z]/g, "").slice(-10);

const op15 = JSON.parse(fs.readFileSync(path.join(ROOT, "卡牌数据", "OP15.json"), "utf8"));
const deck = buildDeck("OP15-001");
const clients = [];
let closedUnexpectedly = 0;

function positiveInt(name, fallback) {
  const value = Number.parseInt(process.env[name] ?? "", 10);
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function evenPositiveInt(name, fallback) {
  const value = positiveInt(name, fallback);
  return value % 2 === 0 ? value : value + 1;
}

function buildDeck(leaderNumber) {
  const leader = op15.find((card) => card.number === leaderNumber);
  const colors = new Set(leader.color.split("/"));
  const candidates = op15.filter((card) =>
    card.type !== "领航" && card.color.split("/").some((color) => colors.has(color)));
  const cards = [];
  const counts = new Map();
  for (let cursor = 0; cards.length < 50; cursor++) {
    const card = candidates[cursor % candidates.length];
    const count = counts.get(card.number) ?? 0;
    if (count >= 4) continue;
    counts.set(card.number, count + 1);
    cards.push(card.number);
  }
  return [leaderNumber, ...cards].join("\n");
}

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

function withTimeout(promise, label, timeoutMs = PHASE_TIMEOUT_MS) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`${label} 超时`)), timeoutMs);
    promise.then(
      (value) => { clearTimeout(timer); resolve(value); },
      (error) => { clearTimeout(timer); reject(error); },
    );
  });
}

async function createClient(index) {
  const account = `load_${RUN_ID}_${index}`;
  const socket = new WebSocket(WS_URL);
  const secret = deferred();
  const login = deferred();
  const entered = deferred();
  const ready = deferred();
  const gameOver = deferred();
  const client = {
    account, socket, secret, login, entered, ready, gameOver,
    choseFirst: false, mulliganSent: false, active: false, stopping: false,
  };

  socket.addEventListener("open", () => {
    socket.send(JSON.stringify({ proto: "MsgSecret", vesion: "0.998", supportsStateDelta: false }));
  });
  socket.addEventListener("message", (event) => {
    let message;
    try { message = JSON.parse(event.data.toString()); } catch { return; }
    if (message.proto === "MsgSecret") secret.resolve();
    if (message.proto === "MsgLogin") {
      if (message.result) login.resolve();
      else login.reject(new Error(`${account} 登录失败：${message.logStr}`));
    }
    if (message.proto === "MsgEnterMatch") {
      if (message.result) entered.resolve();
      else entered.reject(new Error(`${account} 匹配失败：${message.logStr}`));
    }
    if (message.proto === "MsgGameState") {
      if (message.canChooseFirstPlayer && !client.choseFirst) {
        client.choseFirst = true;
        socket.send(JSON.stringify({
          proto: "MsgGameAction", action: "ChooseFirstPlayer", data: { goFirst: true },
        }));
      }
      if (message.firstPlayerChosen && !client.mulliganSent) {
        client.mulliganSent = true;
        socket.send(JSON.stringify({
          proto: "MsgGameAction", action: "Mulligan", data: { redraw: false },
        }));
      }
      if (message.mulliganBothDone && !client.active) {
        client.active = true;
        ready.resolve();
      }
      if (message.isGameOver) gameOver.resolve();
    }
  });
  socket.addEventListener("error", () => {
    const error = new Error(`${account} WebSocket 错误`);
    secret.reject(error); login.reject(error); entered.reject(error); ready.reject(error);
  });
  socket.addEventListener("close", () => {
    if (!client.stopping) closedUnexpectedly += 1;
  });

  await withTimeout(secret.promise, `${account} 握手`, 10_000);
  socket.send(JSON.stringify({ proto: "MsgLogin", account, password: "load-test" }));
  await withTimeout(login.promise, `${account} 登录`, 10_000);
  clients.push(client);
  return client;
}

async function fetchSelectedMetrics() {
  const response = await fetch(METRICS_URL);
  if (!response.ok) throw new Error(`指标端点返回 HTTP ${response.status}`);
  const selected = {};
  for (const line of (await response.text()).split("\n")) {
    const match = line.match(/^(grandumi_(?:connections|rooms|process_working_set_bytes|gc_heap_bytes|room_action_queue_depth|websocket_dropped_messages_total|room_journal_dropped_total|replay_dropped_total|matchlog_dropped_total)) ([0-9.eE+-]+)/);
    if (match) selected[match[1]] = Number(match[2]);
  }
  return selected;
}

async function main() {
  console.log(`活跃对局压测：${WS_URL}，玩家=${CLIENTS}，房间=${CLIENTS / 2}`);
  const startedAt = performance.now();
  for (let offset = 0; offset < CLIENTS; offset += 40) {
    const count = Math.min(40, CLIENTS - offset);
    await Promise.all(Array.from({ length: count }, (_, index) => createClient(offset + index)));
  }

  await Promise.all(clients.map((client) => {
    client.socket.send(JSON.stringify({ proto: "MsgEnterMatch", deck }));
    return withTimeout(client.entered.promise, `${client.account} 进入匹配`);
  }));
  await Promise.all(clients.map((client) => withTimeout(client.ready.promise, `${client.account} 对局就绪`)));
  const readyMs = performance.now() - startedAt;
  const steadyMetrics = await fetchSelectedMetrics();
  console.log(`全部对局就绪，耗时=${readyMs.toFixed(0)}ms`);
  console.log(JSON.stringify(steadyMetrics, null, 2));

  await new Promise((resolve) => setTimeout(resolve, DURATION_SECONDS * 1000));
  for (const client of clients) {
    client.socket.send(JSON.stringify({ proto: "MsgGameAction", action: "Surrender", data: {} }));
  }
  await Promise.allSettled(clients.map((client) => withTimeout(client.gameOver.promise, `${client.account} 对局结束`, 10_000)));
  await new Promise((resolve) => setTimeout(resolve, 1000));
  const finalMetrics = await fetchSelectedMetrics();

  for (const client of clients) {
    client.stopping = true;
    try { client.socket.close(1000, "load-test-complete"); } catch { }
  }
  console.log(JSON.stringify({
    clients: CLIENTS,
    rooms: CLIENTS / 2,
    readyMs: Number(readyMs.toFixed(1)),
    closedUnexpectedly,
    steadyMetrics,
    finalMetrics,
  }, null, 2));
  if (closedUnexpectedly > 0) process.exitCode = 1;
}

main().catch((error) => {
  console.error(`活跃对局压测失败：${error.message}`);
  for (const client of clients) {
    client.stopping = true;
    try { client.socket.close(); } catch { }
  }
  process.exitCode = 1;
});
