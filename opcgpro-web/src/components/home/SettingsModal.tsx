"use client";

import Modal from "@/components/ui/Modal";
import {
  LAYOUT_PREVIEW_OPTIONS,
  type LayoutPreviewMode,
} from "./LayoutPreviewFrame";

function LayoutIcon({ mode }: { mode: LayoutPreviewMode }) {
  if (mode === "desktop") {
    return (
      <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
        <rect x="3" y="4" width="18" height="13" rx="2" />
        <path d="M8 21h8M12 17v4" />
      </svg>
    );
  }

  const landscape = mode === "mobile-landscape";
  return (
    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
      <rect
        x={landscape ? 2.5 : 6.5}
        y={landscape ? 6.5 : 2.5}
        width={landscape ? 19 : 11}
        height={landscape ? 11 : 19}
        rx="2"
      />
      <path d={landscape ? "M18.5 12h.01" : "M12 18.5h.01"} />
    </svg>
  );
}

export default function SettingsModal({
  open,
  mode,
  onChange,
  onClose,
}: {
  open: boolean;
  mode: LayoutPreviewMode;
  onChange: (mode: LayoutPreviewMode) => void;
  onClose: () => void;
}) {
  return (
    <Modal open={open} onClose={onClose} title="设置" mobileSheet maxWidthClass="max-w-lg">
      <section aria-labelledby="layout-preview-title">
        <h3 id="layout-preview-title" className="text-sm font-bold text-white">界面布局</h3>
        <p className="mt-1 text-sm leading-5 text-gray-500">切换电脑或手机画布，方便检查不同方向下的界面排版。</p>

        <div className="mt-4 grid grid-cols-1 gap-3 @[640px]:grid-cols-3">
          {LAYOUT_PREVIEW_OPTIONS.map((option) => {
            const active = option.value === mode;
            return (
              <button
                key={option.value}
                type="button"
                aria-pressed={active}
                onClick={() => onChange(option.value)}
                className={`flex min-h-20 items-center gap-3 rounded-xl border px-4 py-3 text-left transition-colors @[640px]:flex-col @[640px]:items-start ${
                  active
                    ? "border-orange-500 bg-orange-500/10 text-orange-300"
                    : "border-gray-700 bg-gray-950/60 text-gray-400 hover:border-gray-500 hover:text-white"
                }`}
              >
                <LayoutIcon mode={option.value} />
                <span>
                  <span className="block text-sm font-bold">{option.label}</span>
                  <span className="mt-0.5 block text-xs text-gray-500">{option.description}</span>
                </span>
              </button>
            );
          })}
        </div>
      </section>
    </Modal>
  );
}
