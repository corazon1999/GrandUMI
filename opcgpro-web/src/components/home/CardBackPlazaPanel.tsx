"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import CardBack from "@/components/ui/CardBack";
import { useLanguage } from "@/i18n/LanguageProvider";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";

const MAX_SOURCE_BYTES = 8 * 1024 * 1024;
const TARGET_WIDTH = 420;
const TARGET_HEIGHT = 588;
const MAX_UPLOAD_BYTES = 235 * 1024;
const GALLERY_TIMEOUT_MS = 8_000;

type PreparedImage = {
  previewUrl: string;
  mimeType: "image/webp" | "image/jpeg";
  imageBase64: string;
  size: number;
};

type GalleryView = "popular" | "mine";

function canvasBlob(canvas: HTMLCanvasElement, type: "image/webp" | "image/jpeg", quality: number) {
  return new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, quality));
}

function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error("读取压缩图片失败"));
    reader.onload = () => resolve(String(reader.result).split(",", 2)[1] ?? "");
    reader.readAsDataURL(blob);
  });
}

async function prepareCardBack(file: File): Promise<PreparedImage> {
  if (!file.type.startsWith("image/")) throw new Error("请选择 PNG、JPEG 或 WebP 图片");
  if (file.size > MAX_SOURCE_BYTES) throw new Error("原图不能超过 8MB");

  const bitmap = await createImageBitmap(file);
  try {
    const canvas = document.createElement("canvas");
    canvas.width = TARGET_WIDTH;
    canvas.height = TARGET_HEIGHT;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("浏览器无法处理这张图片");

    const scale = Math.max(TARGET_WIDTH / bitmap.width, TARGET_HEIGHT / bitmap.height);
    const width = bitmap.width * scale;
    const height = bitmap.height * scale;
    context.drawImage(bitmap, (TARGET_WIDTH - width) / 2, (TARGET_HEIGHT - height) / 2, width, height);

    let mimeType: PreparedImage["mimeType"] = "image/webp";
    let blob: Blob | null = null;
    for (const quality of [0.88, 0.78, 0.68, 0.56]) {
      blob = await canvasBlob(canvas, mimeType, quality);
      if (blob && blob.size <= MAX_UPLOAD_BYTES) break;
    }
    if (!blob || blob.type !== "image/webp") {
      mimeType = "image/jpeg";
      for (const quality of [0.86, 0.74, 0.62, 0.5]) {
        blob = await canvasBlob(canvas, mimeType, quality);
        if (blob && blob.size <= MAX_UPLOAD_BYTES) break;
      }
    }
    if (!blob || blob.size > MAX_UPLOAD_BYTES) throw new Error("图片压缩后仍过大，请换一张细节较少的图片");

    return {
      previewUrl: URL.createObjectURL(blob),
      mimeType,
      imageBase64: await blobToBase64(blob),
      size: blob.size,
    };
  } finally {
    bitmap.close();
  }
}

export default function CardBackPlazaPanel({ onOpenProfile }: { onOpenProfile: () => void }) {
  const { t } = useLanguage();
  const gallery = useNetStore((state) => state.cardBackGallery);
  const currentCardBackId = useNetStore((state) => state.cardBackId);
  const connState = useNetStore((state) => state.connState);
  const [name, setName] = useState("");
  const [prepared, setPrepared] = useState<PreparedImage | null>(null);
  const [preparing, setPreparing] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [galleryView, setGalleryView] = useState<GalleryView>("popular");
  const [galleryTimedOut, setGalleryTimedOut] = useState(false);
  const [error, setError] = useState("");
  const fileRef = useRef<HTMLInputElement>(null);

  const requestGallery = useCallback(() => {
    setGalleryTimedOut(false);
    if (!HomeRequest.requestCardBackGallery()) setGalleryTimedOut(true);
  }, []);

  useEffect(() => {
    if (connState === "connected") requestGallery();
    else setGalleryTimedOut(false);
  }, [connState, requestGallery]);
  useEffect(() => {
    if (gallery !== null) {
      setGalleryTimedOut(false);
      return;
    }
    if (connState !== "connected" || galleryTimedOut) return;
    const timeout = window.setTimeout(() => setGalleryTimedOut(true), GALLERY_TIMEOUT_MS);
    return () => window.clearTimeout(timeout);
  }, [connState, gallery, galleryTimedOut]);
  useEffect(() => { setSubmitting(false); setDeletingId(null); }, [gallery]);
  useEffect(() => () => { if (prepared) URL.revokeObjectURL(prepared.previewUrl); }, [prepared]);

  const chooseFile = async (file: File | undefined) => {
    if (!file) return;
    setPreparing(true);
    setError("");
    try {
      const next = await prepareCardBack(file);
      setPrepared((previous) => {
        if (previous) URL.revokeObjectURL(previous.previewUrl);
        return next;
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "图片处理失败");
    } finally {
      setPreparing(false);
    }
  };

  const upload = () => {
    const trimmedName = name.trim();
    if (!trimmedName) { setError("请先为卡背定义一个名字"); return; }
    if (!prepared) { setError("请先选择一张卡背图片"); return; }
    setSubmitting(true);
    setError("");
    if (!HomeRequest.uploadCardBack(trimmedName, prepared.mimeType, prepared.imageBase64)) {
      setSubmitting(false);
      setError("网络未连接，暂时无法上传");
      return;
    }
    setGalleryView("mine");
    setName("");
    setPrepared(null);
    if (fileRef.current) fileRef.current.value = "";
  };

  const deleteCardBack = (cardBackId: string, cardBackName: string) => {
    if (!window.confirm(t(`确定删除卡背“${cardBackName}”吗？删除后无法恢复。`))) return;
    setDeletingId(cardBackId);
    setError("");
    if (!HomeRequest.deleteCardBack(cardBackId)) {
      setDeletingId(null);
      setError("网络未连接，暂时无法删除");
    }
  };

  const approvedCardBacks = gallery?.filter((item) => item.reviewStatus === "approved" && item.publiclyListed) ?? [];
  const ownedCardBacks = gallery?.filter((item) => item.owned) ?? [];
  const displayedCardBacks = galleryView === "mine" ? ownedCardBacks : approvedCardBacks;

  return (
    <section className="h-full overflow-y-auto px-4 py-5 @[720px]:px-6 @[720px]:py-6" data-testid="card-back-plaza">
      <header className="flex flex-col gap-3 @[640px]:flex-row @[640px]:items-end @[640px]:justify-between">
        <div>
          <p className="text-xs font-bold uppercase tracking-[0.2em] text-rose-400">Community Gallery</p>
          <h1 className="mt-1 text-2xl font-black text-white">卡背广场</h1>
          <p className="mt-1 text-sm text-gray-500">分享你的卡背设计，审核通过后进入广场，红心越多排名越靠前。</p>
        </div>
        <button type="button" onClick={onOpenProfile} className="min-h-11 rounded-xl border border-gray-700 px-4 text-sm font-bold text-gray-300 hover:border-orange-500 hover:text-white">
          查看我的卡背设置
        </button>
      </header>

      <article className="mt-5 grid gap-4 rounded-2xl border border-gray-800 bg-gray-900 p-4 @[720px]:grid-cols-[10rem_minmax(0,1fr)] @[720px]:p-5">
        <button
          type="button"
          onClick={() => fileRef.current?.click()}
          className="relative mx-auto aspect-[5/7] w-36 overflow-hidden rounded-xl border-2 border-dashed border-gray-700 bg-gray-950 text-xs text-gray-500 transition-colors hover:border-rose-500 hover:text-rose-300 @[720px]:mx-0 @[720px]:w-full"
        >
          {prepared ? <img src={prepared.previewUrl} alt="待上传卡背预览" className="h-full w-full object-cover" /> : <span>{preparing ? "图片处理中…" : "点击选择图片"}</span>}
        </button>
        <div className="min-w-0">
          <h2 className="font-bold text-white">发布新卡背</h2>
          <p className="mt-1 text-xs leading-5 text-gray-500">上传后将由管理员审核，通过后公开展示。图片会居中裁切为 5:7，并压缩到适合对局加载的大小。</p>
          <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(event) => void chooseFile(event.target.files?.[0])} />
          <label className="mt-4 block text-xs font-bold text-gray-400" htmlFor="card-back-name">卡背名字</label>
          <input
            id="card-back-name"
            value={name}
            maxLength={30}
            onChange={(event) => setName(event.target.value)}
            placeholder="例如：新世界的黎明"
            className="mt-2 min-h-11 w-full rounded-xl border border-gray-700 bg-gray-950 px-3 text-sm text-white outline-none focus:border-rose-500"
          />
          <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
            <p className={`text-xs ${error ? "text-red-400" : "text-gray-600"}`}>{error || (prepared ? `已压缩至 ${Math.ceil(prepared.size / 1024)}KB` : "支持 PNG / JPEG / WebP，原图最大 8MB")}</p>
            <button type="button" onClick={upload} disabled={preparing || submitting || !prepared || !name.trim()} className="min-h-11 rounded-xl bg-rose-500 px-5 text-sm font-bold text-white hover:bg-rose-400 disabled:bg-gray-700 disabled:text-gray-500">
              {submitting ? "发布中…" : "发布到广场"}
            </button>
          </div>
        </div>
      </article>

      <div className="mt-6 flex flex-col gap-3 @[560px]:flex-row @[560px]:items-end @[560px]:justify-between">
        <div>
          <div role="tablist" aria-label="卡背广场分类" className="inline-flex rounded-xl border border-gray-800 bg-gray-900 p-1">
            <button
              type="button"
              role="tab"
              aria-selected={galleryView === "popular"}
              onClick={() => setGalleryView("popular")}
              className={`min-h-10 rounded-lg px-4 text-sm font-bold transition-colors ${galleryView === "popular" ? "bg-orange-500 text-white" : "text-gray-500 hover:text-white"}`}
            >
              热门卡背
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={galleryView === "mine"}
              onClick={() => setGalleryView("mine")}
              className={`min-h-10 rounded-lg px-4 text-sm font-bold transition-colors ${galleryView === "mine" ? "bg-rose-500 text-white" : "text-gray-500 hover:text-white"}`}
            >
              我发布的卡背
              {ownedCardBacks.length > 0 && <span className="ml-1.5 text-xs opacity-75">{ownedCardBacks.length}</span>}
            </button>
          </div>
          <p className="mt-2 text-xs text-gray-600">
            {galleryView === "popular" ? "最多展示 300 款已通过审核的卡背；按红心数量排序，同票时新发布的在前。" : "在这里查看审核状态，并管理你提交的卡背。"}
          </p>
        </div>
        <span className="text-xs text-gray-600">{displayedCardBacks?.length ?? 0} 款</span>
      </div>

      {gallery === null ? galleryTimedOut ? (
        <div className="mt-4 rounded-2xl border border-amber-500/30 bg-gray-900 px-4 py-12 text-center">
          <p className="text-sm text-amber-200">卡背广场响应超时，请检查当前线路后重试。</p>
          <button
            type="button"
            onClick={requestGallery}
            disabled={connState !== "connected"}
            className="mt-4 min-h-11 rounded-xl bg-orange-500 px-5 text-sm font-bold text-white hover:bg-orange-400 disabled:bg-gray-700 disabled:text-gray-500"
          >
            重试
          </button>
        </div>
      ) : (
        <div className="mt-4 rounded-2xl border border-gray-800 bg-gray-900 py-16 text-center text-sm text-gray-500">正在读取卡背广场…</div>
      ) : displayedCardBacks?.length === 0 ? (
        <div className="mt-4 rounded-2xl border border-dashed border-gray-700 py-16 text-center text-sm text-gray-500">
          {galleryView === "mine" ? "你还没有发布卡背，可以在上方发布第一款作品。" : "广场还没有投稿，来发布第一款卡背吧。"}
        </div>
      ) : (
        <div className="mt-4 grid grid-cols-2 gap-3 @[560px]:grid-cols-3 @[820px]:grid-cols-4 @[1120px]:grid-cols-5">
          {displayedCardBacks?.map((item, index) => {
            const active = currentCardBackId === item.id;
            const approved = item.reviewStatus === "approved";
            return (
              <article key={item.id} className={`overflow-hidden rounded-2xl border bg-gray-900 p-3 ${active ? "border-orange-500 ring-1 ring-orange-500/30" : "border-gray-800"}`}>
                <div className="relative mx-auto aspect-[5/7] w-full max-w-40 overflow-hidden rounded-xl bg-gray-950 shadow-xl">
                  <CardBack cardBackId={item.id} decorative />
                  {galleryView === "popular" && <span className="absolute left-2 top-2 rounded-full bg-black/75 px-2 py-1 text-[10px] font-black text-white">#{index + 1}</span>}
                  {active && <span className="absolute bottom-2 left-2 rounded-full bg-orange-500 px-2 py-1 text-[10px] font-bold text-white">使用中</span>}
                  {galleryView === "mine" && !approved && (
                    <span className={`absolute bottom-2 left-2 rounded-full px-2 py-1 text-[10px] font-bold text-white ${item.reviewStatus === "pending" ? "bg-amber-500" : "bg-red-600"}`}>
                      {item.reviewStatus === "pending" ? "待审核" : "未通过"}
                    </span>
                  )}
                </div>
                <h3 className="mt-3 truncate text-sm font-bold text-white" title={item.name}>{item.name}</h3>
                <p className="mt-1 truncate text-[11px] text-gray-600">by {item.authorName}{item.owned ? " · 我的投稿" : ""}</p>
                {galleryView === "mine" && item.reviewStatus === "rejected" && item.reviewReason && (
                  <p className="mt-2 rounded-lg border border-red-900/70 bg-red-950/20 px-2 py-2 text-[11px] leading-5 text-red-300">未通过理由：{item.reviewReason}</p>
                )}
                <div className="mt-3 grid grid-cols-[auto_1fr] gap-2">
                  <button
                    type="button"
                    disabled={!approved}
                    aria-label={`${item.liked ? "取消" : "添加"}红心，当前 ${item.likes} 个`}
                    aria-pressed={item.liked}
                    onClick={() => HomeRequest.toggleCardBackLike(item.id)}
                    className={`min-h-11 rounded-xl border px-3 text-sm font-bold disabled:border-gray-800 disabled:text-gray-600 ${item.liked ? "border-rose-500/60 bg-rose-500/15 text-rose-300" : "border-gray-700 text-gray-400 hover:border-rose-500 hover:text-rose-300"}`}
                  >
                    {item.liked ? "♥" : "♡"} {item.likes}
                  </button>
                  <button type="button" disabled={active || !approved} onClick={() => HomeRequest.updateCardBack(item.id)} className="min-h-11 rounded-xl bg-orange-500 px-2 text-xs font-bold text-white hover:bg-orange-400 disabled:bg-gray-700 disabled:text-gray-500">
                    {active ? "已选用" : approved ? "选用并点♥" : "审核后可选"}
                  </button>
                  {galleryView === "mine" && item.owned && (
                    <button
                      type="button"
                      disabled={deletingId === item.id}
                      onClick={() => deleteCardBack(item.id, item.name)}
                      className="col-span-2 min-h-11 rounded-xl border border-red-500/50 px-3 text-xs font-bold text-red-300 hover:border-red-400 hover:bg-red-500/10 disabled:border-gray-700 disabled:text-gray-600"
                    >
                      {deletingId === item.id ? "删除中…" : "删除投稿"}
                    </button>
                  )}
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
