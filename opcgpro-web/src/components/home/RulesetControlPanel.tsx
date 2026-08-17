"use client";

import { useEffect, useState } from "react";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { showMessage } from "@/components/ui/MessageBox";

export default function RulesetControlPanel() {
  const connState = useNetStore((state) => state.connState);
  const rulesets = useNetStore((state) => state.rulesets);
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    if (connState === "connected") HomeRequest.requestRulesetState();
  }, [connState]);

  const refresh = () => {
    if (!HomeRequest.requestRulesetState()) {
      showMessage("服务器未连接，无法刷新规则包", "error");
    }
  };

  const activate = (rulesetId: string) => {
    setPending(true);
    setConfirmingId(null);
    if (!HomeRequest.activateRuleset(rulesetId)) {
      setPending(false);
      showMessage("服务器未连接，规则版本没有改变", "error");
      return;
    }
    window.setTimeout(() => setPending(false), 1_500);
  };

  return (
    <section
      aria-label="卡效热更新"
      className="rounded-2xl border border-cyan-800/70 bg-cyan-950/20 p-3 @[640px]:p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h2 className="text-sm font-black text-cyan-100">卡效热更新</h2>
          <p className="mt-1 break-all text-xs leading-5 text-cyan-200/70">
            当前新对局版本：{rulesets.activeRulesetId || "读取中…"}
          </p>
        </div>
        <button
          type="button"
          disabled={connState !== "connected" || pending}
          onClick={refresh}
          className="min-h-11 shrink-0 rounded-xl border border-cyan-700 bg-cyan-950/40 px-4 text-sm font-bold text-cyan-200 hover:bg-cyan-900/50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          刷新规则包
        </button>
      </div>

      <div className="mt-3 grid gap-2">
        {rulesets.availableRulesets.map((ruleset) => {
          const roomCount = Math.max(0, rulesets.activeRoomCounts[ruleset.id] ?? 0);
          const confirming = confirmingId === ruleset.id;
          return (
            <article
              key={ruleset.id}
              className={`rounded-xl border p-3 ${ruleset.active ? "border-emerald-700/70 bg-emerald-950/20" : "border-gray-800 bg-gray-950/70"}`}
            >
              <div className="flex flex-col gap-3 @[520px]:flex-row @[520px]:items-center">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="break-all text-sm font-black text-white">{ruleset.id}</p>
                    {ruleset.active && <span className="rounded-full bg-emerald-800/70 px-2 py-0.5 text-[11px] font-bold text-emerald-100">新局启用中</span>}
                    {roomCount > 0 && <span className="rounded-full bg-amber-900/70 px-2 py-0.5 text-[11px] font-bold text-amber-200">进行中 {roomCount} 局</span>}
                  </div>
                  <p className="mt-1 text-xs leading-5 text-gray-400">{ruleset.description || "无版本说明"}</p>
                  {ruleset.changedCards.length > 0 && (
                    <p className="mt-1 break-words text-xs leading-5 text-gray-500">涉及卡牌：{ruleset.changedCards.join("、")}</p>
                  )}
                </div>

                {!ruleset.active && (confirming ? (
                  <div className="grid shrink-0 grid-cols-2 gap-2">
                    <button type="button" onClick={() => setConfirmingId(null)} className="min-h-11 rounded-xl bg-gray-800 px-3 text-sm font-bold text-gray-200 hover:bg-gray-700">取消</button>
                    <button type="button" disabled={pending} onClick={() => activate(ruleset.id)} className="min-h-11 rounded-xl bg-cyan-600 px-3 text-sm font-black text-white hover:bg-cyan-500 disabled:opacity-50">确认激活</button>
                  </div>
                ) : (
                  <button
                    type="button"
                    disabled={pending || connState !== "connected"}
                    onClick={() => setConfirmingId(ruleset.id)}
                    className="min-h-11 shrink-0 rounded-xl bg-cyan-700 px-4 text-sm font-black text-white hover:bg-cyan-600 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    激活给新对局
                  </button>
                ))}
              </div>
              {confirming && (
                <p className="mt-2 text-xs leading-5 text-amber-200">
                  激活后只影响新建对局；当前所有进行中对局继续使用各自锁定的旧版本。
                </p>
              )}
            </article>
          );
        })}
      </div>

      {rulesets.availableRulesets.length === 0 && (
        <p className="mt-3 rounded-xl border border-dashed border-gray-700 px-3 py-4 text-center text-xs leading-5 text-gray-500">
          暂未读取到规则版本。将完整规则包放入服务端 Rulesets 目录后点击刷新。
        </p>
      )}
    </section>
  );
}
