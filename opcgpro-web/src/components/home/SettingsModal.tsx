"use client";

import Modal from "@/components/ui/Modal";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";
import { useAudio } from "@/hooks/useAudio";
import { useAudioStore } from "@/store/audioStore";
import {
  useSettingsStore,
  type AnimationSpeed,
  type CardSizePreference,
} from "@/store/settingsStore";
import { useLanguage, type Locale } from "@/i18n/LanguageProvider";
import { LANGUAGE_OPTIONS } from "@/i18n/core.mjs";
import {
  LAYOUT_PREVIEW_OPTIONS,
  type SelectableLayoutPreviewMode,
} from "./LayoutPreviewFrame";

function LayoutIcon({ mode }: { mode: SelectableLayoutPreviewMode }) {
  if (mode === "desktop") {
    return (
      <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
        <rect x="3" y="4" width="18" height="13" rx="2" />
        <path d="M8 21h8M12 17v4" />
      </svg>
    );
  }

  return (
    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
      <rect
        x="6.5"
        y="2.5"
        width="11"
        height="19"
        rx="2"
      />
      <path d="M12 18.5h.01" />
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
  mode: SelectableLayoutPreviewMode;
  onChange: (mode: SelectableLayoutPreviewMode) => void;
  onClose: () => void;
}) {
  const { play, unlock } = useAudio();
  const { locale, setLocale } = useLanguage();
  const sfxVolume = useAudioStore((state) => state.sfxVolume);
  const isMuted = useAudioStore((state) => state.isMuted);
  const isHydrated = useAudioStore((state) => state.isHydrated);
  const isUnlocked = useAudioStore((state) => state.isUnlocked);
  const setSfxVolume = useAudioStore((state) => state.setSfxVolume);
  const toggleMute = useAudioStore((state) => state.toggleMute);
  const setUnlocked = useAudioStore((state) => state.setUnlocked);
  const volumePercent = Math.round(sfxVolume * 100);
  const cardSize = useSettingsStore((state) => state.cardSize);
  const animationSpeed = useSettingsStore((state) => state.animationSpeed);
  const confirmAttachDon = useSettingsStore((state) => state.confirmAttachDon);
  const hideOpponentCardBack = useSettingsStore((state) => state.hideOpponentCardBack);
  const setCardSize = useSettingsStore((state) => state.setCardSize);
  const setAnimationSpeed = useSettingsStore((state) => state.setAnimationSpeed);
  const setConfirmAttachDon = useSettingsStore((state) => state.setConfirmAttachDon);
  const setHideOpponentCardBack = useSettingsStore((state) => state.setHideOpponentCardBack);
  const compactLandscape = useLayoutQuarterTurn();
  const compactSectionClass = compactLandscape
    ? "rounded-xl border border-gray-800 bg-gray-950/40 p-3"
    : "";
  const dividedSectionClass = compactLandscape
    ? compactSectionClass
    : "mt-6 border-t border-gray-800 pt-5";

  const testSound = async () => {
    const unlocked = isUnlocked || (await unlock());
    setUnlocked(unlocked);
    if (unlocked) play("message");
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="设置"
      mobileSheet
      maxWidthClass={compactLandscape ? "max-w-[52rem]" : "max-w-lg"}
    >
      <div
        data-settings-layout={compactLandscape ? "landscape-compact" : "stacked"}
        className={compactLandscape ? "grid grid-cols-2 items-start gap-3" : ""}
      >
      <section
        className={compactLandscape ? `${compactSectionClass} order-4` : compactSectionClass}
        aria-labelledby="language-settings-title"
      >
        <h3 id="language-settings-title" className="text-sm font-bold text-white">界面语言</h3>
        <p className="mt-1 text-sm leading-5 text-gray-500">切换后立即应用，并保存在当前浏览器。</p>
        <div className="mt-4 grid grid-cols-3 gap-2" data-no-i18n>
          {LANGUAGE_OPTIONS.map((option) => {
            const active = option.value === locale;
            return (
              <button
                key={option.value}
                type="button"
                lang={option.value}
                aria-pressed={active}
                onClick={() => setLocale(option.value as Locale)}
                className={`min-h-12 rounded-xl border px-2 text-sm font-bold transition-colors ${
                  active
                    ? "border-orange-500 bg-orange-500/10 text-orange-200"
                    : "border-gray-700 bg-gray-950/60 text-gray-400 hover:border-gray-500 hover:text-white"
                }`}
              >
                {option.label}
              </button>
            );
          })}
        </div>
      </section>

      <section
        className={compactLandscape ? `${compactSectionClass} order-4` : dividedSectionClass}
        aria-labelledby="layout-preview-title"
      >
        <h3 id="layout-preview-title" className="text-sm font-bold text-white">界面布局</h3>
        <p className="mt-1 text-sm leading-5 text-gray-500">手机竖屏模式下大厅保持竖屏，对局与回放会自动旋转为横屏，无需切换系统方向。</p>

        <div className="mt-4 grid grid-cols-1 gap-3 @[640px]:grid-cols-2">
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

      <section
        className={compactLandscape ? `${compactSectionClass} order-1` : dividedSectionClass}
        aria-labelledby="card-display-settings-title"
      >
        <h3 id="card-display-settings-title" className="text-sm font-bold text-white">卡牌显示</h3>
        <p className="mt-1 text-sm leading-5 text-gray-500">调整牌桌卡牌大小，设置会保存在当前浏览器。</p>
        <div className="mt-4 grid grid-cols-2 gap-2 @[640px]:grid-cols-4">
          {([
            ["auto", "自动"],
            ["sm", "小"],
            ["md", "中"],
            ["lg", "大"],
          ] as Array<[CardSizePreference, string]>).map(([value, label]) => (
            <button
              key={value}
              type="button"
              aria-pressed={cardSize === value}
              onClick={() => setCardSize(value)}
              className={`min-h-12 rounded-xl border px-3 text-sm font-bold transition-colors ${
                cardSize === value
                  ? "border-orange-500 bg-orange-500/10 text-orange-200"
                  : "border-gray-700 bg-gray-950/60 text-gray-400 hover:border-gray-500 hover:text-white"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
        <div
          data-opponent-card-back-setting
          className="mt-4 flex flex-col items-stretch gap-3 rounded-xl border border-gray-800 bg-gray-950/60 p-3 @[420px]:flex-row @[420px]:items-center @[420px]:justify-between"
        >
          <div>
            <h4 id="hide-opponent-card-back-title" className="text-sm font-bold text-gray-200">屏蔽对手卡背</h4>
            <p id="hide-opponent-card-back-description" className="mt-1 text-xs leading-5 text-gray-500">
              开启后，敌方手牌、牌库、生命区等背面牌统一显示为经典卡背；不影响我方卡背和公开牌。
            </p>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={hideOpponentCardBack}
            aria-labelledby="hide-opponent-card-back-title"
            aria-describedby="hide-opponent-card-back-description"
            onClick={() => setHideOpponentCardBack(!hideOpponentCardBack)}
            className={`flex min-h-12 min-w-12 w-full shrink-0 items-center justify-center gap-2 rounded-xl border px-3 text-sm font-bold transition-colors @[420px]:w-auto ${
              hideOpponentCardBack
                ? "border-emerald-400/50 bg-emerald-500/10 text-emerald-200 hover:bg-emerald-500/20"
                : "border-gray-700 bg-gray-950 text-gray-400 hover:border-gray-500"
            }`}
          >
            <span>{hideOpponentCardBack ? "屏蔽开启" : "屏蔽关闭"}</span>
            <span
              aria-hidden="true"
              className={`relative h-6 w-11 rounded-full transition-colors ${hideOpponentCardBack ? "bg-emerald-500" : "bg-gray-700"}`}
            >
              <span
                className={`absolute top-1 h-4 w-4 rounded-full bg-white shadow transition-transform ${hideOpponentCardBack ? "translate-x-6" : "translate-x-1"}`}
              />
            </span>
          </button>
        </div>
      </section>

      <section
        className={compactLandscape ? `${compactSectionClass} order-1` : dividedSectionClass}
        aria-labelledby="animation-settings-title"
      >
        <h3 id="animation-settings-title" className="text-sm font-bold text-white">对局动画</h3>
        <p className="mt-1 text-sm leading-5 text-gray-500">关闭时会跳过纯视觉特效，必要的卡牌公开与对局结果仍会显示。</p>
        <div className="mt-4 grid grid-cols-3 gap-2">
          {([
            ["off", "关闭"],
            ["fast", "快速"],
            ["standard", "标准"],
          ] as Array<[AnimationSpeed, string]>).map(([value, label]) => (
            <button
              key={value}
              type="button"
              aria-pressed={animationSpeed === value}
              onClick={() => setAnimationSpeed(value)}
              className={`min-h-12 rounded-xl border px-3 text-sm font-bold transition-colors ${
                animationSpeed === value
                  ? "border-orange-500 bg-orange-500/10 text-orange-200"
                  : "border-gray-700 bg-gray-950/60 text-gray-400 hover:border-gray-500 hover:text-white"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </section>

      <section
        className={compactLandscape ? `${compactSectionClass} order-2 col-span-2` : dividedSectionClass}
        aria-labelledby="attach-don-settings-title"
      >
        <div className="flex items-center justify-between gap-4">
          <div>
            <h3 id="attach-don-settings-title" className="text-sm font-bold text-white">贴咚操作</h3>
            <p className="mt-1 text-sm leading-5 text-gray-500">
              此设置默认关闭；开启后会先询问确认，关闭时点选数量即提交。贴咚会立即生效，执行其他对局操作前仍可撤回。
            </p>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={confirmAttachDon}
            onClick={() => setConfirmAttachDon(!confirmAttachDon)}
            className={`flex min-h-12 shrink-0 items-center gap-2 rounded-xl border px-3 text-sm font-bold transition-colors ${
              confirmAttachDon
                ? "border-emerald-400/50 bg-emerald-500/10 text-emerald-200 hover:bg-emerald-500/20"
                : "border-gray-700 bg-gray-950 text-gray-400 hover:border-gray-500"
            }`}
          >
            <span>{confirmAttachDon ? "确认开启" : "确认关闭"}</span>
            <span
              aria-hidden="true"
              className={`relative h-6 w-11 rounded-full transition-colors ${confirmAttachDon ? "bg-emerald-500" : "bg-gray-700"}`}
            >
              <span
                className={`absolute top-1 h-4 w-4 rounded-full bg-white shadow transition-transform ${confirmAttachDon ? "translate-x-6" : "translate-x-1"}`}
              />
            </span>
          </button>
        </div>
      </section>

      <section
        className={compactLandscape ? `${compactSectionClass} order-3 col-span-2` : dividedSectionClass}
        aria-labelledby="audio-settings-title"
      >
        <div className={compactLandscape ? "grid grid-cols-2 items-start gap-3" : ""}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 id="audio-settings-title" className="text-sm font-bold text-white">游戏音效</h3>
            <p className="mt-1 text-sm leading-5 text-gray-500">控制出牌、战斗、回合和系统提示音，设置会保存在当前浏览器。</p>
          </div>
          <button
            type="button"
            aria-pressed={!isMuted}
            onClick={toggleMute}
            disabled={!isHydrated}
            className={`min-h-12 shrink-0 rounded-lg border px-3 text-sm font-bold transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
              isMuted
                ? "border-gray-700 bg-gray-950 text-gray-400 hover:border-gray-500"
                : "border-emerald-400/50 bg-emerald-500/10 text-emerald-200 hover:bg-emerald-500/20"
            }`}
          >
            {isMuted ? "已静音" : "音效开启"}
          </button>
        </div>

        <div className={`${compactLandscape ? "" : "mt-5"} rounded-xl border border-gray-800 bg-gray-950/60 p-4`}>
          <div className="flex items-center justify-between gap-4">
            <label htmlFor="sfx-volume" className="text-sm font-bold text-gray-200">音效音量</label>
            <span className="font-mono text-sm tabular-nums text-orange-300">{volumePercent}%</span>
          </div>
          <input
            id="sfx-volume"
            type="range"
            min={0}
            max={100}
            step={1}
            value={volumePercent}
            disabled={!isHydrated}
            onChange={(event) => setSfxVolume(Number(event.target.value) / 100)}
            aria-valuetext={`${volumePercent}%`}
            className="mt-3 h-2 w-full cursor-pointer accent-orange-500 disabled:cursor-not-allowed disabled:opacity-50"
          />
          <div className="mt-4 flex items-center justify-between gap-3">
            <p className="text-xs text-gray-500">
              {isUnlocked ? "音频已就绪" : "首次点击或按键后，浏览器才允许播放声音"}
            </p>
            <button
              type="button"
              onClick={() => void testSound()}
              disabled={!isHydrated || isMuted || volumePercent === 0}
              className="min-h-12 shrink-0 rounded-lg bg-orange-500 px-4 text-sm font-bold text-white transition-colors hover:bg-orange-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600"
            >
              试听
            </button>
          </div>
        </div>
        </div>
      </section>
      </div>
    </Modal>
  );
}
