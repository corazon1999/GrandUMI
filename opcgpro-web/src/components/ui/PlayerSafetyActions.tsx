"use client";

import { useState, type FormEvent } from "react";
import Modal from "@/components/ui/Modal";
import { HomeRequest } from "@/net/HomeProtocol";
import type { PlayerReportCategory } from "@/types/net";
import { useLayoutQuarterTurn } from "./ResponsiveScope";

const REPORT_CATEGORIES: ReadonlyArray<{
  value: PlayerReportCategory;
  label: string;
  help: string;
}> = [
  {
    value: "harassment",
    label: "言语辱骂或不当信息",
    help: "侮辱、骚扰、威胁，或使用违规昵称与不当内容",
  },
  {
    value: "stalling",
    label: "恶意拖延或挂机",
    help: "反复耗尽操作时间、长时间无操作或故意消极对局",
  },
  {
    value: "cheating",
    label: "疑似作弊或利用漏洞",
    help: "异常获知隐藏信息、篡改对局，或故意利用漏洞获利",
  },
  {
    value: "spam",
    label: "刷屏、广告或引流",
    help: "重复发送无关内容、广告、群聊或外部平台引流信息",
  },
  {
    value: "other",
    label: "其他违规行为",
    help: "不属于以上分类，但确实影响公平对局或社区环境",
  },
];

export default function PlayerSafetyActions({
  targetAccount,
  targetName,
  currentOpponent = false,
  compact = false,
}: {
  targetAccount?: string;
  targetName: string;
  currentOpponent?: boolean;
  compact?: boolean;
}) {
  const [confirmBlock, setConfirmBlock] = useState(false);
  const [reportOpen, setReportOpen] = useState(false);
  const [category, setCategory] = useState<PlayerReportCategory>("harassment");
  const [description, setDescription] = useState("");
  const [submitError, setSubmitError] = useState("");
  const rotateQuarterTurn = useLayoutQuarterTurn();

  const selectedCategory = REPORT_CATEGORIES.find((item) => item.value === category) ?? REPORT_CATEGORIES[0];

  const block = () => {
    if (!confirmBlock) {
      setConfirmBlock(true);
      return;
    }
    HomeRequest.blockPlayer(targetAccount, currentOpponent);
    setConfirmBlock(false);
  };

  const report = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalized = description.trim();
    if (normalized.length < 2) {
      setSubmitError("请至少填写 2 个字，简单说明发生了什么");
      return;
    }
    if (!HomeRequest.reportPlayer(normalized, category, targetAccount, currentOpponent)) {
      setSubmitError("当前网络未连接，请重连后再提交");
      return;
    }
    setDescription("");
    setSubmitError("");
    setReportOpen(false);
  };

  return (
    <>
      <div className="flex flex-wrap justify-end gap-1" aria-label={`${targetName} 的安全操作`}>
        <button
          type="button"
          onClick={block}
          onBlur={() => setConfirmBlock(false)}
          className={`min-h-12 min-w-12 rounded-lg border px-2 text-xs font-bold transition-colors ${confirmBlock ? "border-red-400 bg-red-950 text-red-200" : "border-gray-700 text-gray-400 hover:border-red-700 hover:text-red-300"}`}
        >
          {confirmBlock ? "确认屏蔽" : compact ? "屏蔽" : "屏蔽玩家"}
        </button>
        <button
          type="button"
          onClick={() => {
            setSubmitError("");
            setReportOpen(true);
          }}
          className="min-h-12 min-w-12 rounded-lg border border-amber-800/80 px-2 text-xs font-bold text-amber-300 transition-colors hover:bg-amber-950"
          aria-label={`举报玩家 ${targetName}`}
        >
          举报
        </button>
      </div>

      <Modal
        open={reportOpen}
        onClose={() => setReportOpen(false)}
        title={`举报 ${targetName}`}
        mobileSheet
        maxWidthClass={rotateQuarterTurn ? "max-w-4xl" : "max-w-md"}
      >
        <form className={rotateQuarterTurn ? "grid grid-cols-2 items-start gap-3" : "space-y-3"} onSubmit={report}>
          <div className="space-y-3">
            <p className="rounded-lg border border-sky-400/20 bg-sky-950/40 px-3 py-2 text-xs leading-5 text-sky-100">
              系统会自动附带本局编号、回合、阶段、计时与最近局内聊天，帮助管理员核查；请勿填写账号密码等隐私信息。
            </p>
            <label className="block text-xs font-bold text-gray-400">
              举报类型
              <select
                value={category}
                onChange={(event) => {
                  setCategory(event.target.value as PlayerReportCategory);
                  setSubmitError("");
                }}
                className="mt-1 h-12 w-full rounded-xl border border-gray-700 bg-gray-950 px-3 text-base text-white"
              >
                {REPORT_CATEGORIES.map((item) => (
                  <option key={item.value} value={item.value}>{item.label}</option>
                ))}
              </select>
              <span className="mt-1 block font-normal leading-5 text-gray-500">{selectedCategory.help}</span>
            </label>
          </div>
          <div className="space-y-3">
            <label className="block text-xs font-bold text-gray-400">
              具体说明
              <textarea
                value={description}
                onChange={(event) => {
                  setDescription(event.target.value);
                  setSubmitError("");
                }}
                minLength={2}
                maxLength={1000}
                rows={rotateQuarterTurn ? 2 : 4}
                required
                placeholder="例如：对手连续多个回合故意耗尽操作时间"
                className="mt-1 w-full resize-none rounded-xl border border-gray-700 bg-gray-950 p-3 text-base text-white outline-none placeholder:text-gray-600 focus:border-amber-500"
              />
              <span className="mt-1 flex justify-between gap-3 font-normal text-gray-500">
                <span>请描述可核查的具体行为</span>
                <span className="tabular-nums">{description.length}/1000</span>
              </span>
            </label>
            {submitError && <p role="alert" className="text-sm text-red-300">{submitError}</p>}
            <button
              type="submit"
              disabled={description.trim().length < 2}
              className="min-h-12 w-full rounded-xl bg-amber-500 px-4 text-sm font-black text-slate-950 transition-colors hover:bg-amber-400 disabled:bg-gray-800 disabled:text-gray-600"
            >
              提交举报
            </button>
          </div>
        </form>
      </Modal>
    </>
  );
}
