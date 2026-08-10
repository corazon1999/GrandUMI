"use client";

import { useEffect, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useNet } from "@/hooks/useNet";
import { useNetStore } from "@/store/netStore";
import LoginPanel from "@/components/home/LoginPanel";
import GlobalAnnouncementBanner from "@/components/ui/GlobalAnnouncementBanner";
import MessageBox from "@/components/ui/MessageBox";

const GAME_REFRESH_RESUME_KEY = "grandumi_resume_game_after_refresh";

export default function NetProvider({ children }: { children: ReactNode }) {
  useNet();
  const router = useRouter();
  const pathname = usePathname();
  const navigateTo = useNetStore((s) => s.navigateTo);
  const loggedIn = useNetStore((s) => s.loggedIn);
  const requiresLogin = pathname === "/game" || pathname === "/spectate";

  useEffect(() => {
    // 对局页整页刷新会重建内存登录态；先留下仅本标签页生效的一次性恢复标记，
    // 握手完成后再用已保存账号自动登录，由服务端按账号找回原房间。
    if (!loggedIn && requiresLogin) {
      if (pathname === "/game") {
        const savedAccount = localStorage.getItem("grandumi_account")?.trim();
        if (savedAccount) sessionStorage.setItem(GAME_REFRESH_RESUME_KEY, "1");
      }
      router.replace("/home");
    }
  }, [loggedIn, pathname, requiresLogin, router]);

  useEffect(() => {
    if (navigateTo) {
      useNetStore.getState().setNavigateTo(null);
      router.push(navigateTo);
    }
  }, [navigateTo, router]);

  return (
    <>
      {!loggedIn && requiresLogin ? <LoginPanel /> : children}
      <GlobalAnnouncementBanner />
      <MessageBox />
    </>
  );
}
