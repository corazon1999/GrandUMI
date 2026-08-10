"use client";

import { useEffect, useState } from "react";
import { eventBus } from "@/net/eventBus";
import type { MsgBase, MsgGlobalAnnouncement } from "@/types/net";

interface Announcement {
  id: number;
  content: string;
}

export default function GlobalAnnouncementBanner() {
  const [announcement, setAnnouncement] = useState<Announcement | null>(null);

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const handleMessage = (message: MsgBase) => {
      if (message.proto !== "MsgGlobalAnnouncement") return;
      const { content, issuedAt } = message as MsgGlobalAnnouncement;
      if (!content) return;
      setAnnouncement({ id: issuedAt ?? Date.now(), content });
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => setAnnouncement(null), 15_000);
    };
    eventBus.on("message", handleMessage);
    return () => {
      eventBus.off("message", handleMessage);
      if (timer) clearTimeout(timer);
    };
  }, []);

  if (!announcement) return null;

  return (
    <div
      aria-live="polite"
      role="status"
      className="pointer-events-none fixed inset-x-0 top-0 z-[80] pt-[var(--layout-safe-top,env(safe-area-inset-top))]"
    >
      <div className="overflow-hidden border-y border-amber-400/60 bg-gray-950/95 py-2 text-sm font-bold text-amber-100 shadow-lg backdrop-blur">
        <span key={announcement.id} className="global-announcement-marquee">
          📢 全服公告：{announcement.content}
        </span>
      </div>
    </div>
  );
}
