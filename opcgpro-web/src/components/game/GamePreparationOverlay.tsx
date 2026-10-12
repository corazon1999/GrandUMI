"use client";

/** 开局资料失败时允许重试；准备等待沿用对局画布的旋转安全区。 */
export default function GamePreparationOverlay({ error, onRetry }: {
  error?: string | null;
  onRetry?: () => void;
}) {
  return (
    <div data-game-preparation={error ? "failed" : "waiting"}
      className={`absolute inset-0 flex items-center justify-center ${error ? "z-[160] bg-slate-950/85" : "z-[140] bg-slate-950/70"}`}
      style={{ padding: "max(1rem, var(--layout-safe-top, 0px)) max(1rem, var(--layout-safe-right, 0px)) max(1rem, var(--layout-safe-bottom, 0px)) max(1rem, var(--layout-safe-left, 0px))" }}>
      <div className="flex max-w-sm flex-col items-center gap-4 rounded-2xl border border-white/15 bg-slate-900 p-5 text-center text-white">
        <p role={error ? "alert" : "status"}>{error || "正在等待双方准备完成…"}</p>
        {error && <button className="min-h-12 min-w-32 rounded-xl bg-cyan-600 px-5 py-3 font-semibold" onClick={onRetry}>重新加载</button>}
      </div>
    </div>
  );
}
