"use client";

import { useState } from "react";
import { useNetStore } from "@/store/netStore";
import LoginPanel from "@/components/home/LoginPanel";
import MainPanel from "@/components/home/MainPanel";
import FeedbackOverlay from "@/components/game/FeedbackOverlay";

export default function HomeClient() {
  const loggedIn = useNetStore((s) => s.loggedIn);
  const [feedbackOpenRequest, setFeedbackOpenRequest] = useState(0);
  return loggedIn ? (
    <>
      <MainPanel onOpenFeedback={() => setFeedbackOpenRequest((value) => value + 1)} />
      <FeedbackOverlay context="lobby" openRequest={feedbackOpenRequest} />
    </>
  ) : (
    <LoginPanel />
  );
}
