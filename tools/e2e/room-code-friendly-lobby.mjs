/**
 * 房间码友谊战端到端验证。
 *
 * 前置条件：服务端监听 ws://localhost:8080/ws/。
 * 运行方式：node tools/e2e/room-code-friendly-lobby.mjs
 */

import fs from "node:fs";
import path from "node:path";

const WS_URL = "ws://localhost:8080/ws/";
const TIMEOUT_MS = 7_000;
const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

function buildLegalDeck() {
  const root = process.cwd();
  const dataDirectory = fs.readdirSync(root, { withFileTypes: true })
    .find((entry) => entry.isDirectory() && fs.existsSync(path.join(root, entry.name, "OP15.json")));
  if (!dataDirectory) throw new Error("找不到包含 OP15.json 的卡牌数据目录");

  const cards = JSON.parse(fs.readFileSync(path.join(root, dataDirectory.name, "OP15.json"), "utf8"));
  const leader = cards.find((card) => card.number === "OP15-001");
  if (!leader) throw new Error("找不到 OP15-001 领航");

  const leaderColors = new Set(String(leader.color).split("/"));
  const pool = cards.filter((card) =>
    !["领航", "领袖"].includes(card.type)
    && String(card.color).split("/").some((color) => leaderColors.has(color)));
  const mainDeck = [];
  const counts = new Map();
  let cursor = 0;
  while (mainDeck.length < 50) {
    const card = pool[cursor++ % pool.length];
    const count = counts.get(card.number) ?? 0;
    if (count >= 4) continue;
    mainDeck.push(card.number);
    counts.set(card.number, count + 1);
  }
  return [leader.number, ...mainDeck].join("\n");
}

class TestClient {
  constructor(label) {
    this.label = label;
    this.buffer = [];
    this.waiters = [];
  }

  async open() {
    this.ws = new WebSocket(WS_URL);
    this.ws.addEventListener("message", (event) => {
      const message = JSON.parse(event.data);
      const index = this.waiters.findIndex((waiter) => waiter.predicate(message));
      if (index < 0) {
        this.buffer.push(message);
        return;
      }
      const [waiter] = this.waiters.splice(index, 1);
      clearTimeout(waiter.timer);
      waiter.resolve(message);
    });

    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`${this.label} 连接超时`)), 5_000);
      this.ws.addEventListener("open", () => {
        clearTimeout(timer);
        resolve();
      }, { once: true });
      this.ws.addEventListener("error", () => {
        clearTimeout(timer);
        reject(new Error(`${this.label} 连接失败，请确认本地服务端已启动`));
      }, { once: true });
    });
  }

  send(message) {
    this.ws.send(JSON.stringify(message));
  }

  wait(predicate, timeout = TIMEOUT_MS) {
    const bufferedIndex = this.buffer.findIndex(predicate);
    if (bufferedIndex >= 0) return Promise.resolve(this.buffer.splice(bufferedIndex, 1)[0]);

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        const index = this.waiters.findIndex((waiter) => waiter.timer === timer);
        if (index >= 0) this.waiters.splice(index, 1);
        const bufferedProtocols = this.buffer.map((message) => message.proto).join(", ");
        reject(new Error(`${this.label} 等待消息超时；当前缓存：${bufferedProtocols || "空"}`));
      }, timeout);
      this.waiters.push({ predicate, resolve, timer });
    });
  }

  has(predicate) {
    return this.buffer.some(predicate);
  }

  async close() {
    if (!this.ws || this.ws.readyState >= WebSocket.CLOSING) return;
    const closed = new Promise((resolve) => this.ws.addEventListener("close", resolve, { once: true }));
    this.ws.close();
    await Promise.race([closed, delay(1_500)]);
  }
}

async function login(client, account) {
  await client.open();
  client.send({ proto: "MsgSecret", vesion: "0.998" });
  const secret = await client.wait((message) => message.proto === "MsgSecret");
  if (!secret.result) throw new Error(`${client.label} 握手失败`);

  client.send({ proto: "MsgLogin", account, password: "1" });
  const loginResult = await client.wait((message) => message.proto === "MsgLogin");
  if (!loginResult.result) throw new Error(`${client.label} 登录失败：${loginResult.logStr}`);
}

async function main() {
  const deck = buildLegalDeck();
  const suffix = Date.now().toString(36).slice(-7);
  const hostAccount = `cxh${suffix}`;
  const guestAccount = `cxg${suffix}`;
  let host = new TestClient("房主");
  const guest = new TestClient("加入者");

  try {
    await login(host, hostAccount);
    host.send({ proto: "MsgCreateRoom", deck, deckName: "联调卡组A" });
    const created = await host.wait((message) => message.proto === "MsgCreateRoom");
    if (!created.result || !created.roomCode) throw new Error(`创建失败：${created.logStr}`);

    const initialRoom = await host.wait((message) =>
      message.proto === "MsgFriendlyRoom" && message.origin === "roomCode");
    if (initialRoom.players.length !== 1 || initialRoom.roomCode !== created.roomCode) {
      throw new Error("创建房间码后未进入统一友谊战准备房");
    }

    await host.close();
    await delay(500);
    host = new TestClient("重连房主");
    await login(host, hostAccount);
    const restoredRoom = await host.wait((message) =>
      message.proto === "MsgFriendlyRoom" && message.origin === "roomCode");
    if (restoredRoom.roomId !== initialRoom.roomId || restoredRoom.roomCode !== created.roomCode) {
      throw new Error("房主重连后未恢复原房间码准备房");
    }

    await login(guest, guestAccount);
    guest.send({
      proto: "MsgJoinRoom",
      roomCode: created.roomCode,
      deck,
      deckName: "联调卡组B",
    });
    const joined = await guest.wait((message) => message.proto === "MsgJoinRoom");
    if (!joined.result) throw new Error(`加入失败：${joined.logStr}`);

    const [hostFullRoom, guestFullRoom] = await Promise.all([
      host.wait((message) => message.proto === "MsgFriendlyRoom" && message.players?.length === 2),
      guest.wait((message) => message.proto === "MsgFriendlyRoom" && message.players?.length === 2),
    ]);
    if (hostFullRoom.roomId !== guestFullRoom.roomId || hostFullRoom.roomId !== initialRoom.roomId) {
      throw new Error("双方未进入同一个友谊战准备房");
    }

    await delay(300);
    if (host.has((message) => message.proto === "MsgGameStart")
      || guest.has((message) => message.proto === "MsgGameStart")) {
      throw new Error("双方未准备时提前开战");
    }

    host.send({ proto: "MsgFriendlyReady", ready: true });
    await host.wait((message) => message.proto === "MsgFriendlyRoom"
      && message.players?.some((player) => player.account === hostAccount && player.ready));
    guest.send({ proto: "MsgFriendlyReady", ready: true });

    const gameStarts = await Promise.all([
      host.wait((message) => message.proto === "MsgGameStart", 10_000),
      guest.wait((message) => message.proto === "MsgGameStart", 10_000),
    ]);
    await Promise.all([
      host.wait((message) => message.proto === "MsgGameState", 10_000),
      guest.wait((message) => message.proto === "MsgGameState", 10_000),
    ]);

    console.log(JSON.stringify({
      result: "PASS",
      roomCode: created.roomCode,
      roomId: initialRoom.roomId,
      gameStartReceived: gameStarts.every(Boolean),
      checks: [
        "房间码进入友谊战准备房",
        "房主断线重连恢复原房",
        "双方未准备不提前开战",
        "双方准备后启动同一局对战",
      ],
    }, null, 2));
  } finally {
    await host.close();
    await guest.close();
  }
}

main().catch((error) => {
  console.error(error.stack ?? error);
  process.exitCode = 1;
});
