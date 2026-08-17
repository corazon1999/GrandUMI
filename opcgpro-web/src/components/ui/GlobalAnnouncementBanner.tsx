"use client";

import { useEffect, useRef, useState } from "react";
import { eventBus } from "@/net/eventBus";
import type { MsgBase, MsgGlobalAnnouncement } from "@/types/net";

interface Announcement {
  id: string;
  content: string;
  kind?: "rankedStreak";
}

export default function GlobalAnnouncementBanner() {
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);
  const announcement = announcements[0] ?? null;
  const bannerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let sequence = 0;
    const handleMessage = (message: MsgBase) => {
      if (message.proto !== "MsgGlobalAnnouncement") return;
      const { content, issuedAt, kind } = message as MsgGlobalAnnouncement;
      if (!content) return;
      const id = `${issuedAt ?? Date.now()}-${sequence++}`;
      setAnnouncements((current) => [...current, { id, content, kind }]);
    };
    eventBus.on("message", handleMessage);
    return () => eventBus.off("message", handleMessage);
  }, []);

  useEffect(() => {
    if (!announcement) return;
    const timer = setTimeout(() => setAnnouncements((current) => current.slice(1)), 15_000);
    return () => clearTimeout(timer);
  }, [announcement]);

  useEffect(() => {
    if (!announcement || !bannerRef.current) return;
    const banner = bannerRef.current;
    const root = document.documentElement;
    const updateOffset = () => {
      root.style.setProperty("--global-announcement-height", `${Math.ceil(banner.getBoundingClientRect().height)}px`);
    };
    updateOffset();
    const observer = new ResizeObserver(updateOffset);
    observer.observe(banner);
    return () => {
      observer.disconnect();
      root.style.removeProperty("--global-announcement-height");
    };
  }, [announcement]);

  if (!announcement) return null;

  return (
    <div
      ref={bannerRef}
      data-global-announcement-banner
      aria-live="polite"
      role="status"
      className="pointer-events-none fixed inset-x-0 top-0 z-[80] pt-[var(--layout-safe-top,env(safe-area-inset-top))]"
    >
      <div className="overflow-hidden border-y border-amber-400/60 bg-gray-950/95 py-2 text-sm font-bold text-amber-100 shadow-lg backdrop-blur">
        <span key={announcement.id} className="global-announcement-marquee">
          📢 {announcement.kind === "rankedStreak" ? announcement.content : `全服公告：${announcement.content}`}
        </span>
      </div>
    </div>
  );
}
