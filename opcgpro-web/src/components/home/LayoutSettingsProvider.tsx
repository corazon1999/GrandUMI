"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import SettingsModal from "./SettingsModal";
import {
  useLayoutPreviewMode,
  type SelectableLayoutPreviewMode,
} from "./LayoutPreviewFrame";

interface LayoutSettingsContextValue {
  mode: SelectableLayoutPreviewMode;
  setMode: (mode: SelectableLayoutPreviewMode) => void;
  openSettings: () => void;
  gameOverlayHost: HTMLDivElement | null;
  setGameOverlayHost: (host: HTMLDivElement | null) => void;
}

const LayoutSettingsContext = createContext<LayoutSettingsContextValue | null>(null);

export function useLayoutSettings(): LayoutSettingsContextValue {
  const value = useContext(LayoutSettingsContext);
  if (!value) {
    throw new Error("布局设置必须在 LayoutSettingsProvider 内使用");
  }
  return value;
}

export default function LayoutSettingsProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useLayoutPreviewMode();
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [gameOverlayHost, setGameOverlayHostState] = useState<HTMLDivElement | null>(null);
  const openSettings = useCallback(() => setSettingsOpen(true), []);
  const setGameOverlayHost = useCallback((host: HTMLDivElement | null) => {
    setGameOverlayHostState(host);
  }, []);
  const contextValue = useMemo(
    () => ({ mode, setMode, openSettings, gameOverlayHost, setGameOverlayHost }),
    [gameOverlayHost, mode, openSettings, setGameOverlayHost, setMode],
  );

  const settingsUi = (
    <>
      {!settingsOpen && (
        <button
          type="button"
          onClick={openSettings}
          aria-label="打开设置"
          title="设置"
          style={{
            right: "calc(0.625rem + var(--layout-safe-right, env(safe-area-inset-right)))",
            top: "calc(0.625rem + var(--layout-safe-top, env(safe-area-inset-top)))",
          }}
          className="pointer-events-auto fixed z-[10000] flex h-11 w-11 items-center justify-center rounded-xl border border-gray-700/80 bg-gray-900/90 text-gray-300 shadow-lg backdrop-blur-md transition-colors hover:border-orange-500 hover:bg-gray-800 hover:text-white active:bg-gray-700 focus-visible:outline-2 focus-visible:outline-orange-400"
        >
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
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1a1.7 1.7 0 0 0 1.9.3A1.7 1.7 0 0 0 10 3V2.8h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z" />
          </svg>
        </button>
      )}
      <div className="pointer-events-auto contents">
        <SettingsModal
          open={settingsOpen}
          mode={mode}
          onChange={setMode}
          onClose={() => setSettingsOpen(false)}
        />
      </div>
    </>
  );

  return (
    <LayoutSettingsContext.Provider value={contextValue}>
      {children}
      {gameOverlayHost ? createPortal(settingsUi, gameOverlayHost) : settingsUi}
    </LayoutSettingsContext.Provider>
  );
}
