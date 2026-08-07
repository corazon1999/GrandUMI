"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { showMessage } from "@/components/ui/MessageBox";
import ChatPanel from "./ChatPanel";

// 从导出卡组码（exportDeckString 格式 A）统计主卡组张数：
// 卡牌行形如「<数量> <卡号>」，跳过「# 注释」与「领航:」行。
// 解析不出（返回 0）时按「未知」处理，调用方 fail-open，避免误拦合法卡组。
function countMainCards(deckStr: string): number {
  let total = 0;
  for (const line of deckStr.split("\n")) {
    const m = line.match(/^\s*(\d+)\s+[A-Za-z]/);
    if (m) total += parseInt(m[1], 10);
  }
  return total;
}

export default function LobbyPanel({ onGoToDeck }: { onGoToDeck: () => void }) {
  const matchState    = useNetStore((s) => s.matchState);
  const selectedDeck  = useNetStore((s) => s.selectedDeck);
  const opponentName  = useNetStore((s) => s.opponentName);
  const playerName    = useNetStore((s) => s.playerName);
  const roomCode      = useNetStore((s) => s.roomCode);
  const roomOperation = useNetStore((s) => s.roomOperation);
  const connState     = useNetStore((s) => s.connState);

  const [roomMode, setRoomMode] = useState<"none" | "create" | "join">("none");
  const [joinInput, setJoinInput] = useState("");
  const [copied, setCopied] = useState(false);
  const [botGoFirst, setBotGoFirst] = useState(true);

  // 主卡组须恰好 50 张（后端 DeckValidator 强制，不满会被拒，bug #183）。
  // 这里前置拦截：未满 50 时置灰按钮并提示，避免「点了没反应」。
  // mainCount === 0 表示解析失败/未知，fail-open 不拦截，交由后端校验兜底。
  const mainCount     = selectedDeck ? countMainCards(selectedDeck.cards) : 0;
  const deckIncomplete = mainCount > 0 && mainCount !== 50;
  const canEnter      = !!selectedDeck && !deckIncomplete && connState === "connected" && roomOperation === "idle";

  useEffect(() => {
    if (roomMode === "create" && roomOperation === "idle" && !roomCode) {
      setRoomMode("none");
    }
  }, [roomCode, roomMode, roomOperation]);

  const handleMatch = () => {
    if (!selectedDeck) return;
    const sent = HomeRequest.enterMatch(selectedDeck.cards);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
    }
  };

  const handleCancelMatch = () => {
    HomeRequest.cancelMatch();
  };

  // 单人测试：与机器人对战，方便测试单卡效果（机器人轮到自己会自动结束回合）
  const handleBotMatch = () => {
    if (!selectedDeck) return;
    const sent = HomeRequest.enterBotMatch(selectedDeck.cards, botGoFirst);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
    }
  };

  const handleCreateRoom = () => {
    if (!selectedDeck) return;
    setRoomMode("create");
    useNetStore.getState().setRoomOperation("creating");
    const sent = HomeRequest.createRoom(selectedDeck.cards, selectedDeck.name);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
      useNetStore.getState().setRoomOperation("idle");
      setRoomMode("none");
    }
  };

  const handleJoinRoom = () => {
    if (!selectedDeck) return;
    setRoomMode("join");
  };

  const confirmJoinRoom = () => {
    const code = joinInput.trim().toUpperCase();
    if (code.length < 6 || !selectedDeck) return;
    useNetStore.getState().setRoomOperation("joining");
    const sent = HomeRequest.joinRoom(code, selectedDeck.cards, selectedDeck.name);
    if (!sent) {
      useNetStore.getState().setRoomOperation("idle");
      showMessage("服务器未连接，房间码已保留，请重连后重试", "error");
    }
  };

  const handleCancelRoom = () => {
    HomeRequest.cancelRoom();
    useNetStore.getState().setRoomOperation("idle");
    setRoomMode("none");
    setJoinInput("");
  };

  const copyRoomCode = () => {
    if (!roomCode) return;
    navigator.clipboard.writeText(roomCode).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  };

  return (
    <div className="flex h-full">
      {/* 主内容区 */}
      <div className="flex-1 flex flex-col items-center justify-center p-8 gap-6 overflow-hidden">
        {/* 标题 */}
        <h2 className="text-white font-bold text-2xl">GrandUMI 对战</h2>

        {/* 已选卡组信息 */}
        <div className="bg-gray-900 border border-gray-800 rounded-xl px-6 py-4 w-full max-w-sm">
          {selectedDeck ? (
            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <span className="text-gray-400 text-xs">已选卡组</span>
                <span className="text-orange-400 text-[10px] font-bold">已就绪</span>
              </div>
              <div className="flex items-center gap-3">
                {/* 领航头像 */}
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={selectedDeck.leaderSprite || "/sprites/CardBack.png"}
                  alt={selectedDeck.leaderName}
                  className="w-10 h-14 object-cover rounded border border-gray-600 shrink-0"
                  onError={(e) => {
                    (e.target as HTMLImageElement).src = "/sprites/CardBack.png";
                  }}
                />
                <div className="min-w-0">
                  <p className="text-white text-sm font-bold truncate">{selectedDeck.name}</p>
                  <p className="text-gray-500 text-xs truncate">
                    领航：{selectedDeck.leaderName}
                  </p>
                </div>
              </div>
              {deckIncomplete && (
                <p className="text-red-400 text-xs font-bold">
                  卡组需 50 张（当前 {mainCount} 张），请补满后再进入对战
                </p>
              )}
            </div>
          ) : (
            <div className="text-center py-2">
              <button
                onClick={onGoToDeck}
                className="text-gray-500 hover:text-orange-400 text-sm transition-colors underline underline-offset-4 decoration-gray-700 hover:decoration-orange-400"
              >
                请先在「卡组」面板选择一副卡组
              </button>
            </div>
          )}
        </div>

        {/* 匹配按钮区 */}
        <div className="flex flex-col items-center gap-3">
          {matchState === "idle" && (
            <div className="flex items-center gap-3">
              <button
                onClick={handleMatch}
                disabled={!canEnter}
                className={`px-10 py-3 rounded-xl text-lg font-bold transition-all ${
                  canEnter
                    ? "bg-orange-500 hover:bg-orange-400 text-white hover:scale-105 active:scale-95"
                    : "bg-gray-800 text-gray-600 cursor-not-allowed"
                }`}
              >
                开始匹配
              </button>
              <div className="flex flex-col items-center gap-2">
                <button
                  onClick={handleBotMatch}
                  disabled={!canEnter}
                  title="与机器人对战，机器人轮到自己会自动结束回合，方便测试单卡效果"
                  className={`px-8 py-3 rounded-xl text-lg font-bold transition-all ${
                    canEnter
                      ? "bg-sky-600 hover:bg-sky-500 text-white hover:scale-105 active:scale-95"
                      : "bg-gray-800 text-gray-600 cursor-not-allowed"
                  }`}
                >
                  单人测试
                </button>
                <div className="flex overflow-hidden rounded-lg border border-sky-700 text-sm font-bold">
                  <button
                    onClick={() => setBotGoFirst(true)}
                    className={`px-4 py-1.5 transition-colors ${botGoFirst ? "bg-sky-600 text-white" : "bg-gray-800 text-gray-400 hover:text-gray-200"}`}
                  >
                    先手
                  </button>
                  <button
                    onClick={() => setBotGoFirst(false)}
                    className={`px-4 py-1.5 transition-colors ${!botGoFirst ? "bg-sky-600 text-white" : "bg-gray-800 text-gray-400 hover:text-gray-200"}`}
                  >
                    后手
                  </button>
                </div>
              </div>
            </div>
          )}

          {matchState === "matching" && (
            <>
              <div className="flex items-center gap-3 bg-gray-900 border border-orange-800 rounded-xl px-8 py-4">
                <div className="w-5 h-5 border-2 border-orange-500 border-t-transparent rounded-full animate-spin" />
                <span className="text-orange-400 text-sm font-bold">匹配中...</span>
              </div>
              <button
                onClick={handleCancelMatch}
                className="text-gray-500 hover:text-white text-xs transition-colors"
              >
                取消匹配
              </button>
            </>
          )}

          {matchState === "matched" && (
            <div className="flex flex-col items-center gap-2 bg-gray-900 border border-green-800 rounded-xl px-8 py-4">
              <p className="text-green-400 text-sm font-bold">匹配成功！</p>
              <p className="text-gray-300 text-xs">
                对手：<span className="text-white font-bold">{opponentName}</span>
              </p>
              <p className="text-gray-500 text-[10px]">即将开始游戏...</p>
            </div>
          )}
        </div>

        {/* 房间码对战区 */}
        {matchState === "idle" && !roomCode && roomMode === "none" && (
          <div className="flex flex-col items-center gap-2">
            <div className="flex items-center gap-2 text-gray-500 text-xs">
              <span className="border-t border-gray-700 w-8" />
              <span>或</span>
              <span className="border-t border-gray-700 w-8" />
            </div>
            <div className="flex gap-3">
              <button
                onClick={handleCreateRoom}
                disabled={!canEnter}
                className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all ${
                  canEnter
                    ? "bg-blue-600 hover:bg-blue-500 text-white"
                    : "bg-gray-800 text-gray-600 cursor-not-allowed"
                }`}
              >
                创建房间
              </button>
              <button
                onClick={handleJoinRoom}
                disabled={!canEnter}
                className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all ${
                  canEnter
                    ? "bg-green-600 hover:bg-green-500 text-white"
                    : "bg-gray-800 text-gray-600 cursor-not-allowed"
                }`}
              >
                加入房间
              </button>
            </div>
          </div>
        )}

        {/* 等待服务器创建房间 */}
        {!roomCode && roomMode === "create" && roomOperation === "creating" && (
          <div className="flex flex-col items-center gap-3 bg-gray-900 border border-blue-800 rounded-xl px-6 py-4">
            <div className="w-5 h-5 border-2 border-blue-500 border-t-transparent rounded-full animate-spin" />
            <p className="text-blue-400 text-sm font-bold">正在创建房间...</p>
            <button
              onClick={handleCancelRoom}
              className="px-3 py-1 rounded bg-gray-700 hover:bg-gray-600 text-gray-300 text-xs transition-colors"
            >
              取消
            </button>
          </div>
        )}

        {/* 房间码显示 */}
        {roomCode && roomMode === "create" && (
          <div className="flex flex-col items-center gap-3 bg-gray-900 border border-blue-800 rounded-xl px-6 py-4">
            <p className="text-white text-sm font-bold">房间码</p>
            <p className="text-blue-400 text-2xl font-mono font-bold tracking-widest select-all">
              {roomCode}
            </p>
            <div className="flex gap-2">
              <button
                onClick={copyRoomCode}
                className="px-3 py-1 rounded bg-blue-600 hover:bg-blue-500 text-white text-xs font-bold transition-colors"
              >
                {copied ? "已复制" : "复制房间码"}
              </button>
              <button
                onClick={handleCancelRoom}
                className="px-3 py-1 rounded bg-gray-700 hover:bg-gray-600 text-gray-300 text-xs transition-colors"
              >
                取消
              </button>
            </div>
            <p className="text-gray-500 text-[10px]">等待对手加入...</p>
          </div>
        )}

        {/* 加入房间输入 */}
        {roomMode === "join" && !roomCode && (
          <div className="flex flex-col items-center gap-3 bg-gray-900 border border-green-800 rounded-xl px-6 py-4">
            <p className="text-white text-sm font-bold">输入房间码</p>
            <input
              value={joinInput}
              onChange={(e) => setJoinInput(e.target.value.toUpperCase())}
              placeholder="6位房间码"
              maxLength={6}
              className="bg-gray-800 text-white text-center text-lg font-mono tracking-widest rounded px-3 py-2 outline-none border border-gray-700 focus:border-green-500 w-40"
              onKeyDown={(e) => e.key === "Enter" && confirmJoinRoom()}
            />
            <div className="flex gap-2">
              <button
                onClick={confirmJoinRoom}
                disabled={joinInput.trim().length < 6 || roomOperation === "joining" || connState !== "connected"}
                className={`px-4 py-1 rounded text-xs font-bold transition-colors ${
                  joinInput.trim().length >= 6 && roomOperation !== "joining" && connState === "connected"
                    ? "bg-green-600 hover:bg-green-500 text-white"
                    : "bg-gray-800 text-gray-600 cursor-not-allowed"
                }`}
              >
                {roomOperation === "joining" ? "正在加入…" : "加入"}
              </button>
              <button
                onClick={handleCancelRoom}
                className="px-4 py-1 rounded bg-gray-700 hover:bg-gray-600 text-gray-300 text-xs transition-colors"
              >
                取消
              </button>
            </div>
          </div>
        )}

        {/* 提示信息 */}
        <p className="text-gray-600 text-xs text-center max-w-xs">
          {roomCode && "将房间码发送给好友，对方在大厅点击「加入房间」输入即可"}
          {matchState === "idle" && !roomCode && roomMode === "none" && "选择卡组后点击匹配，系统将自动为您寻找对手"}
          {matchState === "matching" && "正在寻找水平相近的对手，请耐心等待"}
        </p>
      </div>

      {/* 聊天区 */}
      <div className="w-72 border-l border-gray-800 shrink-0">
        <ChatPanel />
      </div>
    </div>
  );
}
