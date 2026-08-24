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
import {
  clearSessionReplacedNotice,
  getClientInstanceId,
  getSessionReplacedNotice,
  rememberSessionReplacedNotice,
} from "./sessionReplacement";
import type {
  MsgBase,
  MsgSecret,
  MsgLogin,
  MsgPlayerData,
  MsgSaveDeck,
  MsgDeleteDeck,
  MsgSelectDeck,
  MsgUpdateProfile,
  MsgUpdateCardBack,
  MsgCardBackGallery,
  MsgUploadCardBack,
  MsgLikeCardBack,
  MsgDeleteCardBack,
  MsgCardBackReviewQueue,
  MsgReviewCardBack,
  MsgImportDecks,
  MsgDeckPlazaList,
  MsgPublishDeckPlaza,
  MsgLikeDeckPlaza,
  MsgCopyDeckPlaza,
  MsgDeleteDeckPlaza,
  MsgAddAccount,
  MsgUpdatePs,
  MsgEnterMatch,
  MsgEnterBotMatch,
  MsgCancelMatch,
  MsgSelectRankFaction,
  RankFaction,
  RankedMode,
  MatchQueueKind,
  MsgMatchFound,
  MsgRankSnapshot,
  MsgRankResult,
  MsgCreateRoom,
  MsgJoinRoom,
  MsgCancelRoom,
  MsgGameStart,
  MsgChatMsg,
  MsgGlobalAnnouncement,
  MsgOnlineCount,
  MsgPlayerList,
  MsgFriendList,
  MsgFriendSearch,
  MsgFriendRequest,
  MsgFriendRespond,
  MsgFriendRemove,
  MsgFriendCancel,
  MsgPlayerSafety,
  MsgLeaderLeaderboard,
  MsgLeaderMatchupMatrix,
  MsgLeaderMatchups,
  MsgPlayerProfileStats,
  LeaderboardPeriod,
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
  MsgLeaveSpectate,
  MsgUpdateSpectateSettings,
  SpectateMode,
  MsgRulesetState,
  MsgActivateRuleset,
  MsgAdminOperations,
  MsgAdminDeploy,
  AdminDeploymentEnvironment,
} from "@/types/net";
import type { SavedDeck } from "@/types/deck";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import { showMessage } from "@/components/ui/MessageBox";
import {
  loadLegacyDecksForMigration,
  markLegacyDecksMigrated,
  replaceAllDecks,
  setDeckStorageAccount,
  setSelectedDeckName,
} from "@/data/DeckMapper";

// ── 协议注册 ────────────────────────────────────────────────────────────

let registered = false;
let spectateRequestTimer: ReturnType<typeof setTimeout> | null = null;
let roomRequestTimer: ReturnType<typeof setTimeout> | null = null;
let pendingLegacyImport: { account: string; selectedDeckName: string | null } | null = null;
const GAME_REFRESH_RESUME_KEY = "grandumi_resume_game_after_refresh";
const AUTH_ACCOUNT_KEY = "grandumi_auth_account";
const AUTH_TOKEN_KEY = "grandumi_auth_token";

interface MsgMaintenanceState extends MsgBase {
  proto: "MsgMaintenanceState";
  enabled: boolean;
  activeRoomCount: number;
  startedAt?: number | null;
  canManage?: boolean;
  result?: boolean;
  logStr?: string;
}

interface MsgSetMaintenance extends MsgBase {
  proto: "MsgSetMaintenance";
  enabled: boolean;
}

function readAuthToken(account: string): string | undefined {
  if (typeof window === "undefined") return undefined;
  if (sessionStorage.getItem(AUTH_ACCOUNT_KEY)?.toLocaleLowerCase("zh-CN") !== account.toLocaleLowerCase("zh-CN")) {
    return undefined;
  }
  return sessionStorage.getItem(AUTH_TOKEN_KEY) || undefined;
}

function saveAuthToken(account: string, token?: string) {
  if (typeof window === "undefined" || !token) return;
  sessionStorage.setItem(AUTH_ACCOUNT_KEY, account);
  sessionStorage.setItem(AUTH_TOKEN_KEY, token);
}

function clearAuthToken(account: string) {
  if (typeof window === "undefined") return;
  if (sessionStorage.getItem(AUTH_ACCOUNT_KEY)?.toLocaleLowerCase("zh-CN") !== account.toLocaleLowerCase("zh-CN")) return;
  sessionStorage.removeItem(AUTH_ACCOUNT_KEY);
  sessionStorage.removeItem(AUTH_TOKEN_KEY);
}

function clearSpectateRequestTimer() {
  if (spectateRequestTimer) clearTimeout(spectateRequestTimer);
  spectateRequestTimer = null;
}

function clearRoomRequestTimer() {
  if (roomRequestTimer) clearTimeout(roomRequestTimer);
  roomRequestTimer = null;
}

function armRoomRequestTimer() {
  clearRoomRequestTimer();
  roomRequestTimer = setTimeout(() => {
    roomRequestTimer = null;
    const store = useNetStore.getState();
    if (store.roomOperation === "idle") return;
    store.setRoomOperation("idle");
    showMessage("房间请求超时，请检查连接后重试", "error");
  }, 8_000);
}

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
      case "MsgPlayerData":
        handlePlayerData(msg as MsgPlayerData);
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
      case "MsgSelectRankFaction":
        handleSelectRankFaction(msg as MsgSelectRankFaction);
        break;
      case "MsgMatchFound":
        handleMatchFound(msg as MsgMatchFound);
        break;
      case "MsgRankSnapshot":
        handleRankSnapshot(msg as MsgRankSnapshot);
        break;
      case "MsgRankResult":
        handleRankResult(msg as MsgRankResult);
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
      case "MsgGlobalAnnouncement":
        handleGlobalAnnouncement(msg as MsgGlobalAnnouncement);
        break;
      case "MsgMaintenanceState":
        handleMaintenanceState(msg as MsgMaintenanceState);
        break;
      case "MsgRulesetState":
        handleRulesetState(msg as MsgRulesetState);
        break;
      case "MsgAdminOperations":
        handleAdminOperations(msg as MsgAdminOperations);
        break;
      case "MsgOnlineCount":
        handleOnlineCount(msg as MsgOnlineCount);
        break;
      case "MsgPlayerList":
        handlePlayerList(msg as MsgPlayerList);
        break;
      case "MsgFriendList":
        handleFriendList(msg as MsgFriendList);
        break;
      case "MsgFriendSearch":
        handleFriendSearch(msg as MsgFriendSearch);
        break;
      case "MsgFriendRequest":
        handleFriendMutation(msg as MsgFriendRequest);
        break;
      case "MsgFriendRespond":
        handleFriendMutation(msg as MsgFriendRespond);
        break;
      case "MsgFriendRemove":
        handleFriendMutation(msg as MsgFriendRemove);
        break;
      case "MsgFriendCancel":
        handleFriendMutation(msg as MsgFriendCancel);
        break;
      case "MsgPlayerSafety":
        handlePlayerSafety(msg as MsgPlayerSafety);
        break;
      case "MsgLeaderLeaderboard":
        handleLeaderLeaderboard(msg as MsgLeaderLeaderboard);
        break;
      case "MsgLeaderMatchups":
        handleLeaderMatchups(msg as MsgLeaderMatchups);
        break;
      case "MsgLeaderMatchupMatrix":
        handleLeaderMatchupMatrix(msg as MsgLeaderMatchupMatrix);
        break;
      case "MsgPlayerProfileStats":
        handlePlayerProfileStats(msg as MsgPlayerProfileStats);
        break;
      case "MsgCardBackGallery":
        handleCardBackGallery(msg as MsgCardBackGallery);
        break;
      case "MsgLikeCardBack":
        handleCardBackLike(msg as MsgLikeCardBack);
        break;
      case "MsgCardBackReviewQueue":
        handleCardBackReviewQueue(msg as MsgCardBackReviewQueue);
        break;
      case "MsgDeckPlazaList":
        handleDeckPlazaList(msg as MsgDeckPlazaList);
        break;
      case "MsgPublishDeckPlaza":
      case "MsgLikeDeckPlaza":
      case "MsgCopyDeckPlaza":
      case "MsgDeleteDeckPlaza":
        handleDeckPlazaMutation(msg as MsgPublishDeckPlaza | MsgLikeDeckPlaza | MsgCopyDeckPlaza | MsgDeleteDeckPlaza);
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
      case "MsgSpectateRoom":
        handleSpectateRoom(msg as MsgSpectateRoom);
        break;
      case "MsgLeaveSpectate":
        handleLeaveSpectate(msg as MsgLeaveSpectate);
        break;
      case "MsgUpdateSpectateSettings":
        handleUpdateSpectateSettings(msg as MsgUpdateSpectateSettings);
        break;
    }
  });

  // 对局页整页刷新会以全新 SessionId 首次握手；只消费一次性恢复标记，
  // 避免普通首页访问静默登录，同时让服务端 TryReclaim 能自动找回原房间。
  eventBus.on("connectSucc", () => {
    if (typeof window === "undefined") return;
    if (sessionStorage.getItem(GAME_REFRESH_RESUME_KEY) !== "1") return;
    const savedAccount = localStorage.getItem("grandumi_account")?.trim();
    if (savedAccount) HomeRequest.login(savedAccount, undefined, true);
    else sessionStorage.removeItem(GAME_REFRESH_RESUME_KEY);
  });

  // 已登录会话发生普通网络断线时继续沿用当前账号自动恢复。
  eventBus.on("reconnected", () => {
    const { loggedIn, account } = useNetStore.getState();
    if (loggedIn && account) HomeRequest.login(account, undefined, true);
    else NetManager.finishRecovery();
  });

  eventBus.on("sessionReplaced", ({ reason }) => {
    const notice = rememberSessionReplacedNotice(reason);
    if (typeof window !== "undefined") {
      sessionStorage.removeItem(GAME_REFRESH_RESUME_KEY);
    }
    clearRoomRequestTimer();
    clearSpectateRequestTimer();
    pendingLegacyImport = null;
    useGameStore.getState().resetGame();
    const store = useNetStore.getState();
    store.reset();
    store.setError(notice);
    if (typeof window !== "undefined" && window.location.pathname !== "/home") {
      store.setNavigateTo("/home");
    }
    showMessage(notice, "error");
  });

  eventBus.on("close", () => {
    const store = useNetStore.getState();
    const { spectateState, roomOperation } = store;
    if (roomOperation !== "idle") {
      clearRoomRequestTimer();
      store.setRoomOperation("idle");
      showMessage("网络已断开，房间请求尚未完成，请重连后重试", "error");
    }
    if (spectateState === "idle") return;

    clearSpectateRequestTimer();
    useNetStore.getState().setSpectate("idle");
    useGameStore.getState().resetGame();
    useGameStore.getState().setMode("Player");
    if (spectateState === "joining") {
      showMessage("网络已断开，无法进入观战", "error");
    } else {
      showMessage("观战连接已断开，已返回大厅", "error");
      useNetStore.getState().setNavigateTo("/home");
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
  if (!getSessionReplacedNotice()) useNetStore.getState().setError(null);
}

/**
 * MsgLogin — 登录回包
 * C#: LoginPanel.LoginCallBack(msg.result, msg.name)
 */
function handleLogin(msg: MsgLogin) {
  const store = useNetStore.getState();
  // 收到明确登录结果后才消费标记；若握手后、回包前再次断线，下一次连接仍可继续恢复。
  if (typeof window !== "undefined") sessionStorage.removeItem(GAME_REFRESH_RESUME_KEY);
  if (msg.result === true) {
    const account = msg.account ?? "";
    const displayName = msg.name || account;
    const legacyDecks = loadLegacyDecksForMigration(account);
    const legacySelectedDeck = typeof window !== "undefined"
      ? localStorage.getItem("grandumi_selected_deck")
      : null;

    applyPlayerData(
      account,
      displayName,
      msg.avatar ?? "",
      msg.cardBackId ?? "classic",
      msg.canChangeDisplayName ?? false,
      msg.selectedDeckName ?? null,
      msg.decks ?? [],
    );
    store.setLoggedIn(true, displayName, account);
    store.setError(null);
    clearSessionReplacedNotice();
    NetManager.finishRecovery();
    // 持久化账号用于登录页预填，以及对局页刷新时的一次性自动恢复。
    if (typeof window !== "undefined" && account) {
      localStorage.setItem("grandumi_account", account);
    }
    saveAuthToken(account, msg.authToken);
    if (legacyDecks.length > 0) {
      pendingLegacyImport = { account, selectedDeckName: legacySelectedDeck };
      if (!HomeRequest.importDecks(legacyDecks)) pendingLegacyImport = null;
    } else {
      markLegacyDecksMigrated(account);
    }
    if (msg.logStr) showMessage(msg.logStr, "info");
  } else {
    NetManager.finishRecovery();
    if (msg.needsPassword) {
      clearAuthToken(msg.account ?? "");
      store.setLoggedIn(false);
      if (typeof window !== "undefined" && window.location.pathname === "/game") {
        store.setNavigateTo("/home");
      }
    }
    if (msg.authChallenge) {
      store.setError(null);
    } else {
      store.setError(msg.logStr ?? "账号或密码错误");
      if (msg.logStr) showMessage(msg.logStr, "error");
    }
  }
}

function applyPlayerData(
  account: string,
  displayName: string,
  avatar: string,
  cardBackId: string,
  canChangeDisplayName: boolean,
  selectedDeckName: string | null,
  decks: SavedDeck[],
) {
  setDeckStorageAccount(account);
  replaceAllDecks(decks);
  setSelectedDeckName(selectedDeckName);

  const store = useNetStore.getState();
  store.setProfile(displayName, avatar, cardBackId, canChangeDisplayName);
  const selected = selectedDeckName
    ? decks.find((deck) => deck.name === selectedDeckName)
    : undefined;
  store.setSelectedDeck(selected ? {
    name: selected.name,
    leader: selected.leader,
    leaderName: selected.leaderName,
    leaderSprite: selected.leaderSprite,
    cards: [selected.leader, ...selected.cards].join("\n"),
  } : null);

  if (typeof window !== "undefined" && selected?.spriteMap) {
    sessionStorage.setItem("grandumi_spriteMap", JSON.stringify(selected.spriteMap));
  }
}

function handlePlayerData(msg: MsgPlayerData) {
  if (msg.result !== true) {
    showMessage(msg.logStr ?? "云端数据同步失败", "error");
    return;
  }

  const current = useNetStore.getState();
  const account = msg.account ?? current.account;
  if (!account || !msg.displayName || !msg.decks) {
    showMessage("服务端返回的玩家数据不完整", "error");
    return;
  }

  applyPlayerData(
    account,
    msg.displayName,
    msg.avatar ?? "",
    msg.cardBackId ?? current.cardBackId,
    msg.canChangeDisplayName ?? current.canChangeDisplayName,
    msg.selectedDeckName ?? null,
    msg.decks,
  );

  if (pendingLegacyImport?.account === account) {
    const desiredSelection = pendingLegacyImport.selectedDeckName;
    pendingLegacyImport = null;
    markLegacyDecksMigrated(account);
    if (desiredSelection && msg.decks.some((deck) => deck.name === desiredSelection)) {
      HomeRequest.selectDeck(desiredSelection);
    }
  }

  if (msg.logStr) showMessage(msg.logStr, "info");
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
    saveAuthToken(useNetStore.getState().account, msg.authToken);
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
    useNetStore.getState().setMatchState("idle");
    showMessage(msg.logStr ?? "加入匹配失败", "error");
    return;
  }
  if (msg.queueKind) useNetStore.getState().setMatchQueueKind(msg.queueKind);
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

function handleSelectRankFaction(msg: MsgSelectRankFaction) {
  if (!msg.result || !msg.profile) {
    showMessage(msg.logStr ?? "阵营选择失败", "error");
    return;
  }
  const mode = msg.mode ?? "standard";
  const previousFaction = useNetStore.getState().rankProfiles[mode]?.faction;
  useNetStore.getState().setRankSnapshot(mode, msg.profile, msg.leaderboard ?? [], msg.factionStandings);
  showMessage(previousFaction && previousFaction !== msg.profile.faction
    ? "阵营已更换，排位进度已清空，请重新定级"
    : "阵营已选定", "info");
}

/**
 * MsgMatchFound — 匹配成功
 */
function handleMatchFound(msg: MsgMatchFound) {
  if (msg.queueKind) {
    useNetStore.getState().setMatchQueueKind(msg.queueKind);
    if (typeof window !== "undefined") sessionStorage.setItem("grandumi_match_queue_kind", msg.queueKind);
  }
  useNetStore.getState().setOpponentName(msg.opponentName);
  useNetStore.getState().setMatchState("matched");
  showMessage(`匹配成功！对手：${msg.opponentName}`, "info");
}

function handleRankSnapshot(msg: MsgRankSnapshot) {
  if (!msg.profile) return;
  useNetStore.getState().setRankSnapshot(msg.mode ?? "standard", msg.profile, msg.leaderboard ?? [], msg.factionStandings);
}

function handleRankResult(msg: MsgRankResult) {
  if (msg.error) {
    showMessage(msg.error, "error");
    return;
  }
  if (msg.profile) useNetStore.getState().setRankSnapshot(msg.mode ?? "standard", msg.profile, msg.leaderboard ?? [], msg.factionStandings);
  if (msg.result) useNetStore.getState().setLastRankResult(msg.result);
}

/**
 * MsgCreateRoom — 创建房间回包
 * 服务器返回房间码
 */
function handleCreateRoom(msg: MsgCreateRoom) {
  clearRoomRequestTimer();
  const store = useNetStore.getState();
  store.setRoomOperation("idle");
  if (msg.result === false) {
    store.setRoomCode(null);
    showMessage(msg.logStr ?? "创建房间失败", "error");
    return;
  }
  store.setRoomCode(msg.roomCode ?? null);
  showMessage("房间创建成功，等待对手加入", "info");
}

/**
 * MsgJoinRoom — 加入房间回包
 */
function handleJoinRoom(msg: MsgJoinRoom) {
  clearRoomRequestTimer();
  useNetStore.getState().setRoomOperation("idle");
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
  clearRoomRequestTimer();
  const store = useNetStore.getState();
  store.setRoomCode(null);
  store.setRoomOperation("idle");
}

/**
 * MsgGameStart — 游戏开始
 * 跳转到游戏场景
 */
function handleGameStart(msg: MsgGameStart) {
  const { useGameStore } = require("@/store/gameStore");
  const gameStore = useGameStore.getState();
  // MsgGameStart 是新对局的生命周期边界。必须在首份权威快照到达前清掉上一局终局状态，
  // 否则连续匹配时会先用旧的 isGameOver/牌桌镜像渲染新对局。
  gameStore.resetGame();
  NetManager.resetGameStateBaseline();
  // 先后手信息由服务端 MsgGameState 决定，这里仅切换为对战模式。
  gameStore.setMode("Player");

  if (typeof window !== "undefined") {
    sessionStorage.setItem("myDeck", msg.MainDeck ?? "");
    sessionStorage.setItem("enemyDeck", msg.EnemyDeck ?? "");
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

function handleGlobalAnnouncement(msg: MsgGlobalAnnouncement) {
  if (msg.result === undefined) return;
  showMessage(msg.logStr ?? (msg.result ? "全服公告已发送" : "全服公告发送失败"), msg.result ? "info" : "error");
}

function handleMaintenanceState(msg: MsgMaintenanceState) {
  const store = useNetStore.getState();
  store.setMaintenance({
    enabled: msg.enabled === true,
    activeRoomCount: Math.max(0, Number(msg.activeRoomCount) || 0),
    startedAt: typeof msg.startedAt === "number" ? msg.startedAt : null,
    canManage: msg.canManage === true,
  });
  if (msg.enabled && store.matchState === "matching") store.setMatchState("idle");
  if (msg.logStr) showMessage(msg.logStr, msg.result === false ? "error" : "info");
}

function handleRulesetState(msg: MsgRulesetState) {
  const store = useNetStore.getState();
  if (msg.activeRulesetId && Array.isArray(msg.availableRulesets)) {
    store.setRulesets({
      activeRulesetId: msg.activeRulesetId,
      availableRulesets: msg.availableRulesets,
      activeRoomCounts: msg.activeRoomCounts ?? {},
    });
  }
  if (msg.logStr) showMessage(msg.logStr, msg.result === false ? "error" : "info");
}

function handleAdminOperations(msg: MsgAdminOperations) {
  const store = useNetStore.getState();
  if (msg.currentCommit && msg.test && msg.production) {
    store.setAdminOperations({
      currentCommit: msg.currentCommit,
      deploymentAvailable: msg.deploymentAvailable === true,
      peaks7: Array.isArray(msg.peaks7) ? msg.peaks7 : [],
      peaks30: Array.isArray(msg.peaks30) ? msg.peaks30 : [],
      test: msg.test,
      production: msg.production,
    });
  }
  if (msg.logStr) showMessage(msg.logStr, msg.result === false ? "error" : "info");
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

function handleFriendList(msg: MsgFriendList) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "好友列表加载失败", "error");
    return;
  }
  useNetStore.getState().setFriendData(
    msg.friends ?? [],
    msg.incomingRequests ?? [],
    msg.outgoingRequests ?? [],
  );
  if (msg.logStr) showMessage(msg.logStr, "info");
}

function handleFriendSearch(msg: MsgFriendSearch) {
  if (msg.result === false) {
    useNetStore.getState().setFriendSearchResults([]);
    showMessage(msg.logStr ?? "搜索玩家失败", "error");
    return;
  }
  useNetStore.getState().setFriendSearchResults(msg.players ?? []);
}

function handleFriendMutation(msg: MsgFriendRequest | MsgFriendRespond | MsgFriendRemove | MsgFriendCancel) {
  showMessage(
    msg.logStr ?? (msg.result === false ? "好友操作失败" : "好友状态已更新"),
    msg.result === false ? "error" : "info",
  );
}

function handlePlayerSafety(msg: MsgPlayerSafety) {
  if (msg.result !== false) {
    const store = useNetStore.getState();
    const blockedPlayers = msg.blockedPlayers ?? [];
    const blockedKeys = new Set(blockedPlayers.map((player) => player.account.toLocaleLowerCase("zh-CN")));
    store.setBlockedPlayers(blockedPlayers);
    store.setPlayerList(store.playerList.filter(
      (player) => !blockedKeys.has(player.account.toLocaleLowerCase("zh-CN")),
    ));
    store.setFriendSearchResults(store.friendSearchResults.filter(
      (player) => !blockedKeys.has(player.account.toLocaleLowerCase("zh-CN")),
    ));
  }
  if (msg.result === false || msg.logStr) {
    showMessage(
      msg.logStr ?? "安全操作失败",
      msg.result === false ? "error" : "info",
    );
  }
}

/** MsgLeaderLeaderboard — 服务端 Leader 聚合榜单。 */
function handleLeaderLeaderboard(msg: MsgLeaderLeaderboard) {
  useNetStore.getState().setLeaderLeaderboard(msg);
  if (msg.result === false && msg.error) showMessage(msg.error, "error");
}

/** MsgLeaderMatchups — 指定 Leader 对阵当前周期榜前二十及起手留牌的统计。 */
function handleLeaderMatchups(msg: MsgLeaderMatchups) {
  useNetStore.getState().setLeaderMatchups(msg);
}

/** MsgLeaderMatchupMatrix — 当前周期榜前十五的完整对阵矩阵。 */
function handleLeaderMatchupMatrix(msg: MsgLeaderMatchupMatrix) {
  useNetStore.getState().setLeaderMatchupMatrix(msg);
}

/** MsgPlayerProfileStats — 当前登录账号的周期战绩。 */
function handlePlayerProfileStats(msg: MsgPlayerProfileStats) {
  useNetStore.getState().setPlayerProfileStats(msg);
  if (msg.result === false && msg.error) showMessage(msg.error, "error");
}

function handleCardBackGallery(msg: MsgCardBackGallery) {
  if (msg.result === false) {
    const store = useNetStore.getState();
    if (store.cardBackGallery === null) store.setCardBackGallery([]);
    store.setCardBackGalleryLoadingMore(false);
    showMessage(msg.logStr ?? "卡背广场操作失败", "error");
    return;
  }
  useNetStore.getState().setCardBackGalleryPage({
    items: Array.isArray(msg.items) ? msg.items : [],
    ownedItems: Array.isArray(msg.ownedItems) ? msg.ownedItems : [],
    total: Math.max(0, msg.total ?? 0),
    nextCursor: msg.nextCursor ?? null,
    hasMore: Boolean(msg.hasMore),
    append: Boolean(msg.cursor),
  });
  if (msg.logStr) showMessage(msg.logStr, "info");
}

function handleCardBackLike(msg: MsgLikeCardBack) {
  if (msg.result === false || !msg.item) {
    showMessage(msg.logStr ?? "更新红心失败", "error");
    return;
  }
  useNetStore.getState().updateCardBackGalleryItem(msg.item);
}

function handleCardBackReviewQueue(msg: MsgCardBackReviewQueue) {
  if (msg.result === false) {
    const current = useNetStore.getState().cardBackReviewQueue;
    useNetStore.getState().setCardBackReviewQueue(current ? [...current] : []);
    showMessage(msg.logStr ?? "卡背审核操作失败", "error");
    return;
  }
  useNetStore.getState().setCardBackReviewQueue(Array.isArray(msg.items) ? msg.items : []);
  if (msg.logStr) showMessage(msg.logStr, "info");
}

function handleDeckPlazaList(msg: MsgDeckPlazaList) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "读取卡组广场失败", "error");
    return;
  }
  useNetStore.getState().setDeckPlazaPage({
    items: Array.isArray(msg.items) ? msg.items : [],
    page: msg.page ?? 1,
    pageSize: msg.pageSize ?? 20,
    total: msg.total ?? 0,
    hasMore: Boolean(msg.hasMore),
  });
}

function handleDeckPlazaMutation(
  msg: MsgPublishDeckPlaza | MsgLikeDeckPlaza | MsgCopyDeckPlaza | MsgDeleteDeckPlaza,
) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "卡组广场操作失败", "error");
    return;
  }
  if (msg.logStr) showMessage(msg.logStr, "info");
  useNetStore.getState().refreshDeckPlaza();
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
    origin: msg.origin ?? "invite",
    roomCode: msg.roomCode ?? null,
    players: msg.players,
    scores: msg.scores,
    state: msg.state,
  });
  useNetStore.getState().setRoomCode(msg.roomCode ?? null);
  useNetStore.getState().setRoomOperation("idle");
  if (msg.error) showMessage(msg.error, "error");
}

/** MsgFriendlyLeft — 房间已解散/自己已退出 */
function handleFriendlyLeft(msg: MsgFriendlyLeft) {
  const store = useNetStore.getState();
  store.setFriendlyRoom(null);
  store.setRoomCode(null);
  store.setRoomOperation("idle");
  if (msg.logStr) showMessage(msg.logStr, "info");
}

/** MsgSpectateRoom — 服务端确认成功后才进入观战页，失败则留在原页面 */
function handleSpectateRoom(msg: MsgSpectateRoom) {
  const netStore = useNetStore.getState();
  clearSpectateRequestTimer();

  // 超时后才抵达的成功回包不能再把用户带入对局，同时主动清理服务端关系。
  if (netStore.spectateState !== "joining") {
    if (msg.result !== false) NetManager.send({ proto: "MsgLeaveSpectate" } as MsgLeaveSpectate);
    return;
  }

  if (msg.result === false) {
    netStore.setSpectate("idle");
    showMessage(msg.logStr ?? "无法进入观战", "error");
    return;
  }

  const roomId = msg.roomId || netStore.spectateRoomId;
  netStore.setSpectate("watching", roomId);
  useGameStore.getState().setMode("Observer");
  netStore.setNavigateTo("/game");
}

/** MsgLeaveSpectate — 主动退出观战回执（服务端按幂等方式处理） */
function handleLeaveSpectate(msg: MsgLeaveSpectate) {
  useNetStore.getState().setSpectate("idle");
  if (msg.result === false && msg.logStr) showMessage(msg.logStr, "error");
}

function handleUpdateSpectateSettings(msg: MsgUpdateSpectateSettings) {
  if (msg.result === false) {
    showMessage(msg.logStr ?? "观战设置保存失败", "error");
    return;
  }
  const mode = msg.mode ?? "open";
  const handsPublic = msg.handsPublic === true;
  useNetStore.getState().setSpectateSettings(mode, handsPublic, msg.spectateCode ?? null);
  if (typeof window !== "undefined") {
    localStorage.setItem("grandumi_spectate_settings", JSON.stringify({ mode, handsPublic }));
  }
}

// ── 请求发送 ────────────────────────────────────────────────────────────
// 对应 C# HomeProtocol.cs 中的各 Request 静态方法

export const HomeRequest = {
  login(account: string, password?: string, resume = false) {
    return NetManager.send({
      proto: "MsgLogin",
      account,
      clientInstanceId: getClientInstanceId(),
      resume,
      ...(password === undefined
        ? { authToken: readAuthToken(account) }
        : { password }),
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

  updatePassword(currentPassword: string, newPassword: string) {
    return NetManager.send({
      proto: "MsgUpdatePs",
      currentPassword,
      newPassword,
    } as MsgUpdatePs);
  },

  saveDeck(deck: SavedDeck) {
    return NetManager.send({ proto: "MsgSaveDeck", deck } as MsgSaveDeck);
  },

  deleteDeck(name: string) {
    return NetManager.send({ proto: "MsgDeleteDeck", name } as MsgDeleteDeck);
  },

  selectDeck(name: string | null) {
    return NetManager.send({ proto: "MsgSelectDeck", name } as MsgSelectDeck);
  },

  updateProfile(displayName: string, avatar: string) {
    return NetManager.send({ proto: "MsgUpdateProfile", displayName, avatar } as MsgUpdateProfile);
  },

  updateCardBack(cardBackId: string) {
    return NetManager.send({ proto: "MsgUpdateCardBack", cardBackId } as MsgUpdateCardBack);
  },

  requestCardBackGallery(cursor?: string | null) {
    const store = useNetStore.getState();
    if (cursor) store.setCardBackGalleryLoadingMore(true);
    else store.setCardBackGallery(null);
    const sent = NetManager.send({
      proto: "MsgCardBackGallery",
      cursor: cursor ?? null,
      pageSize: 40,
    } as MsgCardBackGallery);
    if (!sent && cursor) store.setCardBackGalleryLoadingMore(false);
    return sent;
  },

  uploadCardBack(name: string, mimeType: MsgUploadCardBack["mimeType"], imageBase64: string) {
    return NetManager.send({ proto: "MsgUploadCardBack", name, mimeType, imageBase64 } as MsgUploadCardBack);
  },

  toggleCardBackLike(cardBackId: string) {
    return NetManager.send({ proto: "MsgLikeCardBack", cardBackId } as MsgLikeCardBack);
  },

  deleteCardBack(cardBackId: string) {
    return NetManager.send({ proto: "MsgDeleteCardBack", cardBackId } as MsgDeleteCardBack);
  },

  requestCardBackReviewQueue() {
    useNetStore.getState().setCardBackReviewQueue(null);
    return NetManager.send({ proto: "MsgCardBackReviewQueue" } as MsgCardBackReviewQueue);
  },

  reviewCardBack(cardBackId: string, approved: boolean, reason?: string) {
    return NetManager.send({ proto: "MsgReviewCardBack", cardBackId, approved, reason } as MsgReviewCardBack);
  },

  importDecks(decks: SavedDeck[]) {
    return NetManager.send({ proto: "MsgImportDecks", decks } as MsgImportDecks);
  },

  requestDeckPlaza(options: {
    page?: number;
    pageSize?: number;
    sort?: MsgDeckPlazaList["sort"];
    query?: string;
    color?: string;
    mineOnly?: boolean;
  } = {}) {
    useNetStore.getState().setDeckPlazaPage(null);
    return NetManager.send({ proto: "MsgDeckPlazaList", ...options } as MsgDeckPlazaList);
  },

  publishDeckPlaza(sourceDeckName: string, title: string, publicationId?: string) {
    return NetManager.send({
      proto: "MsgPublishDeckPlaza",
      sourceDeckName,
      title,
      publicationId,
    } as MsgPublishDeckPlaza);
  },

  toggleDeckPlazaLike(publicationId: string) {
    return NetManager.send({ proto: "MsgLikeDeckPlaza", publicationId } as MsgLikeDeckPlaza);
  },

  copyDeckPlaza(publicationId: string) {
    return NetManager.send({ proto: "MsgCopyDeckPlaza", publicationId } as MsgCopyDeckPlaza);
  },

  deleteDeckPlaza(publicationId: string) {
    return NetManager.send({ proto: "MsgDeleteDeckPlaza", publicationId } as MsgDeleteDeckPlaza);
  },

  enterMatch(deck: string, deckName?: string, queueKind: MatchQueueKind = "casual") {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    useNetStore.getState().setMatchQueueKind(queueKind);
    useNetStore.getState().setLastRankResult(null);
    return NetManager.send({
      proto: "MsgEnterMatch",
      deck,
      deckName,
      queueKind,
    } as MsgEnterMatch);
  },

  enterBotMatch(deck: string, goFirst: boolean = true, deckName?: string) {
    // 单人测试：标记本局为机器人对战，对战页据此显示 GM 按钮
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "1");
    return NetManager.send({
      proto: "MsgEnterBotMatch",
      deck,
      goFirst,
      deckName,
    } as MsgEnterBotMatch);
  },

  cancelMatch() {
    NetManager.send({
      proto: "MsgCancelMatch",
    } as MsgCancelMatch);
  },

  requestRankSnapshot(mode: RankedMode = "standard") {
    return NetManager.send({ proto: "MsgRankSnapshot", mode } as MsgRankSnapshot);
  },

  selectRankFaction(faction: RankFaction, resetRankProgress = false, mode: RankedMode = "standard") {
    return NetManager.send({
      proto: "MsgSelectRankFaction",
      faction,
      resetRankProgress,
      mode,
    } as MsgSelectRankFaction);
  },

  createRoom(deck: string, deckName: string) {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    const sent = NetManager.send({
      proto: "MsgCreateRoom",
      deck,
      deckName,
    } as MsgCreateRoom);
    if (sent) armRoomRequestTimer();
    return sent;
  },

  joinRoom(roomCode: string, deck: string, deckName: string) {
    if (typeof window !== "undefined") sessionStorage.setItem("isBotMatch", "0");
    const sent = NetManager.send({
      proto: "MsgJoinRoom",
      roomCode,
      deck,
      deckName,
    } as MsgJoinRoom);
    if (sent) armRoomRequestTimer();
    return sent;
  },

  cancelRoom() {
    clearRoomRequestTimer();
    return NetManager.send({
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

  sendGlobalAnnouncement(content: string) {
    return NetManager.send({
      proto: "MsgGlobalAnnouncement",
      content,
    } as MsgGlobalAnnouncement);
  },

  requestMaintenanceState() {
    return NetManager.send({ proto: "MsgMaintenanceState" } as MsgMaintenanceState);
  },

  setMaintenance(enabled: boolean) {
    return NetManager.send({ proto: "MsgSetMaintenance", enabled } as MsgSetMaintenance);
  },

  requestRulesetState() {
    return NetManager.send({ proto: "MsgRulesetState" } as MsgRulesetState);
  },

  activateRuleset(rulesetId: string) {
    return NetManager.send({ proto: "MsgActivateRuleset", rulesetId } as MsgActivateRuleset);
  },

  requestAdminOperations() {
    return NetManager.send({ proto: "MsgAdminOperations" } as MsgAdminOperations);
  },

  deployLatest(environment: AdminDeploymentEnvironment) {
    return NetManager.send({ proto: "MsgAdminDeploy", environment } as MsgAdminDeploy);
  },

  requestPlayerList(offset = 0, limit = 200) {
    return NetManager.send({ proto: "MsgPlayerList", offset, limit } as MsgPlayerList);
  },

  requestFriendList() {
    return NetManager.send({ proto: "MsgFriendList" } as MsgFriendList);
  },

  searchFriends(query: string) {
    useNetStore.getState().setFriendSearchResults([]);
    return NetManager.send({ proto: "MsgFriendSearch", query } as MsgFriendSearch);
  },

  sendFriendRequest(toAccount: string) {
    return NetManager.send({ proto: "MsgFriendRequest", toAccount } as MsgFriendRequest);
  },

  sendOpponentFriendRequest() {
    return NetManager.send({ proto: "MsgFriendRequest", currentOpponent: true } as MsgFriendRequest);
  },

  respondFriendRequest(requestId: number, accept: boolean) {
    return NetManager.send({ proto: "MsgFriendRespond", requestId, accept } as MsgFriendRespond);
  },

  cancelFriendRequest(requestId: number) {
    return NetManager.send({ proto: "MsgFriendCancel", requestId } as MsgFriendCancel);
  },

  removeFriend(account: string) {
    return NetManager.send({ proto: "MsgFriendRemove", account } as MsgFriendRemove);
  },

  requestPlayerSafety() {
    return NetManager.send({ proto: "MsgPlayerSafety", action: "list" } as MsgPlayerSafety);
  },

  blockPlayer(targetAccount?: string, currentOpponent = false) {
    return NetManager.send({
      proto: "MsgPlayerSafety",
      action: "block",
      targetAccount,
      currentOpponent,
    } as MsgPlayerSafety);
  },

  unblockPlayer(targetAccount: string) {
    return NetManager.send({
      proto: "MsgPlayerSafety",
      action: "unblock",
      targetAccount,
    } as MsgPlayerSafety);
  },

  reportPlayer(
    description: string,
    category: MsgPlayerSafety["category"] = "harassment",
    targetAccount?: string,
    currentOpponent = false,
  ) {
    return NetManager.send({
      proto: "MsgPlayerSafety",
      action: "report",
      targetAccount,
      currentOpponent,
      category,
      description,
    } as MsgPlayerSafety);
  },

  requestLeaderLeaderboard(period: LeaderboardPeriod) {
    const store = useNetStore.getState();
    store.setLeaderLeaderboard(null);
    store.clearLeaderMatchups();
    store.setLeaderMatchupMatrix(null);
    return NetManager.send({ proto: "MsgLeaderLeaderboard", period } as MsgLeaderLeaderboard);
  },

  requestLeaderMatchups(period: LeaderboardPeriod, leaderNumber: string) {
    useNetStore.getState().setLeaderMatchups({
      proto: "MsgLeaderMatchups",
      period,
      leaderNumber,
    });
    return NetManager.send({ proto: "MsgLeaderMatchups", period, leaderNumber } as MsgLeaderMatchups);
  },

  requestLeaderMatchupMatrix(period: LeaderboardPeriod) {
    useNetStore.getState().setLeaderMatchupMatrix({
      proto: "MsgLeaderMatchupMatrix",
      period,
    });
    return NetManager.send({ proto: "MsgLeaderMatchupMatrix", period } as MsgLeaderMatchupMatrix);
  },

  requestPlayerProfileStats(period: LeaderboardPeriod) {
    useNetStore.getState().setPlayerProfileStats(null);
    return NetManager.send({ proto: "MsgPlayerProfileStats", period } as MsgPlayerProfileStats);
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
  spectateRoom(roomId: string, viewPlayerIndex: 0 | 1 = 0, spectateCode?: string) {
    const normalizedRoomId = roomId.trim();
    if (!normalizedRoomId) {
      showMessage("请输入有效的房间 ID", "error");
      return false;
    }

    const netStore = useNetStore.getState();
    if (netStore.spectateState === "joining") return false;

    netStore.setSpectate("joining", normalizedRoomId);
    const sent = NetManager.send({
      proto: "MsgSpectateRoom",
      roomId: normalizedRoomId,
      viewPlayerIndex,
      spectateCode: spectateCode?.trim(),
    } as MsgSpectateRoom);
    if (!sent) {
      netStore.setSpectate("idle");
      showMessage("网络未连接，无法进入观战", "error");
      return false;
    }

    clearSpectateRequestTimer();
    spectateRequestTimer = setTimeout(() => {
      const current = useNetStore.getState();
      if (current.spectateState !== "joining" || current.spectateRoomId !== normalizedRoomId) return;
      current.setSpectate("idle");
      showMessage("进入观战超时，请确认对局仍在进行后重试", "error");
    }, 8_000);
    return sent;
  },

  updateSpectateSettings(mode: SpectateMode, handsPublic: boolean, regenerateCode = false) {
    useNetStore.getState().setSpectateSettings(mode, handsPublic, mode === "password"
      ? useNetStore.getState().spectateCode
      : null);
    return NetManager.send({
      proto: "MsgUpdateSpectateSettings",
      mode,
      handsPublic,
      regenerateCode,
    } as MsgUpdateSpectateSettings);
  },

  /** 主动退出观战；本地立即恢复大厅状态，服务端回执用于最终确认 */
  leaveSpectate() {
    clearSpectateRequestTimer();
    const sent = NetManager.send({ proto: "MsgLeaveSpectate" } as MsgLeaveSpectate);
    useNetStore.getState().setSpectate("idle");
    return sent;
  },
};
