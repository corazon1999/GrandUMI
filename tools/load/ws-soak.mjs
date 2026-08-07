#!/usr/bin/env node

const WS_URL = process.env.WS_URL ?? "ws://127.0.0.1:8080/ws";
const CLIENTS = positiveInt("CLIENTS", 500);
const DURATION_SECONDS = positiveInt("DURATION_SECONDS", 30);
const PING_INTERVAL_MS = Math.max(2000, positiveInt("PING_INTERVAL_MS", 5000));
const CONNECT_BATCH = Math.max(1, positiveInt("CONNECT_BATCH", 50));

const clients = [];
const latencies = [];
let connected = 0;
let failed = 0;
let closedUnexpectedly = 0;
let pingSent = 0;
let pingReceived = 0;
let stopping = false;

function positiveInt(name, fallback) {
  const value = Number.parseInt(process.env[name] ?? "", 10);
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function percentile(values, ratio) {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * ratio) - 1)];
}

function connectOne(index) {
  return new Promise((resolve) => {
    const socket = new WebSocket(WS_URL);
    const pending = new Map();
    let ready = false;
    let resolved = false;

    const finish = (ok) => {
      if (resolved) return;
      resolved = true;
      if (ok) connected += 1;
      else failed += 1;
      resolve();
    };

    const timeout = setTimeout(() => {
      try { socket.close(); } catch { }
      finish(false);
    }, 10_000);

    socket.addEventListener("open", () => {
      socket.send(JSON.stringify({ proto: "MsgSecret", vesion: "0.998", supportsStateDelta: true }));
    });
    socket.addEventListener("message", (event) => {
      let message;
      try { message = JSON.parse(event.data.toString()); } catch { return; }
      if (message.proto === "MsgSecret" && !ready) {
        ready = true;
        clearTimeout(timeout);
        clients.push({ socket, pending, index });
        finish(true);
        return;
      }
      if (message.proto === "MsgPing" && pending.has(message.id)) {
        const startedAt = pending.get(message.id);
        pending.delete(message.id);
        latencies.push(performance.now() - startedAt);
        pingReceived += 1;
      }
    });
    socket.addEventListener("error", () => finish(false));
    socket.addEventListener("close", () => {
      if (!ready) finish(false);
      else if (!stopping) closedUnexpectedly += 1;
    });
  });
}

async function connectAll() {
  for (let offset = 0; offset < CLIENTS; offset += CONNECT_BATCH) {
    const count = Math.min(CONNECT_BATCH, CLIENTS - offset);
    await Promise.all(Array.from({ length: count }, (_, i) => connectOne(offset + i)));
    if (offset + count < CLIENTS) await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

function sendPings(sequence) {
  for (const client of clients) {
    if (client.socket.readyState !== WebSocket.OPEN) continue;
    const id = `${sequence}-${client.index}`;
    client.pending.set(id, performance.now());
    client.socket.send(JSON.stringify({ proto: "MsgPing", id }));
    pingSent += 1;
  }
}

async function main() {
  console.log(`WebSocket 稳态压测：${WS_URL}，目标连接=${CLIENTS}，持续=${DURATION_SECONDS}s`);
  const connectStartedAt = performance.now();
  await connectAll();
  const connectMs = performance.now() - connectStartedAt;
  console.log(`握手完成：成功=${connected}，失败=${failed}，耗时=${connectMs.toFixed(0)}ms`);
  if (connected === 0) throw new Error("没有连接成功");

  let sequence = 0;
  sendPings(sequence++);
  const timer = setInterval(() => sendPings(sequence++), PING_INTERVAL_MS);
  await new Promise((resolve) => setTimeout(resolve, DURATION_SECONDS * 1000));
  clearInterval(timer);
  await new Promise((resolve) => setTimeout(resolve, 1000));

  stopping = true;
  for (const client of clients) {
    try { client.socket.close(1000, "load-test-complete"); } catch { }
  }

  const summary = {
    targetClients: CLIENTS,
    connected,
    failed,
    closedUnexpectedly,
    connectMs: Number(connectMs.toFixed(1)),
    pingSent,
    pingReceived,
    pingLossRate: pingSent === 0 ? 0 : Number(((pingSent - pingReceived) / pingSent).toFixed(6)),
    rttP50Ms: Number(percentile(latencies, 0.50).toFixed(2)),
    rttP95Ms: Number(percentile(latencies, 0.95).toFixed(2)),
    rttP99Ms: Number(percentile(latencies, 0.99).toFixed(2)),
    rttMaxMs: Number(Math.max(0, ...latencies).toFixed(2)),
  };
  console.log(JSON.stringify(summary, null, 2));
  if (failed > 0 || closedUnexpectedly > 0 || summary.pingLossRate > 0.01) process.exitCode = 1;
}

main().catch((error) => {
  console.error(`压测失败：${error.message}`);
  process.exitCode = 1;
});
