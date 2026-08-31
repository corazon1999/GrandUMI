"use client";

import { useCallback, useEffect, useState } from "react";
import Modal from "@/components/ui/Modal";
import GameOverlayPortal from "@/components/ui/GameOverlayPortal";
import { useLanguage } from "@/i18n/LanguageProvider";

type WebkitFullscreenDocument = Document & {
  webkitFullscreenElement?: Element | null;
  webkitExitFullscreen?: () => Promise<void> | void;
};

type WebkitFullscreenElement = HTMLElement & {
  webkitRequestFullscreen?: () => Promise<void> | void;
};

type StandaloneNavigator = Navigator & { standalone?: boolean };

function currentFullscreenElement() {
  const fullscreenDocument = document as WebkitFullscreenDocument;
  return document.fullscreenElement ?? fullscreenDocument.webkitFullscreenElement ?? null;
}

function FullscreenIcon({ active }: { active: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className="h-5 w-5"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {active ? (
        <>
          <path d="M8 3v5H3" />
          <path d="M16 3v5h5" />
          <path d="M8 21v-5H3" />
          <path d="M16 21v-5h5" />
        </>
      ) : (
        <>
          <path d="M8 3H3v5" />
          <path d="M16 3h5v5" />
          <path d="M8 21H3v-5" />
          <path d="M16 21h5v-5" />
        </>
      )}
    </svg>
  );
}

export default function MobileFullscreenButton() {
  const { t } = useLanguage();
  const [mounted, setMounted] = useState(false);
  const [standalone, setStandalone] = useState(false);
  const [fullscreen, setFullscreen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);

  useEffect(() => {
    const standaloneQuery = window.matchMedia("(display-mode: standalone), (display-mode: fullscreen)");
    const updateStandalone = () => {
      setStandalone(standaloneQuery.matches || Boolean((navigator as StandaloneNavigator).standalone));
    };
    const updateFullscreen = () => setFullscreen(Boolean(currentFullscreenElement()));

    setMounted(true);
    updateStandalone();
    updateFullscreen();
    standaloneQuery.addEventListener("change", updateStandalone);
    document.addEventListener("fullscreenchange", updateFullscreen);
    document.addEventListener("webkitfullscreenchange", updateFullscreen);
    return () => {
      standaloneQuery.removeEventListener("change", updateStandalone);
      document.removeEventListener("fullscreenchange", updateFullscreen);
      document.removeEventListener("webkitfullscreenchange", updateFullscreen);
    };
  }, []);

  const toggleFullscreen = useCallback(async () => {
    const fullscreenDocument = document as WebkitFullscreenDocument;
    const root = document.documentElement as WebkitFullscreenElement;

    try {
      if (currentFullscreenElement()) {
        if (document.exitFullscreen) await document.exitFullscreen();
        else await fullscreenDocument.webkitExitFullscreen?.();
        return;
      }

      if (root.requestFullscreen) await root.requestFullscreen();
      else if (root.webkitRequestFullscreen) await root.webkitRequestFullscreen();
      else setHelpOpen(true);
    } catch {
      setHelpOpen(true);
    }
  }, []);

  const closeHelp = useCallback(() => setHelpOpen(false), []);

  if (!mounted || standalone) return null;

  const label = fullscreen ? t("退出全屏") : t("进入全屏");

  return (
    <GameOverlayPortal>
      {!helpOpen && (
        <button
          type="button"
          onClick={toggleFullscreen}
          aria-label={label}
          title={label}
          style={{
            right: "calc(0.625rem + var(--layout-safe-right, env(safe-area-inset-right)))",
            top: "calc(3.875rem + var(--layout-safe-top, env(safe-area-inset-top)))",
          }}
          className="pointer-events-auto fixed z-[10000] flex h-12 w-12 items-center justify-center rounded-xl border border-gray-700/80 bg-gray-900/90 text-gray-300 shadow-lg backdrop-blur-md transition-colors hover:border-sky-400 hover:bg-gray-800 hover:text-white active:bg-gray-700 focus-visible:outline-2 focus-visible:outline-sky-400"
        >
          <FullscreenIcon active={fullscreen} />
        </button>
      )}

      <Modal
        open={helpOpen}
        onClose={closeHelp}
        title={t("全屏显示")}
        maxWidthClass="max-w-md"
        layerClassName="z-[11000]"
      >
        <div className="space-y-3 text-sm leading-6 text-gray-200">
          <p>{t("当前 iPhone Safari 不支持网页按钮直接进入全屏。")}</p>
          <ol className="list-decimal space-y-1 pl-5 text-gray-300">
            <li>{t("点击 Safari 底部的分享按钮。")}</li>
            <li>{t("选择“添加到主屏幕”。")}</li>
            <li>{t("从桌面的 GrandUMI 图标打开，即可隐藏浏览器栏。")}</li>
          </ol>
          <button
            type="button"
            onClick={closeHelp}
            className="mt-4 flex min-h-12 w-full items-center justify-center rounded-xl bg-sky-600 px-4 font-bold text-white transition-colors hover:bg-sky-500 active:bg-sky-700 focus-visible:outline-2 focus-visible:outline-sky-300"
          >
            {t("我知道了")}
          </button>
        </div>
      </Modal>
    </GameOverlayPortal>
  );
}
