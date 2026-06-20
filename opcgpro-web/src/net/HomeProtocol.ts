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
  MsgInviteResponse,
  MsgInviteResult,
  MsgFriendlyRoom,
  MsgFriendlySelectDeck,
  MsgFriendlyReady,
  MsgFriendlyLeave,
  MsgFriendlyLeft,
  MsgSpectateRoom,
} from "@/types/net";
import { useNetStore } from "@/store/netStore";
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
      case "MsgEnterBotMatch":
        handleEnterBotMatch(msg as MsgEnterBotMatch);
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

  // 握手成功后（首连/重连/整页刷新）若本地存有账号则自动登录。
  // 后端 OnLogin 会自动 TryReclaim：找回进行中的对局并回发完整快照，从而恢复棋盘。
  eventBus.on("connectSucc", () => {
    if (typeof window === "undefined") return;
    const saved = localStorage.getItem("grandumi_account");
    if (saved) HomeRequest.login(saved);
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
    // 持久化账号，供刷新/重连后自动登录恢复登录态与进行中的对局
    if (typeof window !== "undefined" && account) {
      localStorage.setItem("grandumi_account", account);
    }
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
    showMessage(msg.logStr ?? "加入匹配失败", "error");
    return;
  }
  useNetStore.getState().setMatchState("matching");
}

/**
 * MsgEnterBotMatch — 单人测试开局回包（失败提示；成功后由 MsgMatchFound/MsgGameStart 进入对战）
 */
function handleEnterBotMatch(msg: MsgEnterBotMatch) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "单人测试开局失败", "error");
  }
}

/**
 * MsgCancelMatch — 取消匹配回包
 */
function handleCancelMatch(_msg: MsgCancelMatch) {
  useNetStore.getState().setMatchState("idle");
}

/**
 * MsgMatchFound — 匹配成功
 */
function handleMatchFound(msg: MsgMatchFound) {
  useNetStore.getState().setOpponentName(msg.opponentName);
  useNetStore.getState().setMatchState("matched");
  showMessage(`匹配成功！对手：${msg.opponentName}`, "info");
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
  const { useGameStore } = require("@/store/gameStore");
  const gameStore = useGameStore.getState();
  // 先手信息由服务端 MsgGameState 的 firstPlayer 决定，这里仅切换为对战模式；
  // IsFirst 另存入 sessionStorage 供 /game 页初始化使用
  gameStore.setMode("Player");

  if (typeof window !== "undefined") {
    sessionStorage.setItem("myDeck", msg.MainDeck ?? "");
    sessionStorage.setItem("enemyDeck", msg.EnemyDeck ?? "");
    sessionStorage.setItem("isFirst", msg.IsFirst ? "1" : "0");
    const net = useNetStore.getState();
    sessionStorage.setItem("myName", net.playerName);
    sessionStorage.setItem("opponentName", net.opponentName);
  }

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

/**
 * MsgOnlineCount — 在线人数广播
 * 服务器在有人登录/断开时推送，更新角落徽标
 */
function handleOnlineCount(msg: MsgOnlineCount) {
  useNetStore.getState().setOnlineCount(msg.count ?? 0);
}

/** MsgPlayerList — 在线玩家列表返回 */
function handlePlayerList(msg: MsgPlayerList) {
  useNetStore.getState().setPlayerList(msg.players ?? []);
}

/** MsgInvitePlayer — 发起邀请的回执（给发起方） */
function handleInvitePlayer(msg: MsgInvitePlayer) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "邀请失败", "error");
    return;
  }
  showMessage(`已向 ${msg.toName ?? "对方"} 发送对战邀请，等待回应…`, "info");
}

/** MsgInviteNotify — 收到对战邀请（给被邀请方） */
function handleInviteNotify(msg: MsgInviteNotify) {
  useNetStore.getState().setIncomingInvite({ inviteId: msg.inviteId, fromName: msg.fromName });
}

/** MsgInviteResult — 邀请被拒/失效（接受成功走 MsgFriendlyRoom 进房间，不会到这里） */
function handleInviteResult(msg: MsgInviteResult) {
  if (!msg.accepted) {
    const text = msg.byName ? `${msg.byName} 拒绝了你的对战邀请` : (msg.logStr ?? "邀请未成功");
    showMessage(text, "info");
  }
}

/** MsgFriendlyRoom — 友谊战房间状态更新（进房/选卡组/准备/对局结束回房均经此） */
function handleFriendlyRoom(msg: MsgFriendlyRoom) {
  useNetStore.getState().setFriendlyRoom({
    roomId: msg.roomId,
    players: msg.players,
    scores: msg.scores,
    state: msg.state,
  });
  if (msg.error) showMessage(msg.error, "error");
}

/** MsgFriendlyLeft — 房间已解散/自己已退出 */
function handleFriendlyLeft(msg: MsgFriendlyLeft) {
  useNetStore.getState().setFriendlyRoom(null);
  if (msg.logStr) showMessage(msg.logStr, "info");
}

// ── 请求发送 ────────────────────────────────────────────────────────────
// 对应 C# HomeProtocol.cs 中的各 Request 静态方法

export const HomeRequest = {
  login(account: string) {
    NetManager.send({
      proto: "MsgLogin",
      account,
      password: "",
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
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    return NetManager.send({
      proto: "MsgEnterMatch",
      deck,
    } as MsgEnterMatch);
  },

  enterBotMatch(deck: string, goFirst: boolean = true) {
    // 单人测试：标记本局为机器人对战，对战页据此显示 GM 按钮
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "1");
    return NetManager.send({
      proto: "MsgEnterBotMatch",
      deck,
      goFirst,
    } as MsgEnterBotMatch);
  },

  cancelMatch() {
    NetManager.send({
      proto: "MsgCancelMatch",
    } as MsgCancelMatch);
  },

  createRoom(deck: string) {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    return NetManager.send({
      proto: "MsgCreateRoom",
      deck,
    } as MsgCreateRoom);
  },

  joinRoom(roomCode: string, deck: string) {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
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

  /** 申请观战指定房间（观战者由后端注册，随后每帧收到脱敏快照） */
  spectateRoom(roomId: string) {
    return NetManager.send({ proto: "MsgSpectateRoom", roomId } as MsgSpectateRoom);
  },
};
