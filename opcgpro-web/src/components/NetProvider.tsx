"use client";

import { useEffect, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useNet } from "@/hooks/useNet";
import { useNetStore } from "@/store/netStore";
import LoginPanel from "@/components/home/LoginPanel";
import MessageBox from "@/components/ui/MessageBox";

export default function NetProvider({ children }: { children: ReactNode }) {
  useNet();
  const router = useRouter();
  const pathname = usePathname();
  const navigateTo = useNetStore((s) => s.navigateTo);
  const loggedIn = useNetStore((s) => s.loggedIn);
  const requiresLogin = pathname === "/game" || pathname === "/spectate";

  useEffect(() => {
    // 在线页面整页刷新后必须先回到登录页，由玩家确认账号。
    // 回放和卡组编辑器是本地功能，不强制登录。
    if (!loggedIn && requiresLogin) {
      router.replace("/home");
    }
  }, [loggedIn, requiresLogin, router]);

  useEffect(() => {
    if (navigateTo) {
      useNetStore.getState().setNavigateTo(null);
      router.push(navigateTo);
    }
  }, [navigateTo, router]);

  return (
    <>
      {!loggedIn && requiresLogin ? <LoginPanel /> : children}
      <MessageBox />
    </>
  );
}
