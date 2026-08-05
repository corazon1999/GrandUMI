"use client";

import { useNetStore } from "@/store/netStore";
import LoginPanel from "@/components/home/LoginPanel";
import MainPanel from "@/components/home/MainPanel";
import FeedbackOverlay from "@/components/game/FeedbackOverlay";

export default function HomePage() {
  const loggedIn = useNetStore((s) => s.loggedIn);
  return loggedIn ? (
    <>
      <MainPanel />
      <FeedbackOverlay context="lobby" />
    </>
  ) : (
    <LoginPanel />
  );
}
