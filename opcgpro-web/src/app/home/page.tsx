"use client";

import { useNetStore } from "@/store/netStore";
import LoginPanel from "@/components/home/LoginPanel";
import MainPanel from "@/components/home/MainPanel";

export default function HomePage() {
  const loggedIn = useNetStore((s) => s.loggedIn);
  return loggedIn ? <MainPanel /> : <LoginPanel />;
}
