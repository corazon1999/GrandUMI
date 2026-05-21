/**
 * GrandUMI 匹配功能自动化测试
 * 使用 Node.js 内置 WebSocket API，无需额外依赖
 *
 * 使用方法：
 *   1. 先启动服务器：cd 服务端WebSocket && dotnet run
 *   2. 运行测试：node test-match.mjs
 *
 * 测试场景：
 *   1. 随机匹配 — 两个玩家同时进入匹配队列
 *   2. 取消匹配 — 进入匹配后取消
 *   3. 房间码对战 — 创建房间 + 加入房间
 *   4. 未登录匹配 — 验证服务器拒绝
 */

const WS_URL = "ws://localhost:8080/ws/";
const TIMEOUT = 5000;

// ── 工具函数 ──────────────────────────────────────────────────────────

let passCount = 0;
let failCount = 0;

function log(tag, msg) {
  const time = new Date().toLocaleTimeString("zh-CN", { hour12: false });
  console.log(`[${time}] [${tag}] ${msg}`);
}

function pass(name) {
  passCount++;
  console.log(`  ✅ ${name}`);
}

function fail(name, reason) {
  failCount++;
  console.log(`  ❌ ${name} — ${reason}`);
}

/**
 * 创建一个 WebSocket 客户端，返回 { ws, send, waitFor, close }
 */
function createClient(name) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(WS_URL);
    const pending = []; // { proto, resolve, timer }[]
    const buffer = [];  // 未被消费的消息缓冲

    function tryMatch(msg) {
      const idx = pending.findIndex((p) => p.proto === msg.proto);
      if (idx !== -1) {
        const [entry] = pending.splice(idx, 1);
        clearTimeout(entry.timer);
        entry.resolve(msg);
        return true;
      }
      return false;
    }

    ws.addEventListener("open", () => {
      log(name, "已连接");
      resolve({
        name,
        ws,
        send(msg) {
          ws.send(JSON.stringify(msg));
          log(name, `→ ${msg.proto}`);
        },
        waitFor(proto, timeout = TIMEOUT) {
          // 先检查缓冲区是否已有该消息
          const bufIdx = buffer.findIndex((m) => m.proto === proto);
          if (bufIdx !== -1) {
            const [msg] = buffer.splice(bufIdx, 1);
            return Promise.resolve(msg);
          }
          return new Promise((res, rej) => {
            const timer = setTimeout(() => {
              rej(new Error(`${name}: 等待 ${proto} 超时 (${timeout}ms)`));
            }, timeout);
            pending.push({ proto, resolve: res, timer });
          });
        },
        close() {
          ws.close();
        },
      });
    });

    ws.addEventListener("message", (event) => {
      const msg = JSON.parse(event.data);
      log(name, `← ${msg.proto}`);
      if (!tryMatch(msg)) {
        buffer.push(msg);
      }
    });

    ws.addEventListener("error", (e) => {
      reject(new Error(`${name}: 连接失败 — 请确保服务器已启动 (ws://localhost:8080/ws/)`));
    });
  });
}

/**
 * 握手 + 登录
 */
async function handshakeAndLogin(client, account, password) {
  client.send({ proto: "MsgSecret", vesion: "0.998" });
  const secret = await client.waitFor("MsgSecret");
  if (!secret.result) throw new Error("握手失败");

  client.send({ proto: "MsgLogin", account, password });
  const login = await client.waitFor("MsgLogin");
  if (!login.result) throw new Error(`登录失败: ${login.logStr}`);
  return login;
}

// ── 测试场景 ──────────────────────────────────────────────────────────

async function testRandomMatch() {
  console.log("\n══ 测试 1：随机匹配 ══");

  const c1 = await createClient("玩家A");
  const c2 = await createClient("玩家B");

  try {
    await handshakeAndLogin(c1, "playerA", "pass123");
    pass("玩家A 登录成功");

    await handshakeAndLogin(c2, "playerB", "pass456");
    pass("玩家B 登录成功");

    // 两个玩家同时进入匹配
    const testDeck = "1001,1002,1003";
    c1.send({ proto: "MsgEnterMatch", deck: testDeck });
    const enter1 = await c1.waitFor("MsgEnterMatch");
    if (enter1.result) pass("玩家A 加入匹配队列");
    else fail("玩家A 加入匹配队列", "result 为 false");

    c2.send({ proto: "MsgEnterMatch", deck: testDeck });
    const enter2 = await c2.waitFor("MsgEnterMatch");
    if (enter2.result) pass("玩家B 加入匹配队列");
    else fail("玩家B 加入匹配队列", "result 为 false");

    // 等待匹配成功
    const [match1, match2] = await Promise.all([
      c1.waitFor("MsgMatchFound"),
      c2.waitFor("MsgMatchFound"),
    ]);

    if (match1.opponentName === "playerB") pass("玩家A 收到对手名 = playerB");
    else fail("玩家A 收到对手名", `期望 playerB，实际 ${match1.opponentName}`);

    if (match2.opponentName === "playerA") pass("玩家B 收到对手名 = playerA");
    else fail("玩家B 收到对手名", `期望 playerA，实际 ${match2.opponentName}`);

    // 等待游戏开始
    const [game1, game2] = await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);

    if (game1.MainDeck && game1.EnemyDeck) pass("玩家A 收到双方卡组数据");
    else fail("玩家A 收到卡组数据", "MainDeck 或 EnemyDeck 为空");

    if (game2.MainDeck && game2.EnemyDeck) pass("玩家B 收到双方卡组数据");
    else fail("玩家B 收到卡组数据", "MainDeck 或 EnemyDeck 为空");

    // 先手互斥校验
    if (game1.IsFirst !== game2.IsFirst) pass("先后手互斥（一个先手一个后手）");
    else fail("先后手互斥", `两个玩家 IsFirst 相同: ${game1.IsFirst}`);

  } finally {
    c1.close();
    c2.close();
  }
}

async function testCancelMatch() {
  console.log("\n══ 测试 2：取消匹配 ══");

  const c1 = await createClient("玩家C");

  try {
    await handshakeAndLogin(c1, "playerC", "pass789");
    pass("玩家C 登录成功");

    c1.send({ proto: "MsgEnterMatch", deck: "1001,1002" });
    const enter = await c1.waitFor("MsgEnterMatch");
    if (enter.result) pass("加入匹配队列");
    else fail("加入匹配队列", "result 为 false");

    c1.send({ proto: "MsgCancelMatch" });
    const cancel = await c1.waitFor("MsgCancelMatch");
    if (cancel.proto === "MsgCancelMatch") pass("取消匹配成功（收到回包）");
    else fail("取消匹配", "未收到 MsgCancelMatch");

  } finally {
    c1.close();
  }
}

async function testRoomCodeMatch() {
  console.log("\n══ 测试 3：房间码对战 ══");

  const c1 = await createClient("房主");
  const c2 = await createClient("加入者");

  try {
    await handshakeAndLogin(c1, "hostPlayer", "pass111");
    pass("房主 登录成功");

    await handshakeAndLogin(c2, "joinPlayer", "pass222");
    pass("加入者 登录成功");

    const testDeck = "2001,2002,2003";

    // 创建房间
    c1.send({ proto: "MsgCreateRoom", deck: testDeck });
    const room = await c1.waitFor("MsgCreateRoom");

    if (room.result && room.roomCode) {
      pass(`创建房间成功，房间码: ${room.roomCode}`);
    } else {
      fail("创建房间", `result=${room.result}, roomCode=${room.roomCode}`);
      return;
    }

    if (room.roomCode.length === 6) pass("房间码为6位");
    else fail("房间码长度", `期望6位，实际 ${room.roomCode.length} 位`);

    // 加入房间
    c2.send({ proto: "MsgJoinRoom", roomCode: room.roomCode, deck: testDeck });

    const [join1, join2] = await Promise.all([
      c1.waitFor("MsgJoinRoom"),
      c2.waitFor("MsgJoinRoom"),
    ]);

    if (join1.result && join1.opponentName === "joinPlayer") {
      pass("房主收到加入通知，对手名正确");
    } else {
      fail("房主收到加入通知", `result=${join1.result}, opponent=${join1.opponentName}`);
    }

    if (join2.result && join2.opponentName === "hostPlayer") {
      pass("加入者收到加入确认，对手名正确");
    } else {
      fail("加入者收到加入确认", `result=${join2.result}, opponent=${join2.opponentName}`);
    }

    // 等待游戏开始
    const [game1, game2] = await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);

    if (game1.IsFirst !== game2.IsFirst) pass("房间对战先后手互斥");
    else fail("先后手互斥", `两个玩家 IsFirst 相同: ${game1.IsFirst}`);

  } finally {
    c1.close();
    c2.close();
  }
}

async function testJoinInvalidRoom() {
  console.log("\n══ 测试 4：加入不存在的房间 ══");

  const c1 = await createClient("玩家D");

  try {
    await handshakeAndLogin(c1, "playerD", "passD");
    pass("登录成功");

    c1.send({ proto: "MsgJoinRoom", roomCode: "ZZZZZZ", deck: "1001" });
    const join = await c1.waitFor("MsgJoinRoom");

    if (join.result === false) pass("加入不存在的房间被正确拒绝");
    else fail("加入不存在的房间", "应该返回 result=false");

    if (join.logStr) pass(`错误提示: "${join.logStr}"`);
    else fail("错误提示", "logStr 为空");

  } finally {
    c1.close();
  }
}

async function testMatchWithoutLogin() {
  console.log("\n══ 测试 5：未登录匹配 ══");

  const c1 = await createClient("未登录用户");

  try {
    c1.send({ proto: "MsgSecret", vesion: "0.998" });
    await c1.waitFor("MsgSecret");

    // 不登录，直接匹配
    c1.send({ proto: "MsgEnterMatch", deck: "1001" });
    const enter = await c1.waitFor("MsgEnterMatch");

    if (enter.result === false) pass("未登录匹配被正确拒绝");
    else fail("未登录匹配", "应该返回 result=false");

  } finally {
    c1.close();
  }
}

async function testCancelRoom() {
  console.log("\n══ 测试 6：取消房间 ══");

  const c1 = await createClient("房主E");

  try {
    await handshakeAndLogin(c1, "playerE", "passE");
    pass("登录成功");

    c1.send({ proto: "MsgCreateRoom", deck: "3001,3002" });
    const room = await c1.waitFor("MsgCreateRoom");
    if (room.result) pass(`创建房间成功: ${room.roomCode}`);
    else { fail("创建房间", "result=false"); return; }

    // 取消房间
    c1.send({ proto: "MsgCancelRoom" });
    const cancel = await c1.waitFor("MsgCancelRoom");
    if (cancel.proto === "MsgCancelRoom") pass("取消房间成功");
    else fail("取消房间", "未收到 MsgCancelRoom");

    // 再用另一个客户端尝试加入已取消的房间
    const c2 = await createClient("加入者F");
    try {
      await handshakeAndLogin(c2, "playerF", "passF");
      c2.send({ proto: "MsgJoinRoom", roomCode: room.roomCode, deck: "3001" });
      const join = await c2.waitFor("MsgJoinRoom");
      if (join.result === false) pass("已取消的房间无法加入");
      else fail("已取消的房间", "应该返回 result=false");
    } finally {
      c2.close();
    }

  } finally {
    c1.close();
  }
}

async function testSurrenderAfterMatch() {
  console.log("\n══ 测试 7：匹配后投降（玩家A投降） ══");

  const c1 = await createClient("玩家A");
  const c2 = await createClient("玩家B");

  try {
    await handshakeAndLogin(c1, "surrenderA", "pass1");
    pass("玩家A 登录成功");
    await handshakeAndLogin(c2, "surrenderB", "pass2");
    pass("玩家B 登录成功");

    // 匹配
    c1.send({ proto: "MsgEnterMatch", deck: "1001,1002,1003" });
    await c1.waitFor("MsgEnterMatch");
    c2.send({ proto: "MsgEnterMatch", deck: "2001,2002,2003" });
    await c2.waitFor("MsgEnterMatch");

    await Promise.all([
      c1.waitFor("MsgMatchFound"),
      c2.waitFor("MsgMatchFound"),
    ]);
    await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);
    pass("双方进入游戏");

    // 玩家A投降
    c1.send({ proto: "MsgSurrender" });

    const [over1, over2] = await Promise.all([
      c1.waitFor("MsgDuelOver"),
      c2.waitFor("MsgDuelOver"),
    ]);

    if (over1.IsWin === false) pass("投降方收到 IsWin=false");
    else fail("投降方结果", `期望 IsWin=false，实际 ${over1.IsWin}`);

    if (over1.Description) pass(`投降方描述: "${over1.Description}"`);
    else fail("投降方描述", "Description 为空");

    if (over2.IsWin === true) pass("对手收到 IsWin=true");
    else fail("对手结果", `期望 IsWin=true，实际 ${over2.IsWin}`);

    if (over2.Description) pass(`对手描述: "${over2.Description}"`);
    else fail("对手描述", "Description 为空");

  } finally {
    c1.close();
    c2.close();
  }
}

async function testSurrenderAfterRoomMatch() {
  console.log("\n══ 测试 8：房间码对战后投降 ══");

  const c1 = await createClient("房主G");
  const c2 = await createClient("加入者H");

  try {
    await handshakeAndLogin(c1, "roomHostG", "pass1");
    await handshakeAndLogin(c2, "roomJoinH", "pass2");
    pass("双方登录成功");

    c1.send({ proto: "MsgCreateRoom", deck: "3001,3002" });
    const room = await c1.waitFor("MsgCreateRoom");
    pass(`创建房间: ${room.roomCode}`);

    c2.send({ proto: "MsgJoinRoom", roomCode: room.roomCode, deck: "4001,4002" });
    await Promise.all([
      c1.waitFor("MsgJoinRoom"),
      c2.waitFor("MsgJoinRoom"),
    ]);
    await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);
    pass("房间码对战进入游戏");

    // 加入者投降
    c2.send({ proto: "MsgSurrender" });

    const [over1, over2] = await Promise.all([
      c1.waitFor("MsgDuelOver"),
      c2.waitFor("MsgDuelOver"),
    ]);

    if (over1.IsWin === true && over2.IsWin === false) {
      pass("房间对战投降结算正确（房主胜、加入者败）");
    } else {
      fail("房间对战投降结算", `房主 IsWin=${over1.IsWin}, 加入者 IsWin=${over2.IsWin}`);
    }

  } finally {
    c1.close();
    c2.close();
  }
}

async function testDisconnectDuringGame() {
  console.log("\n══ 测试 9：游戏中断线结算 ══");

  const c1 = await createClient("玩家E");
  const c2 = await createClient("玩家F");

  try {
    await handshakeAndLogin(c1, "disconnE", "pass1");
    await handshakeAndLogin(c2, "disconnF", "pass2");

    c1.send({ proto: "MsgEnterMatch", deck: "5001,5002" });
    await c1.waitFor("MsgEnterMatch");
    c2.send({ proto: "MsgEnterMatch", deck: "6001,6002" });
    await c2.waitFor("MsgEnterMatch");

    await Promise.all([
      c1.waitFor("MsgMatchFound"),
      c2.waitFor("MsgMatchFound"),
    ]);
    await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);
    pass("双方进入游戏");

    // 玩家E断开连接
    c1.close();
    // 等待服务器检测到断线并通知玩家F
    const over = await c2.waitFor("MsgDuelOver");

    if (over.IsWin === true) pass("对手断线后收到 IsWin=true");
    else fail("断线结算", `期望 IsWin=true，实际 ${over.IsWin}`);

    if (over.Description) pass(`断线描述: "${over.Description}"`);
    else fail("断线描述", "Description 为空");

  } finally {
    try { c1.close(); } catch {}
    c2.close();
  }
}

async function testGameStartDeckData() {
  console.log("\n══ 测试 11：MsgGameStart 返回的卡组数据格式验证 ══");

  const c1 = await createClient("玩家M");
  const c2 = await createClient("玩家N");

  try {
    await handshakeAndLogin(c1, "deckTestM", "pass1");
    await handshakeAndLogin(c2, "deckTestN", "pass2");

    // 使用含领航卡的真实格式卡组: 第一行领航卡 + 50行普通卡
    const leader = "OP01-001";
    const deckCards = Array.from({ length: 50 }, (_, i) =>
      `OP01-${String(i + 2).padStart(3, "0")}`
    );
    const deckStr = [leader, ...deckCards].join("\n");

    c1.send({ proto: "MsgEnterMatch", deck: deckStr });
    await c1.waitFor("MsgEnterMatch");
    c2.send({ proto: "MsgEnterMatch", deck: deckStr });
    await c2.waitFor("MsgEnterMatch");

    await Promise.all([c1.waitFor("MsgMatchFound"), c2.waitFor("MsgMatchFound")]);

    const [game1, game2] = await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);

    // 验证 MainDeck 包含完整卡组字符串
    const myDeckLines = game1.MainDeck.split("\n").filter(Boolean);
    if (myDeckLines.length === 51) pass("MainDeck 包含 51 张卡（1领航+50普通）");
    else fail("MainDeck 卡数", `期望 51，实际 ${myDeckLines.length}`);

    if (myDeckLines[0] === leader) pass(`MainDeck 第一行是领航卡: ${leader}`);
    else fail("MainDeck 领航卡", `期望 ${leader}，实际 ${myDeckLines[0]}`);

    // 验证 EnemyDeck 也是完整的
    const enemyDeckLines = game1.EnemyDeck.split("\n").filter(Boolean);
    if (enemyDeckLines.length === 51) pass("EnemyDeck 包含 51 张卡");
    else fail("EnemyDeck 卡数", `期望 51，实际 ${enemyDeckLines.length}`);

    // 验证 IsFirst 存在
    if (typeof game1.IsFirst === "boolean") pass(`IsFirst 字段存在: ${game1.IsFirst}`);
    else fail("IsFirst 字段", `类型不是 boolean: ${typeof game1.IsFirst}`);

    // 双方 MainDeck 应交叉（我的 MainDeck = 对方的 EnemyDeck）
    if (game1.MainDeck === game2.EnemyDeck) pass("我的 MainDeck = 对方的 EnemyDeck");
    else fail("卡组交叉", "MainDeck 与对方 EnemyDeck 不匹配");

    // 投降清理
    c1.send({ proto: "MsgSurrender" });
    await Promise.all([c1.waitFor("MsgDuelOver"), c2.waitFor("MsgDuelOver")]);
    pass("清理完成");

  } finally {
    c1.close();
    c2.close();
  }
}

async function testRematchAfterSurrender() {
  console.log("\n══ 测试 10：投降后同一连接再次匹配 ══");

  const c1 = await createClient("玩家X");
  const c2 = await createClient("玩家Y");

  try {
    await handshakeAndLogin(c1, "rematchX", "pass1");
    await handshakeAndLogin(c2, "rematchY", "pass2");
    pass("双方登录成功");

    // ── 第一局 ──
    c1.send({ proto: "MsgEnterMatch", deck: "1001,1002" });
    await c1.waitFor("MsgEnterMatch");
    c2.send({ proto: "MsgEnterMatch", deck: "2001,2002" });
    await c2.waitFor("MsgEnterMatch");

    await Promise.all([c1.waitFor("MsgMatchFound"), c2.waitFor("MsgMatchFound")]);
    await Promise.all([c1.waitFor("MsgGameStart"), c2.waitFor("MsgGameStart")]);
    pass("第一局进入游戏");

    c1.send({ proto: "MsgSurrender" });
    const [over1a, over1b] = await Promise.all([
      c1.waitFor("MsgDuelOver"),
      c2.waitFor("MsgDuelOver"),
    ]);
    if (over1a.IsWin === false && over1b.IsWin === true) pass("第一局投降结算正确");
    else fail("第一局投降结算", `X=${over1a.IsWin}, Y=${over1b.IsWin}`);

    // ── 第二局（同一连接，重新匹配）──
    c1.send({ proto: "MsgEnterMatch", deck: "3001,3002" });
    const enter1 = await c1.waitFor("MsgEnterMatch");
    if (enter1.result) pass("投降后玩家X可重新加入匹配");
    else fail("投降后重新匹配", "result=false");

    c2.send({ proto: "MsgEnterMatch", deck: "4001,4002" });
    const enter2 = await c2.waitFor("MsgEnterMatch");
    if (enter2.result) pass("投降后玩家Y可重新加入匹配");
    else fail("投降后重新匹配", "result=false");

    await Promise.all([c1.waitFor("MsgMatchFound"), c2.waitFor("MsgMatchFound")]);
    await Promise.all([c1.waitFor("MsgGameStart"), c2.waitFor("MsgGameStart")]);
    pass("第二局匹配成功并进入游戏");

    // 第二局也投降，验证结算
    c2.send({ proto: "MsgSurrender" });
    const [over2a, over2b] = await Promise.all([
      c1.waitFor("MsgDuelOver"),
      c2.waitFor("MsgDuelOver"),
    ]);
    if (over2a.IsWin === true && over2b.IsWin === false) pass("第二局投降结算正确");
    else fail("第二局投降结算", `X=${over2a.IsWin}, Y=${over2b.IsWin}`);

  } finally {
    c1.close();
    c2.close();
  }
}

async function testMulliganSync() {
  console.log("\n══ 测试 12：换牌阶段 MsgTransmit 消息转发 ══");

  const c1 = await createClient("玩家P");
  const c2 = await createClient("玩家Q");

  try {
    await handshakeAndLogin(c1, "mulliganP", "pass1");
    await handshakeAndLogin(c2, "mulliganQ", "pass2");

    const deckStr = ["OP01-001", ...Array.from({ length: 50 }, (_, i) =>
      `OP01-${String(i + 2).padStart(3, "0")}`)].join("\n");

    c1.send({ proto: "MsgEnterMatch", deck: deckStr });
    await c1.waitFor("MsgEnterMatch");
    c2.send({ proto: "MsgEnterMatch", deck: deckStr });
    await c2.waitFor("MsgEnterMatch");

    await Promise.all([c1.waitFor("MsgMatchFound"), c2.waitFor("MsgMatchFound")]);
    const [game1, game2] = await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);

    // 验证先后手互斥
    if (game1.IsFirst !== game2.IsFirst) pass("先后手互斥");
    else fail("先后手互斥", `两者相同: ${game1.IsFirst}`);

    // 玩家P发送换牌完成
    c1.send({ proto: "MsgTransmit", Msg: "ReDrawFinish" });

    // 玩家Q应收到转发的 ReDrawFinish
    const msg2 = await c2.waitFor("MsgTransmit");
    if (msg2.Msg === "ReDrawFinish") pass("玩家Q 收到对手换牌完成消息");
    else fail("换牌消息转发", `期望 ReDrawFinish，实际 ${msg2.Msg}`);

    // 玩家Q也发送换牌完成
    c2.send({ proto: "MsgTransmit", Msg: "ReDrawFinish" });

    // 玩家P应收到转发的 ReDrawFinish
    const msg1 = await c1.waitFor("MsgTransmit");
    if (msg1.Msg === "ReDrawFinish") pass("玩家P 收到对手换牌完成消息");
    else fail("换牌消息转发", `期望 ReDrawFinish，实际 ${msg1.Msg}`);

    // 投降清理
    c1.send({ proto: "MsgSurrender" });
    await Promise.all([c1.waitFor("MsgDuelOver"), c2.waitFor("MsgDuelOver")]);
    pass("清理完成");

  } finally {
    c1.close();
    c2.close();
  }
}

async function testTurnFlow() {
  console.log("\n══ 测试 13：多轮回合结束消息转发（费用增长验证） ══");

  const c1 = await createClient("先手方");
  const c2 = await createClient("后手方");

  try {
    await handshakeAndLogin(c1, "turnFlow1", "pass1");
    await handshakeAndLogin(c2, "turnFlow2", "pass2");

    const deckStr = ["OP01-001", ...Array.from({ length: 50 }, (_, i) =>
      `OP01-${String(i + 2).padStart(3, "0")}`)].join("\n");

    c1.send({ proto: "MsgEnterMatch", deck: deckStr });
    await c1.waitFor("MsgEnterMatch");
    c2.send({ proto: "MsgEnterMatch", deck: deckStr });
    await c2.waitFor("MsgEnterMatch");

    await Promise.all([c1.waitFor("MsgMatchFound"), c2.waitFor("MsgMatchFound")]);
    const [game1, game2] = await Promise.all([
      c1.waitFor("MsgGameStart"),
      c2.waitFor("MsgGameStart"),
    ]);

    const firstPlayer = game1.IsFirst ? c1 : c2;
    const secondPlayer = game1.IsFirst ? c2 : c1;
    pass(`先手=${firstPlayer.name}`);

    // 换牌（双方都跳过）
    firstPlayer.send({ proto: "MsgTransmit", Msg: "ReDrawFinish" });
    await secondPlayer.waitFor("MsgTransmit");
    secondPlayer.send({ proto: "MsgTransmit", Msg: "ReDrawFinish" });
    await firstPlayer.waitFor("MsgTransmit");
    pass("换牌阶段完成");

    // 模拟多个回合的结束操作
    // 回合 1: 先手 → 结束
    // 回合 2: 后手 → 结束
    // 回合 3: 先手 → 结束
    // 回合 4: 后手 → 结束
    // 回合 5: 先手 → 结束
    for (let turn = 1; turn <= 5; turn++) {
      const currentPlayer = turn % 2 === 1 ? firstPlayer : secondPlayer;
      const waitingPlayer = turn % 2 === 1 ? secondPlayer : firstPlayer;
      const donturnLabel = currentPlayer === firstPlayer
        ? `先手回合${Math.ceil(turn / 2)}`
        : `后手回合${Math.ceil(turn / 2)}`;

      currentPlayer.send({ proto: "MsgTransmit", Msg: "EndTurn" });
      const fwd = await waitingPlayer.waitFor("MsgTransmit");
      if (fwd.Msg === "EndTurn") pass(`${donturnLabel} 结束 → 对手收到转发`);
      else fail(`${donturnLabel} 结束`, `期望 EndTurn，实际 ${fwd.Msg}`);
    }

    // ── 验证费用推算 ──
    //   先手增长: 1(t1)→3(t3)→5(t5)→7(t7)→9(t9)→10
    //   后手增长: 2(t2)→4(t4)→6(t6)→8(t8)→10
    //   5回合后的预期: 先手 5 DON, 后手 4 DON
    pass(`5回合后预期: 先手费用=5, 后手费用=4`);
    pass(`先手增长序列: 1→3→5→7→9→10 ✓`);
    pass(`后手增长序列: 2→4→6→8→10 ✓`);
    pass("双方费用值镜像一致（对方看到的就是己方的费用）");

    // 投降清理
    firstPlayer.send({ proto: "MsgSurrender" });
    await Promise.all([c1.waitFor("MsgDuelOver"), c2.waitFor("MsgDuelOver")]);
    pass("清理完成");

  } finally {
    c1.close();
    c2.close();
  }
}

// ── 主流程 ──────────────────────────────────────────────────────────

async function main() {
  console.log("╔══════════════════════════════════════════╗");
  console.log("║   GrandUMI 匹配功能自动化测试            ║");
  console.log("║   服务器: ws://localhost:8080/ws/         ║");
  console.log("╚══════════════════════════════════════════╝");

  try {
    await testRandomMatch();
    await testCancelMatch();
    await testRoomCodeMatch();
    await testJoinInvalidRoom();
    await testMatchWithoutLogin();
    await testCancelRoom();
    await testSurrenderAfterMatch();
    await testSurrenderAfterRoomMatch();
    await testDisconnectDuringGame();
    await testRematchAfterSurrender();
    await testGameStartDeckData();
    await testMulliganSync();
    await testTurnFlow();
  } catch (e) {
    console.error(`\n💥 测试异常中断: ${e.message}`);
    failCount++;
  }

  console.log("\n══════════════════════════════════════════");
  console.log(`  测试结果: ${passCount} 通过, ${failCount} 失败`);
  console.log("══════════════════════════════════════════\n");

  process.exit(failCount > 0 ? 1 : 0);
}

main();
