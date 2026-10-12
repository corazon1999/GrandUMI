"use client";

import { useEffect, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useNet } from "@/hooks/useNet";
import { useNetStore } from "@/store/netStore";
import LoginPanel from "@/components/home/LoginPanel";
import GlobalAnnouncementBanner from "@/components/ui/GlobalAnnouncementBanner";
import MessageBox from "@/components/ui/MessageBox";
import { getSessionReplacedNotice } from "@/net/sessionReplacement";
import { GameRequest } from "@/net/GameRequest";
import { NetManager } from "@/net/NetManager";
import { startGameEntryRecovery } from "@/net/gameEntryRecovery";

const GAME_REFRESH_RESUME_KEY = "grandumi_resume_game_after_refresh";

export default function NetProvider({ children }: { children: ReactNode }) {
  useNet();
  const router = useRouter();
  const pathname = usePathname();
  const navigateTo = useNetStore((s) => s.navigateTo);
  const loggedIn = useNetStore((s) => s.loggedIn);
  const matchState = useNetStore((s) => s.matchState);
  const requiresLogin = pathname === "/game" || pathname === "/spectate";
  const enteringGame = loggedIn && pathname !== "/game"
    && (navigateTo === "/game" || (matchState === "matched" && !navigateTo));

  useEffect(() => {
    if (loggedIn) router.prefetch("/game");
  }, [loggedIn, router]);

  useEffect(() => {
    // 对局页整页刷新会重建内存登录态；先留下仅本标签页生效的一次性恢复标记，
    // 握手完成后再用已保存账号自动登录，由服务端按账号找回原房间。
    if (!loggedIn && requiresLogin) {
      if (pathname === "/game") {
        const savedAccount = localStorage.getItem("grandumi_account")?.trim();
        if (savedAccount && !getSessionReplacedNotice()) {
          sessionStorage.setItem(GAME_REFRESH_RESUME_KEY, "1");
        } else {
          sessionStorage.removeItem(GAME_REFRESH_RESUME_KEY);
        }
      }
      router.replace("/home");
    }
  }, [loggedIn, pathname, requiresLogin, router]);

  useEffect(() => {
    if (navigateTo) {
      // push 只发起异步导航；到达目标页面前保留请求，避免加载停滞后失去恢复依据。
      if (pathname === navigateTo) useNetStore.getState().setNavigateTo(null);
      else router.push(navigateTo);
    }
  }, [navigateTo, pathname, router]);

  useEffect(() => {
    if (!enteringGame) return;
    return startGameEntryRecovery({
      isPending: () => {
        const net = useNetStore.getState();
        return net.loggedIn && window.location.pathname !== "/game"
          && (net.navigateTo === "/game" || (net.matchState === "matched" && !net.navigateTo));
      },
      isConnected: () => NetManager.isConnected,
      requestState: () => { GameRequest.refreshStateSnapshot(); },
      retryNavigation: () => {
        if (useNetStore.getState().navigateTo === "/game") router.replace("/game");
      },
      reloadGame: () => {
        console.warn("[入场] 页面跳转超时，重新加载对局页并恢复原房间。");
        window.location.replace("/game");
      },
    });
  }, [enteringGame, router]);

  return (
    <>
      {!loggedIn && requiresLogin ? <LoginPanel /> : children}
      <GlobalAnnouncementBanner />
      <MessageBox />
    </>
  );
}
