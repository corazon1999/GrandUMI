/**
 * HomeProtocol.ts
 * 对应 C# HomeProtocol.cs（ProtocolManager 的大厅部分）
 *
 * 职责：
 *   - registerHomeProtocols() 注册所有大厅协议处理器（仅调用一次）
 *   - HomeRequest 对象暴露所有向服务器发送消息的方法
 *
 * 协议字段名与 C# LobbyMsg.cs [ProtoMember] 完全一致
 */

import { NetManager } from "./NetManager";
import { eventBus } from "./eventBus";
import type {
  MsgBase,
  MsgSecret,
  MsgLogin,
  MsgAddAccount,
  MsgUpdatePs,
  MsgEnterMatch,
  MsgEnterBotMatch,
  MsgCancelMatch,
  MsgMatchFound,
  MsgCreateRoom,
  MsgJoinRoom,
  MsgCancelRoom,
  MsgGameStart,
  MsgChatMsg,
  MsgOnlineCount,
  MsgPlayerList,
  MsgInvitePlayer,
  MsgInviteNotify,
  MsgInviteResult,
  MsgFriendlyRoom,
  MsgFriendlyLeft,
  MsgFriendlySelectDeck,
  MsgFriendlyReady,
  MsgFriendlyLeave,
  MsgInviteResponse,
} from "@/types/net";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import { showMessage } from "@/components/ui/MessageBox";

// ── 协议注册 ────────────────────────────────────────────────────────────

let registered = false;

/**
 * 注册所有大厅协议处理器
 * 对应 C# AddRoomListener() 中的所有 AddProtoListener 调用
 * 应在应用初始化时调用一次
 */
export function registerHomeProtocols() {
  if (registered) return;
  registered = true;

  eventBus.on("message", (msg: MsgBase) => {
    switch (msg.proto) {
      case "MsgSecret":
        handleSecret(msg as MsgSecret);
        break;
      case "MsgLogin":
        handleLogin(msg as MsgLogin);
        break;
      case "MsgAddAccount":
        handleAddAccount(msg as MsgAddAccount);
        break;
      case "MsgUpdatePs":
        handleUpdatePs(msg as MsgUpdatePs);
        break;
      case "MsgEnterMatch":
        handleEnterMatch(msg as MsgEnterMatch);
        break;
      case "MsgCancelMatch":
        handleCancelMatch(msg as MsgCancelMatch);
        break;
      case "MsgMatchFound":
        handleMatchFound(msg as MsgMatchFound);
        break;
      case "MsgCreateRoom":
        handleCreateRoom(msg as MsgCreateRoom);
        break;
      case "MsgJoinRoom":
        handleJoinRoom(msg as MsgJoinRoom);
        break;
      case "MsgCancelRoom":
        handleCancelRoom(msg as MsgCancelRoom);
        break;
      case "MsgGameStart":
        handleGameStart(msg as MsgGameStart);
        break;
      case "MsgChatMsg":
        handleChatMsg(msg as MsgChatMsg);
        break;
      case "MsgEnterBotMatch":
        handleEnterBotMatch(msg as MsgEnterBotMatch);
        break;
      case "MsgOnlineCount":
        handleOnlineCount(msg as MsgOnlineCount);
        break;
      case "MsgPlayerList":
        handlePlayerList(msg as MsgPlayerList);
        break;
      case "MsgInvitePlayer":
        handleInvitePlayer(msg as MsgInvitePlayer);
        break;
      case "MsgInviteNotify":
        handleInviteNotify(msg as MsgInviteNotify);
        break;
      case "MsgInviteResult":
        handleInviteResult(msg as MsgInviteResult);
        break;
      case "MsgFriendlyRoom":
        handleFriendlyRoom(msg as MsgFriendlyRoom);
        break;
      case "MsgFriendlyLeft":
        handleFriendlyLeft(msg as MsgFriendlyLeft);
        break;
    }
  });
}

// ── 处理器实现 ──────────────────────────────────────────────────────────

/**
 * MsgSecret — 握手回包
 * C#: SecretRequest() 的回调
 * 服务器返回版本校验结果和 AES 密钥（WebSocket 版本不需要密钥，但需校验版本）
 */
function handleSecret(msg: MsgSecret) {
  if (msg.result === false) {
    showMessage("版本不一致，请下载最新版本", "error");
    useNetStore.getState().setError("版本不一致，请下载最新版本");
    return;
  }
  // 版本匹配，握手成功，UI 可以展示登录界面
  useNetStore.getState().setError(null);
}

/**
 * MsgLogin — 登录回包
 * C#: LoginPanel.LoginCallBack(msg.result, msg.name)
 */
function handleLogin(msg: MsgLogin) {
  const store = useNetStore.getState();
  if (msg.result === true) {
    const account = msg.account ?? "";
    // 优先用本地存档昵称，其次服务器返回的 name，最后 fallback 到 account
    const saved = typeof window !== "undefined"
      ? (localStorage.getItem(`grandumi_nick_${account}`) ?? "")
      : "";
    const displayName = saved || msg.name || account;
    store.setLoggedIn(true, displayName, account);
    store.setError(null);
    store.setNavigateTo("/home");
    if (msg.logStr) showMessage(msg.logStr, "info");
  } else {
    store.setError(msg.logStr ?? "账号或密码错误");
    if (msg.logStr) showMessage(msg.logStr, "error");
  }
}

/**
 * MsgAddAccount — 注册回包（目前服务器无回包，仅预留）
 */
function handleAddAccount(_msg: MsgAddAccount) {}

/**
 * MsgUpdatePs — 修改密码回包
 * C#: PlayerPanel.OnUpdatePsCallBack()
 */
function handleUpdatePs(msg: MsgUpdatePs) {
  if (msg.result) {
    showMessage(msg.logStr ?? "密码修改成功", "info");
  } else {
    showMessage(msg.logStr ?? "密码修改失败", "error");
  }
}

/**
 * MsgEnterMatch — 匹配加入回包
 */
function handleEnterMatch(msg: MsgEnterMatch) {
  if (msg.result === false) {
    showMessage("加入匹配失败", "error");
    return;
  }
  useNetStore.getState().setMatchState("matching");
}

/**
 * MsgCancelMatch — 取消匹配回包
 */
function handleCancelMatch(_msg: MsgCancelMatch) {
  // 取消匹配（含 bot 取消）：清理 bot 标记并回到 idle
  if (typeof window !== "undefined") sessionStorage.removeItem("isBotMatch");
  useNetStore.getState().setMatchState("idle");
}

/**
 * MsgMatchFound — 匹配成功
 */
function handleMatchFound(msg: MsgMatchFound) {
  const net = useNetStore.getState();
  // bot 模式：服务器可能也会回 MsgMatchFound，但我们要保留 bot-matching 状态卡直到 MsgGameStart 跳转
  const isBot = typeof window !== "undefined" && sessionStorage.getItem("isBotMatch") === "1";
  net.setOpponentName(isBot ? "机器人对手" : msg.opponentName);
  if (!isBot) {
    net.setMatchState("matched");
    showMessage(`匹配成功！对手：${msg.opponentName}`, "info");
  }
}

/**
 * MsgCreateRoom — 创建房间回包
 * 服务器返回房间码
 */
function handleCreateRoom(msg: MsgCreateRoom) {
  if (msg.result === false) {
    showMessage("创建房间失败", "error");
    return;
  }
  useNetStore.getState().setRoomCode(msg.roomCode ?? null);
  showMessage("房间创建成功，等待对手加入", "info");
}

/**
 * MsgJoinRoom — 加入房间回包
 */
function handleJoinRoom(msg: MsgJoinRoom) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "加入房间失败", "error");
    return;
  }
  useNetStore.getState().setOpponentName(msg.opponentName ?? "");
  useNetStore.getState().setRoomCode(null);
  showMessage(`加入成功！对手：${msg.opponentName}`, "info");
}

/**
 * MsgCancelRoom — 取消房间回包
 */
function handleCancelRoom(_msg: MsgCancelRoom) {
  useNetStore.getState().setRoomCode(null);
}

/**
 * MsgGameStart — 游戏开始
 * 跳转到游戏场景
 */
function handleGameStart(msg: MsgGameStart) {
  const gameStore = useGameStore.getState();
  gameStore.setIsStart(msg.IsFirst ?? false);
  gameStore.setMode("Player");

  if (typeof window !== "undefined") {
    sessionStorage.setItem("myDeck", msg.MainDeck ?? "");
    sessionStorage.setItem("enemyDeck", msg.EnemyDeck ?? "");
    sessionStorage.setItem("isFirst", msg.IsFirst ? "1" : "0");
    const net = useNetStore.getState();
    sessionStorage.setItem("myName", net.playerName);
    // bot 模式兜底：服务器若没在 GameStart 前告知对手名，使用机器人对手
    sessionStorage.setItem("opponentName", net.opponentName || "机器人对手");
  }

  // 进入游戏后清掉 isBotMatch 标记，避免污染下次普通匹配
  if (typeof window !== "undefined") sessionStorage.removeItem("isBotMatch");
  useNetStore.getState().setNavigateTo("/game");
}

/**
 * MsgChatMsg — 聊天消息
 */
function handleChatMsg(msg: MsgChatMsg) {
  useNetStore.getState().addChatMessage({
    Name: msg.Name ?? "未知",
    Msg: msg.Msg ?? "",
    type: msg.type ?? 0,
    time: Date.now(),
  });
}

function handleEnterBotMatch(msg: MsgEnterBotMatch) {
  if (msg.result === false) {
    // 失败时把 matchState 回退到 idle，让 LobbyPanel 重新显示按钮
    useNetStore.getState().setMatchState("idle");
    showMessage(msg.logStr ?? "单人测试开局失败", "error");
    return;
  }
  // 成功进入 bot 匹配队列 — 设置独立状态，避免与普通 matching 混淆
  // （服务器后续可能仍回 MsgMatchFound，但 handleMatchFound 会识别 isBotMatch 并保留此状态）
  useNetStore.getState().setOpponentName("机器人对手");
  useNetStore.getState().setMatchState("bot-matching");
}

function handleOnlineCount(msg: MsgOnlineCount) {
  useNetStore.getState().setOnlineCount(msg.count ?? 0);
}

function handlePlayerList(msg: MsgPlayerList) {
  useNetStore.getState().setPlayerList(msg.players ?? []);
}

function handleInvitePlayer(msg: MsgInvitePlayer) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "邀请失败", "error");
    return;
  }
  showMessage(`已向 ${msg.toName ?? "对方"} 发送对战邀请，等待回应…`, "info");
}

function handleInviteNotify(msg: MsgInviteNotify) {
  useNetStore.getState().setIncomingInvite({ inviteId: msg.inviteId, fromName: msg.fromName });
}

function handleInviteResult(msg: MsgInviteResult) {
  if (!msg.accepted) {
    const text = msg.byName ? `${msg.byName} 拒绝了你的对战邀请` : (msg.logStr ?? "邀请未成功");
    showMessage(text, "info");
  }
}

function handleFriendlyRoom(msg: MsgFriendlyRoom) {
  useNetStore.getState().setFriendlyRoom({
    roomId: msg.roomId,
    players: msg.players,
    scores: msg.scores,
    state: msg.state,
  });
  if (msg.error) showMessage(msg.error, "error");
}

function handleFriendlyLeft(msg: MsgFriendlyLeft) {
  useNetStore.getState().setFriendlyRoom(null);
  if (msg.logStr) showMessage(msg.logStr, "info");
}

// ── 请求发送 ────────────────────────────────────────────────────────────
// 对应 C# HomeProtocol.cs 中的各 Request 静态方法

export const HomeRequest = {
  login(account: string, password: string) {
    NetManager.send({
      proto: "MsgLogin",
      account,
      password,
    } as MsgLogin);
  },

  addAccount(id: string, password: string, name: string) {
    NetManager.send({
      proto: "MsgAddAccount",
      id,
      password,
      name,
    } as MsgAddAccount);
  },

  updatePassword(newPs: string) {
    NetManager.send({
      proto: "MsgUpdatePs",
      newPs,
    } as MsgUpdatePs);
  },

  enterMatch(deck: string) {
    NetManager.send({
      proto: "MsgEnterMatch",
      deck,
    } as MsgEnterMatch);
  },

  cancelMatch() {
    NetManager.send({
      proto: "MsgCancelMatch",
    } as MsgCancelMatch);
  },

  createRoom(deck: string) {
    return NetManager.send({
      proto: "MsgCreateRoom",
      deck,
    } as MsgCreateRoom);
  },

  joinRoom(roomCode: string, deck: string) {
    NetManager.send({
      proto: "MsgJoinRoom",
      roomCode,
      deck,
    } as MsgJoinRoom);
  },

  cancelRoom() {
    NetManager.send({
      proto: "MsgCancelRoom",
    } as MsgCancelRoom);
  },

  sendChat(content: string, playerName: string) {
    NetManager.send({
      proto: "MsgChatMsg",
      type: 0,
      Name: playerName,
      Msg: content,
    } as MsgChatMsg);
  },

  enterBotMatch(deck: string, goFirst: boolean = true) {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "1");
    return NetManager.send({
      proto: "MsgEnterBotMatch",
      deck,
      goFirst,
    } as MsgEnterBotMatch);
  },

  requestPlayerList() {
    return NetManager.send({ proto: "MsgPlayerList" } as MsgPlayerList);
  },

  invitePlayer(toAccount: string) {
    return NetManager.send({ proto: "MsgInvitePlayer", toAccount } as MsgInvitePlayer);
  },

  respondInvite(inviteId: string, accept: boolean) {
    if (accept && typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    return NetManager.send({ proto: "MsgInviteResponse", inviteId, accept } as MsgInviteResponse);
  },

  friendlySelectDeck(deck: string, deckName: string) {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    return NetManager.send({ proto: "MsgFriendlySelectDeck", deck, deckName } as MsgFriendlySelectDeck);
  },

  friendlyReady(ready: boolean) {
    return NetManager.send({ proto: "MsgFriendlyReady", ready } as MsgFriendlyReady);
  },

  friendlyLeave() {
    return NetManager.send({ proto: "MsgFriendlyLeave" } as MsgFriendlyLeave);
  },

  spectateRoom(roomId: string) {
    return NetManager.send({ proto: "MsgSpectateRoom", roomId } as import("@/types/net").MsgSpectateRoom);
  },
};
