"use client";

import { useEffect, useState } from "react";
import CardBack from "@/components/ui/CardBack";
import { showMessage } from "@/components/ui/MessageBox";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";

export const DEFAULT_CARD_BACK_REJECTION_REASON =
  "投稿未通过：禁止使用真人人像、个人照片、违法违规、色情暴力、仇恨歧视、侵权盗用或其他不适合公开展示的内容。请调整后重新投稿。";

export default function CardBackReviewPanel() {
  const queue = useNetStore((state) => state.cardBackReviewQueue);
  const connState = useNetStore((state) => state.connState);
  const canReview = useNetStore((state) => state.maintenance.canManage);
  const [rejectionReason, setRejectionReason] = useState(DEFAULT_CARD_BACK_REJECTION_REASON);
  const [processingId, setProcessingId] = useState<string | null>(null);

  useEffect(() => {
    if (connState === "connected" && canReview) HomeRequest.requestCardBackReviewQueue();
  }, [canReview, connState]);

  useEffect(() => setProcessingId(null), [queue]);

  const review = (cardBackId: string, approved: boolean) => {
    if (!approved && !rejectionReason.trim()) {
      showMessage("请填写未通过理由", "error");
      return;
    }
    setProcessingId(cardBackId);
    if (!HomeRequest.reviewCardBack(cardBackId, approved, approved ? undefined : rejectionReason.trim())) {
      setProcessingId(null);
      showMessage("网络未连接，审核结果未提交", "error");
    }
  };

  if (!canReview) {
    return (
      <section className="flex h-full items-center justify-center p-5" data-testid="card-back-review">
        <div className="w-full max-w-lg rounded-2xl border border-red-800/60 bg-red-950/20 px-5 py-12 text-center">
          <h1 className="text-xl font-black text-white">卡背审核</h1>
          <p className="mt-2 text-sm text-red-300">当前账号没有卡背审核权限。</p>
        </div>
      </section>
    );
  }

  return (
    <section className="h-full overflow-y-auto px-4 py-5 @[720px]:px-6 @[720px]:py-6" data-testid="card-back-review">
      <header className="flex flex-col gap-3 @[640px]:flex-row @[640px]:items-end @[640px]:justify-between">
        <div>
          <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-400">Administrator Review</p>
          <h1 className="mt-1 text-2xl font-black text-white">卡背审核</h1>
          <p className="mt-1 text-sm text-gray-500">审核玩家新投稿；只有通过审核的卡背才会进入公开广场。</p>
        </div>
        <button
          type="button"
          onClick={() => HomeRequest.requestCardBackReviewQueue()}
          disabled={connState !== "connected"}
          className="min-h-11 rounded-xl border border-amber-700 px-4 text-sm font-bold text-amber-200 hover:border-amber-400 disabled:border-gray-800 disabled:text-gray-600"
        >
          刷新审核队列
        </button>
      </header>

      <article className="mt-5 rounded-2xl border border-amber-800/60 bg-amber-950/20 p-4 @[720px]:p-5">
        <div className="flex flex-col gap-1 @[640px]:flex-row @[640px]:items-center @[640px]:justify-between">
          <h2 className="font-black text-amber-100">未通过默认理由</h2>
          <span className="text-xs text-amber-400/70">拒绝投稿时可按实际情况编辑</span>
        </div>
        <textarea
          aria-label="卡背未通过理由"
          value={rejectionReason}
          onChange={(event) => setRejectionReason(event.target.value)}
          maxLength={300}
          rows={4}
          className="mt-3 min-h-28 w-full resize-y rounded-xl border border-amber-900/80 bg-gray-950 px-3 py-3 text-sm leading-6 text-white outline-none placeholder:text-gray-600 focus:border-amber-400"
        />
        <p className="mt-2 text-right text-xs text-gray-600">{rejectionReason.length}/300</p>
      </article>

      <div className="mt-6 flex items-center justify-between gap-3">
        <h2 className="font-black text-white">待审核投稿</h2>
        <span className="rounded-full border border-gray-800 bg-gray-900 px-3 py-1 text-xs text-gray-400">
          {queue?.length ?? 0} 款
        </span>
      </div>

      {queue === null ? (
        <div className="mt-4 rounded-2xl border border-gray-800 bg-gray-900 py-16 text-center text-sm text-gray-500">正在读取审核队列…</div>
      ) : queue.length === 0 ? (
        <div className="mt-4 rounded-2xl border border-dashed border-emerald-800/70 bg-emerald-950/10 py-16 text-center text-sm text-emerald-300">当前没有待审核卡背。</div>
      ) : (
        <div className="mt-4 grid grid-cols-1 gap-4 @[520px]:grid-cols-2 @[820px]:grid-cols-3 @[1120px]:grid-cols-4">
          {queue.map((item) => {
            const processing = processingId === item.id;
            return (
              <article key={item.id} className="overflow-hidden rounded-2xl border border-gray-800 bg-gray-900 p-3">
                <div className="relative mx-auto aspect-[5/7] w-full max-w-52 overflow-hidden rounded-xl bg-gray-950 shadow-xl">
                  <CardBack cardBackId={item.id} decorative />
                  <span className="absolute left-2 top-2 rounded-full bg-amber-500 px-2 py-1 text-[10px] font-black text-gray-950">待审核</span>
                </div>
                <h3 className="mt-3 truncate text-sm font-bold text-white" title={item.name}>{item.name}</h3>
                <p className="mt-1 truncate text-xs text-gray-500">投稿者：{item.authorName}</p>
                <p className="mt-1 text-[11px] text-gray-600">{new Date(item.createdAt).toLocaleString()}</p>
                <div className="mt-3 grid grid-cols-2 gap-2">
                  <button
                    type="button"
                    onClick={() => review(item.id, true)}
                    disabled={processingId !== null}
                    className="min-h-11 rounded-xl bg-emerald-600 px-3 text-sm font-black text-white hover:bg-emerald-500 disabled:bg-gray-800 disabled:text-gray-600"
                  >
                    {processing ? "提交中…" : "通过"}
                  </button>
                  <button
                    type="button"
                    onClick={() => review(item.id, false)}
                    disabled={processingId !== null || !rejectionReason.trim()}
                    className="min-h-11 rounded-xl bg-red-600 px-3 text-sm font-black text-white hover:bg-red-500 disabled:bg-gray-800 disabled:text-gray-600"
                  >
                    {processing ? "提交中…" : "未通过"}
                  </button>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
