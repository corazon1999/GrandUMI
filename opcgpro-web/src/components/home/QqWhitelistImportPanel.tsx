"use client";

import { useEffect, useRef, useState } from "react";
import { eventBus } from "@/net/eventBus";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import type { MsgQqWhitelistImport, QqWhitelistUpdateEvent } from "@/types/net";
import {
  previewQqWhitelistJson,
  QQ_WHITELIST_MAX_BYTES,
  QQ_WHITELIST_MAX_MEMBERS,
} from "@/lib/qqWhitelist.mjs";
import { formatQqWhitelistUpdateTime } from "@/lib/qqWhitelistUpdateTime";

type QqWhitelistPreview = {
  totalCount: number;
  uniqueCount: number;
  duplicateCount: number;
};

const describeUpdate = (update: QqWhitelistUpdateEvent) => {
  if (update.outcome === "success") {
    return `更新成功 · v${update.version ?? "?"} · ${update.memberCount ?? "?"} 人`
      + ` · 新增 ${update.addedCount ?? 0} · 移除 ${update.removedCount ?? 0}`;
  }
  const retained = update.version && update.memberCount
    ? `白名单保持 v${update.version} · ${update.memberCount} 人`
    : "白名单仍未初始化";
  return `更新失败 · ${retained} · ${update.error ?? "未提供失败原因"}`;
};

export default function QqWhitelistImportPanel({
  bootstrap = false,
  onImported,
}: {
  bootstrap?: boolean;
  onImported?: (result: MsgQqWhitelistImport) => void;
}) {
  const status = useNetStore((state) => state.qqWhitelistStatus);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const fileReadGenerationRef = useRef(0);
  const onImportedRef = useRef(onImported);
  const [fileName, setFileName] = useState("");
  const [rawJson, setRawJson] = useState("");
  const [preview, setPreview] = useState<QqWhitelistPreview | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [summary, setSummary] = useState<MsgQqWhitelistImport | null>(null);
  const maxBytes = status?.maxImportBytes ?? QQ_WHITELIST_MAX_BYTES;
  const maxMembers = status?.maxImportMembers ?? QQ_WHITELIST_MAX_MEMBERS;
  const canImport = status?.canImport !== false;

  useEffect(() => {
    onImportedRef.current = onImported;
  }, [onImported]);

  useEffect(() => {
    HomeRequest.requestQqWhitelistStatus();
    const statusTimer = window.setInterval(
      () => HomeRequest.requestQqWhitelistStatus(),
      15_000,
    );
    const onMessage = (message: { proto: string }) => {
      if (message.proto !== "MsgQqWhitelistImport") return;
      const result = message as MsgQqWhitelistImport;
      setPending(false);
      if (result.result) {
        setSummary(result);
        setRawJson("");
        setPreview(null);
        setFileName("");
        if (fileInputRef.current) fileInputRef.current.value = "";
        onImportedRef.current?.(result);
      }
    };
    const onClose = () => setPending(false);
    eventBus.on("message", onMessage);
    eventBus.on("close", onClose);
    return () => {
      fileReadGenerationRef.current += 1;
      window.clearInterval(statusTimer);
      eventBus.off("message", onMessage);
      eventBus.off("close", onClose);
    };
  }, []);

  const chooseFile = async (file?: File) => {
    const readGeneration = ++fileReadGenerationRef.current;
    setSummary(null);
    setLocalError(null);
    setPreview(null);
    setRawJson("");
    setFileName(file?.name ?? "");
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".json")) {
      setLocalError("请选择 .json 文件。");
      return;
    }
    if (file.size > maxBytes) {
      setLocalError(`JSON 文件不能超过 ${Math.floor(maxBytes / 1024)} KiB。`);
      return;
    }
    try {
      const text = await file.text();
      const nextPreview = previewQqWhitelistJson(text, maxBytes, maxMembers);
      if (readGeneration !== fileReadGenerationRef.current) return;
      setRawJson(text);
      setPreview(nextPreview);
    } catch (error) {
      if (readGeneration !== fileReadGenerationRef.current) return;
      setLocalError(error instanceof Error ? error.message : "无法读取 JSON 文件。");
    }
  };

  const submit = () => {
    if (!preview || !rawJson || pending || !canImport) return;
    const confirmed = window.confirm(
      `${bootstrap ? "确认初始化" : "确认全量替换"} QQ 群白名单为 ${preview.uniqueCount} 人？`
      + `\n重复项 ${preview.duplicateCount} 条将自动去重；服务端会再次全量校验。`,
    );
    if (!confirmed) return;
    setPending(HomeRequest.importQqWhitelist(rawJson));
  };

  return (
    <section className="rounded-2xl border border-cyan-900/70 bg-cyan-950/15 p-4 sm:p-5" data-testid={bootstrap ? "qq-bootstrap-import" : "qq-whitelist-import"}>
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-black tracking-[0.16em] text-cyan-400">QQ ACCESS CONTROL</p>
          <h2 className="mt-1 text-lg font-black text-white">{bootstrap ? "初始化群成员白名单" : "QQ 群成员白名单"}</h2>
          <p className="mt-1 text-xs leading-5 text-gray-400">
            支持顶层字符串/数字数组、qq/uin/user_id 对象数组，以及 members/data/list 包装。导入为原子全量替换。
          </p>
        </div>
        <div className="shrink-0 rounded-xl border border-gray-800 bg-gray-950/70 px-3 py-2 text-xs text-gray-400">
          {status?.initialized ? `版本 v${status.version ?? 0} · ${status.memberCount ?? 0} 人` : "尚未初始化"}
        </div>
      </div>

      {status?.initialized && (
        <dl className="mt-4 grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
          <div className="rounded-lg bg-gray-950/70 p-3"><dt className="text-gray-500">上次新增</dt><dd className="mt-1 font-black text-emerald-300">{status.addedCount ?? 0}</dd></div>
          <div className="rounded-lg bg-gray-950/70 p-3"><dt className="text-gray-500">上次移除</dt><dd className="mt-1 font-black text-amber-300">{status.removedCount ?? 0}</dd></div>
          <div className="rounded-lg bg-gray-950/70 p-3"><dt className="text-gray-500">移出已绑定</dt><dd className="mt-1 font-black text-red-300">{status.removedBoundCount ?? 0}</dd></div>
          <div className="rounded-lg bg-gray-950/70 p-3"><dt className="text-gray-500">当前账号</dt><dd className="mt-1 font-black text-cyan-200">{status.accountBinding?.bound ? `${status.accountBinding.maskedQq ?? "已绑定"}${status.accountBinding.currentlyWhitelisted ? " · 有效" : " · 已移出"}` : "未绑定"}</dd></div>
        </dl>
      )}

      {!!status?.recentUpdates?.length && (
        <div className="mt-4 rounded-xl border border-gray-800 bg-gray-950/60 p-3">
          <div className="flex items-center justify-between gap-3">
            <h3 className="text-sm font-black text-white">持久更新通知</h3>
            <span className="text-[11px] text-gray-500">每 15 秒自动刷新</span>
          </div>
          <p className="sr-only" aria-live="polite">{describeUpdate(status.recentUpdates[0])}</p>
          <ol className="mt-2 max-h-72 space-y-2 overflow-y-auto pr-1">
            {status.recentUpdates.map((update) => (
              <li
                key={update.eventKey}
                className={`rounded-lg border px-3 py-2 text-xs leading-5 ${update.outcome === "success"
                  ? "border-emerald-900/70 bg-emerald-950/20 text-emerald-200"
                  : "border-red-900/70 bg-red-950/25 text-red-200"}`}
              >
                <div className="font-bold">{describeUpdate(update)}</div>
                <div className="mt-1 text-[11px] text-gray-400">
                  {formatQqWhitelistUpdateTime(update.occurredAt)} · {update.source.startsWith("qq-sync:") ? "机器人整点同步" : "管理员手工导入"}
                </div>
              </li>
            ))}
          </ol>
        </div>
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept=".json,application/json"
        className="sr-only"
        onChange={(event) => void chooseFile(event.target.files?.[0])}
      />
      <div className="mt-4 flex flex-col gap-3 sm:flex-row">
        <button
          type="button"
          onClick={() => {
            if (!fileInputRef.current) return;
            fileInputRef.current.value = "";
            fileInputRef.current.click();
          }}
          disabled={pending || !canImport}
          className="min-h-11 rounded-xl border border-cyan-700 bg-cyan-950/40 px-4 text-sm font-bold text-cyan-100 hover:bg-cyan-900/50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          选择 .json 文件
        </button>
        <div className="min-h-11 min-w-0 flex-1 break-words rounded-xl border border-gray-800 bg-gray-950/70 px-3 py-2 text-sm leading-6 text-gray-400">
          {fileName || `最大 ${Math.floor(maxBytes / 1024)} KiB / ${maxMembers} 条`}
        </div>
      </div>

      {preview && (
        <div className="mt-3 rounded-xl border border-emerald-900/70 bg-emerald-950/20 px-4 py-3 text-sm text-emerald-200" aria-live="polite">
          本地预览：共 {preview.totalCount} 条，去重后 {preview.uniqueCount} 人，重复 {preview.duplicateCount} 条。服务端结果为最终权威。
        </div>
      )}
      {localError && <p role="alert" className="mt-3 rounded-xl border border-red-900/70 bg-red-950/30 px-4 py-3 text-sm text-red-300">{localError}</p>}
      {summary?.result && (
        <div className="mt-3 rounded-xl border border-cyan-800 bg-cyan-950/30 px-4 py-3 text-sm leading-6 text-cyan-100" aria-live="polite">
          导入完成：版本 v{summary.version}，当前 {summary.memberCount} 人；重复 {summary.duplicateCount}、新增 {summary.addedCount}、移除 {summary.removedCount}，其中已绑定但被移出 {summary.removedBoundCount} 人。
        </div>
      )}
      {!canImport && bootstrap && (
        <p className="mt-3 text-sm leading-6 text-amber-300">首份名单已经导入。请继续绑定名单内 QQ，不能在受限会话中再次替换名单。</p>
      )}
      <button
        type="button"
        onClick={submit}
        disabled={!preview || pending || !canImport}
        className="mt-4 min-h-11 w-full rounded-xl bg-cyan-500 px-4 text-sm font-black text-gray-950 hover:bg-cyan-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"
      >
        {pending ? "正在由服务端校验并替换..." : bootstrap ? "确认并初始化白名单" : "确认全量替换白名单"}
      </button>
      <p className="mt-3 text-xs leading-5 text-amber-200/80">原始 JSON 仅用于本次导入校验，不作为审计副本保存；审计仅保留管理员、时间、版本和人数摘要。</p>
    </section>
  );
}
