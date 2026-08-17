"use client";

import { useEffect, useRef, useState } from "react";
import { eventBus } from "@/net/eventBus";
import type { MsgBase, MsgGlobalAnnouncement } from "@/types/net";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";

interface Announcement {
  id: string;
  content: string;
  kind?: "rankedStreak";
}

export default function GlobalAnnouncementBanner() {
  const rotateQuarterTurn = useLayoutQuarterTurn();
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

  const dismissAnnouncements = () => setAnnouncements([]);

  return (
    <div
      ref={bannerRef}
      data-global-announcement-banner
      aria-live="polite"
      role="status"
      className={`pointer-events-none fixed inset-x-0 top-0 z-[80] ${
        rotateQuarterTurn
          ? "pt-[calc(0.5rem+var(--layout-safe-top,env(safe-area-inset-top)))]"
          : "pt-[var(--layout-safe-top,env(safe-area-inset-top))]"
      }`}
    >
      <div className="relative overflow-hidden border-y border-amber-400/60 bg-gray-950/95 py-2 pr-[calc(4rem+var(--layout-safe-right,env(safe-area-inset-right)))] text-sm font-bold text-amber-100 shadow-lg backdrop-blur">
        <span key={announcement.id} className="global-announcement-marquee">
          📢 {announcement.kind === "rankedStreak" ? announcement.content : `全服公告：${announcement.content}`}
        </span>
        <button
          type="button"
          onClick={dismissAnnouncements}
          aria-label="关闭广播横幅"
          title="关闭广播横幅"
          className="pointer-events-auto absolute right-[max(0.5rem,var(--layout-safe-right,env(safe-area-inset-right)))] top-1/2 flex min-h-12 min-w-12 -translate-y-1/2 items-center justify-center rounded-md border border-amber-200/35 bg-gray-900/95 text-lg font-black leading-none text-amber-50 shadow hover:bg-gray-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber-300"
        >
          ×
        </button>
      </div>
    </div>
  );
}
