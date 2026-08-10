"use client";

import { useEffect, useState } from "react";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import type { SpectateMode } from "@/types/net";

const MODES: Array<{ value: SpectateMode; label: string; description: string }> = [
  { value: "open", label: "自由观战", description: "所有在线玩家都能进入" },
  { value: "closed", label: "不可观战", description: "本局不接受任何观战者" },
  { value: "friends", label: "仅好友观战", description: "只有已添加的好友能进入" },
  { value: "password", label: "密码观战", description: "输入随机观战码后才能进入" },
];

const STORAGE_KEY = "grandumi_spectate_settings";

export default function SpectateSettingsPanel({ locked }: { locked: boolean }) {
  const connState = useNetStore((state) => state.connState);
  const mode = useNetStore((state) => state.spectateMode);
  const handsPublic = useNetStore((state) => state.spectatorHandsPublic);
  const spectateCode = useNetStore((state) => state.spectateCode);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (connState !== "connected") return;
    let savedMode: SpectateMode = "open";
    let savedHandsPublic = false;
    try {
      const parsed = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "{}") as { mode?: string; handsPublic?: boolean };
      if (MODES.some((item) => item.value === parsed.mode)) savedMode = parsed.mode as SpectateMode;
      savedHandsPublic = parsed.handsPublic === true;
    } catch { /* 使用安全默认值 */ }
    HomeRequest.updateSpectateSettings(savedMode, savedHandsPublic);
  }, [connState]);

  const update = (nextMode: SpectateMode, nextHandsPublic: boolean, regenerateCode = false) => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ mode: nextMode, handsPublic: nextHandsPublic }));
    HomeRequest.updateSpectateSettings(nextMode, nextHandsPublic, regenerateCode);
  };

  const copyCode = async () => {
    if (!spectateCode) return;
    try {
      await navigator.clipboard.writeText(spectateCode);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1800);
    } catch { /* 浏览器拒绝剪贴板时仍可手动选择 */ }
  };

  return (
    <section className="rounded-2xl border border-gray-800 bg-gray-900 p-4 @[640px]:p-5" data-testid="spectate-settings">
      <div>
        <h2 className="font-bold text-white">观战设置</h2>
        <p className="mt-1 text-sm leading-5 text-gray-500">设置会在进入匹配或房间时锁定，并对本局生效。</p>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2" aria-label="观战权限">
        {MODES.map((item) => (
          <button
            key={item.value}
            type="button"
            disabled={locked}
            aria-pressed={mode === item.value}
            onClick={() => update(item.value, handsPublic, item.value === "password" && mode !== "password")}
            className={`min-h-16 rounded-xl border px-3 py-2 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
              mode === item.value
                ? "border-purple-500 bg-purple-950/70 text-white"
                : "border-gray-800 bg-gray-950/70 text-gray-400 hover:border-gray-700 hover:text-gray-200"
            }`}
          >
            <span className="block text-sm font-black">{item.label}</span>
            <span className="mt-1 block text-[11px] leading-4 text-gray-500">{item.description}</span>
          </button>
        ))}
      </div>

      {mode === "password" && (
        <div className="mt-3 flex flex-wrap items-center gap-2 rounded-xl border border-purple-800/60 bg-purple-950/30 p-3">
          <div className="min-w-0 flex-1">
            <p className="text-[11px] font-bold text-purple-300">本次观战码</p>
            <p className="select-all font-mono text-2xl font-black tracking-[0.22em] text-white">{spectateCode ?? "生成中…"}</p>
          </div>
          <button type="button" onClick={copyCode} disabled={!spectateCode} className="min-h-11 rounded-lg bg-purple-600 px-3 text-xs font-bold text-white hover:bg-purple-500 disabled:opacity-50">
            {copied ? "已复制" : "复制"}
          </button>
          <button type="button" onClick={() => update(mode, handsPublic, true)} disabled={locked} className="min-h-11 rounded-lg border border-purple-700 px-3 text-xs font-bold text-purple-200 hover:bg-purple-900/60 disabled:opacity-50">
            换一个
          </button>
        </div>
      )}

      <div className="mt-4">
        <p className="mb-2 text-sm font-bold text-gray-300">观战者默认看到你的手牌</p>
        <div className="grid grid-cols-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="手牌公开设置">
          <button type="button" disabled={locked} aria-pressed={!handsPublic} onClick={() => update(mode, false)} className={`min-h-11 rounded-lg px-3 text-sm font-bold ${!handsPublic ? "bg-gray-700 text-white" : "text-gray-500 hover:text-gray-200"}`}>
            默认不公开
          </button>
          <button type="button" disabled={locked} aria-pressed={handsPublic} onClick={() => update(mode, true)} className={`min-h-11 rounded-lg px-3 text-sm font-bold ${handsPublic ? "bg-emerald-700 text-white" : "text-gray-500 hover:text-gray-200"}`}>
            默认公开
          </button>
        </div>
        {!handsPublic && <p className="mt-2 text-xs leading-5 text-gray-500">观战者可申请查看主视角手牌；只有你同意后才会向该观战者公开。</p>}
      </div>
    </section>
  );
}
