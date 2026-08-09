"use client";

import { useEffect, useMemo, useState } from "react";
import Modal from "@/components/ui/Modal";
import { getCard, loadAllCards } from "@/data/CardLoader";
import { loadAllDecks, subscribeDecksUpdated } from "@/data/DeckMapper";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import type { DeckPlazaItem, DeckPlazaSort } from "@/types/net";

const COLORS = ["", "红", "绿", "蓝", "紫", "黑", "黄"];

type PublishDraft = {
  publicationId?: string;
  sourceDeckName: string;
  title: string;
};

function formatDate(timestamp: number) {
  return new Intl.DateTimeFormat("zh-CN", { year: "numeric", month: "2-digit", day: "2-digit" }).format(timestamp);
}

function countedCards(cards: string[]) {
  const counts = new Map<string, number>();
  cards.forEach((number) => counts.set(number, (counts.get(number) ?? 0) + 1));
  return [...counts.entries()];
}

function CostCurve({ item }: { item: DeckPlazaItem }) {
  const data = useMemo(() => {
    const costs = Array.from({ length: 11 }, () => 0);
    for (const number of item.cards) {
      const cost = Math.min(10, Math.max(0, getCard(number)?.cost ?? 0));
      costs[cost] += 1;
    }
    return costs;
  }, [item]);
  const max = Math.max(1, ...data);
  return (
    <div>
      <p className="mb-2 text-xs font-bold text-gray-400">费用曲线</p>
      <div className="flex h-20 items-end gap-1 rounded-xl bg-gray-950 p-2">
        {data.map((count, cost) => (
          <div key={cost} className="flex h-full flex-1 flex-col items-center justify-end gap-1">
            <span className="text-[9px] text-gray-400">{count || ""}</span>
            <span className="w-full rounded-t bg-orange-500/80" style={{ height: `${Math.max(count ? 4 : 0, (count / max) * 42)}px` }} />
            <span className="text-[9px] text-gray-600">{cost === 10 ? "10+" : cost}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function DeckDetail({ item }: { item: DeckPlazaItem }) {
  const entries = countedCards(item.cards);
  return (
    <div className="space-y-5">
      <div className="flex gap-4 rounded-2xl border border-gray-800 bg-gray-950 p-4">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={thumbSrc(item.leaderSprite || CARD_BACK_SRC)}
          alt={item.leaderName}
          className="h-28 w-20 shrink-0 rounded-lg border border-gray-700 object-cover"
          onError={(event) => advanceImageFallback(event.currentTarget, [item.leaderSprite])}
        />
        <div className="min-w-0">
          <h2 className="truncate text-xl font-black text-white">{item.title}</h2>
          <p className="mt-1 text-sm text-gray-400">{item.leaderName} · {item.leaderColor}</p>
          <p className="mt-1 text-xs text-gray-600">作者：{item.authorName} · 更新于 {formatDate(item.updatedAt)}</p>
          <div className="mt-4 flex flex-wrap gap-2 text-xs">
            <span className="rounded-full bg-yellow-500/10 px-2 py-1 text-yellow-400">角色 {item.charCount}</span>
            <span className="rounded-full bg-emerald-500/10 px-2 py-1 text-emerald-400">事件 {item.eventCount}</span>
            <span className="rounded-full bg-purple-500/10 px-2 py-1 text-purple-400">舞台 {item.stageCount}</span>
          </div>
        </div>
      </div>
      <CostCurve item={item} />
      <div>
        <div className="mb-2 flex items-center justify-between">
          <p className="text-xs font-bold text-gray-400">主卡组构筑</p>
          <span className="text-xs text-gray-600">共 50 张</span>
        </div>
        <div className="grid gap-2 @[640px]:grid-cols-2">
          {entries.map(([number, count]) => {
            const card = getCard(number);
            const sprite = item.spriteMap[number] || card?.sprite || CARD_BACK_SRC;
            return (
              <div key={number} className="flex items-center gap-3 rounded-xl border border-gray-800 bg-gray-950 p-2">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={thumbSrc(sprite)} alt={card?.name ?? number} className="h-14 w-10 rounded object-cover" onError={(event) => advanceImageFallback(event.currentTarget, [sprite])} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-xs font-bold text-gray-200">{card?.name ?? number}</p>
                  <p className="mt-1 text-[11px] text-gray-600">{number}{card ? ` · 费用 ${card.cost}` : ""}</p>
                </div>
                <span className="rounded-lg bg-orange-500/15 px-2 py-1 text-xs font-black text-orange-300">×{count}</span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

export default function DeckPlazaPanel({
  publishDeckName,
  onPublishOpened,
  onGoMine,
}: {
  publishDeckName?: string | null;
  onPublishOpened?: () => void;
  onGoMine: () => void;
}) {
  const connState = useNetStore((state) => state.connState);
  const pageData = useNetStore((state) => state.deckPlazaPage);
  const revision = useNetStore((state) => state.deckPlazaRevision);
  const [query, setQuery] = useState("");
  const [color, setColor] = useState("");
  const [sort, setSort] = useState<DeckPlazaSort>("popular");
  const [mineOnly, setMineOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [detail, setDetail] = useState<DeckPlazaItem | null>(null);
  const [publish, setPublish] = useState<PublishDraft | null>(null);
  const [decks, setDecks] = useState(() => loadAllDecks());
  const [cardsReady, setCardsReady] = useState(false);

  useEffect(() => subscribeDecksUpdated(() => setDecks(loadAllDecks())), []);
  useEffect(() => { loadAllCards().finally(() => setCardsReady(true)); }, []);

  useEffect(() => {
    if (!publishDeckName) return;
    setPublish({ sourceDeckName: publishDeckName, title: publishDeckName });
    onPublishOpened?.();
  }, [onPublishOpened, publishDeckName]);

  useEffect(() => {
    if (connState !== "connected") return;
    const timer = window.setTimeout(() => {
      HomeRequest.requestDeckPlaza({ page, pageSize: 20, sort, query: query.trim(), color, mineOnly });
    }, 250);
    return () => window.clearTimeout(timer);
  }, [color, connState, mineOnly, page, query, revision, sort]);

  useEffect(() => {
    if (!detail || !pageData) return;
    const refreshed = pageData.items.find((item) => item.id === detail.id);
    if (refreshed && refreshed !== detail) setDetail(refreshed);
  }, [detail, pageData]);

  const openPublish = (item?: DeckPlazaItem) => {
    const firstDeck = Object.keys(decks)[0] ?? "";
    setPublish({
      publicationId: item?.id,
      sourceDeckName: firstDeck,
      title: item?.title ?? firstDeck,
    });
  };

  const submitPublish = () => {
    if (!publish?.sourceDeckName || !publish.title.trim()) return;
    if (HomeRequest.publishDeckPlaza(publish.sourceDeckName, publish.title.trim(), publish.publicationId)) setPublish(null);
  };

  const deletePublication = (item: DeckPlazaItem) => {
    if (!window.confirm(`确定删除卡组投稿“${item.title}”吗？本地卡组不会被删除。`)) return;
    HomeRequest.deleteDeckPlaza(item.id);
    if (detail?.id === item.id) setDetail(null);
  };

  const deckEntries = Object.keys(decks);
  const items = pageData?.items ?? [];

  return (
    <section className="flex h-full min-h-0 flex-col overflow-hidden">
      <div className="shrink-0 border-b border-gray-800 px-3 pb-3 @[640px]:px-4">
        <div className="flex flex-col gap-3 @[900px]:flex-row @[900px]:items-center">
          <div className="min-w-0 flex-1">
            <h1 className="text-lg font-black text-white">卡组广场</h1>
            <p className="text-xs text-gray-500">发现热门构筑，一键复制为自己的独立卡组。</p>
          </div>
          <button type="button" disabled={deckEntries.length === 0} onClick={() => openPublish()} className="min-h-11 rounded-xl bg-orange-500 px-4 text-sm font-bold text-white hover:bg-orange-400 disabled:bg-gray-800 disabled:text-gray-600">
            发布卡组
          </button>
        </div>
        <div className="mt-3 grid gap-2 @[640px]:grid-cols-[minmax(12rem,1fr)_8rem_8rem_auto]">
          <input value={query} onChange={(event) => { setQuery(event.target.value); setPage(1); }} placeholder="搜索卡组或作者" className="min-h-11 rounded-xl border border-gray-700 bg-gray-900 px-3 text-sm text-white outline-none focus:border-orange-500" />
          <select value={color} onChange={(event) => { setColor(event.target.value); setPage(1); }} className="min-h-11 rounded-xl border border-gray-700 bg-gray-900 px-3 text-sm text-gray-300 outline-none">
            {COLORS.map((value) => <option key={value || "all"} value={value}>{value || "全部颜色"}</option>)}
          </select>
          <select value={sort} onChange={(event) => { setSort(event.target.value as DeckPlazaSort); setPage(1); }} className="min-h-11 rounded-xl border border-gray-700 bg-gray-900 px-3 text-sm text-gray-300 outline-none">
            <option value="popular">热门</option><option value="newest">最新</option><option value="copies">最多复制</option>
          </select>
          <label className="flex min-h-11 cursor-pointer items-center gap-2 rounded-xl border border-gray-700 px-3 text-sm text-gray-400">
            <input type="checkbox" checked={mineOnly} onChange={(event) => { setMineOnly(event.target.checked); setPage(1); }} className="accent-orange-500" />只看我的
          </label>
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 @[640px]:p-4">
        {!pageData ? (
          <div className="py-20 text-center text-sm text-gray-500">正在读取卡组广场…</div>
        ) : items.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-gray-700 py-20 text-center text-sm text-gray-500">没有找到符合条件的卡组。</div>
        ) : (
          <div className="grid gap-3 @[760px]:grid-cols-2 @[1280px]:grid-cols-3">
            {items.map((item) => (
              <article key={item.id} className="flex gap-3 rounded-2xl border border-gray-800 bg-gray-900 p-3 transition-colors hover:border-gray-700">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={thumbSrc(item.leaderSprite || CARD_BACK_SRC)} alt={item.leaderName} className="h-28 w-20 shrink-0 rounded-lg border border-gray-700 object-cover" onError={(event) => advanceImageFallback(event.currentTarget, [item.leaderSprite])} />
                <div className="flex min-w-0 flex-1 flex-col">
                  <div className="flex items-start gap-2">
                    <div className="min-w-0 flex-1"><h2 className="truncate text-sm font-black text-white">{item.title}</h2><p className="mt-0.5 truncate text-[11px] text-gray-500">{item.authorName} · {item.leaderName}</p></div>
                    {item.owned && <span className="rounded-full bg-orange-500/10 px-2 py-1 text-[9px] font-bold text-orange-300">我的</span>}
                  </div>
                  <p className="mt-2 text-[11px] text-gray-600">角 {item.charCount} · 事 {item.eventCount} · 场 {item.stageCount}</p>
                  <div className="mt-auto flex items-center gap-1.5 pt-3">
                    <button type="button" aria-pressed={item.liked} onClick={() => HomeRequest.toggleDeckPlazaLike(item.id)} className={`min-h-10 rounded-lg border px-2 text-xs font-bold ${item.liked ? "border-rose-500/60 bg-rose-500/10 text-rose-300" : "border-gray-700 text-gray-400 hover:text-rose-300"}`}>{item.liked ? "♥" : "♡"} {item.likes}</button>
                    <span className="text-[10px] text-gray-600">复制 {item.copies}</span>
                    <button type="button" onClick={() => setDetail(item)} className="ml-auto min-h-10 rounded-lg bg-gray-800 px-3 text-xs font-bold text-gray-200 hover:bg-gray-700">查看构筑</button>
                  </div>
                </div>
              </article>
            ))}
          </div>
        )}

        {pageData && pageData.total > pageData.pageSize && (
          <div className="mt-4 flex items-center justify-center gap-3">
            <button type="button" disabled={page <= 1} onClick={() => setPage((value) => Math.max(1, value - 1))} className="min-h-11 rounded-xl border border-gray-700 px-4 text-sm text-gray-300 disabled:text-gray-700">上一页</button>
            <span className="text-xs text-gray-500">第 {pageData.page} 页 · 共 {pageData.total} 副</span>
            <button type="button" disabled={!pageData.hasMore} onClick={() => setPage((value) => value + 1)} className="min-h-11 rounded-xl border border-gray-700 px-4 text-sm text-gray-300 disabled:text-gray-700">下一页</button>
          </div>
        )}
      </div>

      <Modal open={Boolean(detail)} onClose={() => setDetail(null)} title="卡组详情" mobileSheet maxWidthClass="max-w-3xl">
        {detail && (
          <>
            <div className="max-h-[65vh] overflow-y-auto pr-1"><DeckDetail item={detail} key={`${detail.id}-${cardsReady}`} /></div>
            <div className="mt-4 grid grid-cols-2 gap-2 @[640px]:grid-cols-4">
              <button type="button" onClick={() => HomeRequest.toggleDeckPlazaLike(detail.id)} className="min-h-11 rounded-xl border border-rose-500/40 text-sm font-bold text-rose-300">{detail.liked ? "取消点赞" : "点赞"}</button>
              <button type="button" onClick={() => HomeRequest.copyDeckPlaza(detail.id)} className="min-h-11 rounded-xl bg-emerald-600 text-sm font-bold text-white hover:bg-emerald-500">复制到我的卡组</button>
              {detail.owned && <button type="button" onClick={() => { openPublish(detail); setDetail(null); }} className="min-h-11 rounded-xl bg-orange-500 text-sm font-bold text-white">更新投稿</button>}
              {detail.owned && <button type="button" onClick={() => deletePublication(detail)} className="min-h-11 rounded-xl border border-red-500/50 text-sm font-bold text-red-300">删除投稿</button>}
            </div>
            <button type="button" onClick={onGoMine} className="mt-3 w-full text-xs text-gray-500 hover:text-gray-300">返回“我的卡组”查看已复制内容</button>
          </>
        )}
      </Modal>

      <Modal open={Boolean(publish)} onClose={() => setPublish(null)} title={publish?.publicationId ? "更新卡组投稿" : "发布卡组"} mobileSheet maxWidthClass="max-w-md">
        <label className="block text-xs font-bold text-gray-400">选择本地卡组</label>
        <select value={publish?.sourceDeckName ?? ""} onChange={(event) => setPublish((draft) => draft ? { ...draft, sourceDeckName: event.target.value } : draft)} className="mt-2 min-h-11 w-full rounded-xl border border-gray-700 bg-gray-900 px-3 text-sm text-white">
          <option value="" disabled>请选择卡组</option>
          {deckEntries.map((name) => <option key={name} value={name}>{name}</option>)}
        </select>
        <label className="mt-4 block text-xs font-bold text-gray-400">广场标题</label>
        <input maxLength={50} value={publish?.title ?? ""} onChange={(event) => setPublish((draft) => draft ? { ...draft, title: event.target.value } : draft)} className="mt-2 min-h-11 w-full rounded-xl border border-gray-700 bg-gray-900 px-3 text-sm text-white outline-none focus:border-orange-500" placeholder="为这套构筑起个名字" />
        <p className="mt-2 text-xs text-gray-600">发布的是当前构筑快照，本地卡组之后的删改不会影响投稿。</p>
        <div className="mt-5 grid grid-cols-2 gap-3">
          <button type="button" onClick={() => setPublish(null)} className="min-h-11 rounded-xl bg-gray-800 text-sm text-gray-300">取消</button>
          <button type="button" disabled={!publish?.sourceDeckName || !publish.title.trim()} onClick={submitPublish} className="min-h-11 rounded-xl bg-orange-500 text-sm font-bold text-white disabled:bg-gray-800 disabled:text-gray-600">{publish?.publicationId ? "确认更新" : "确认发布"}</button>
        </div>
      </Modal>
    </section>
  );
}
