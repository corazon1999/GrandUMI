"use client";

import Modal from "@/components/ui/Modal";
import {
  CHANGELOG,
  type ChangelogCategory,
} from "@/data/changelog";

interface Props {
  open: boolean;
  onClose: () => void;
}

const CATEGORY_STYLE: Record<ChangelogCategory, string> = {
  新增: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  修复: "border-rose-500/40 bg-rose-500/10 text-rose-300",
  优化: "border-sky-500/40 bg-sky-500/10 text-sky-300",
};

export default function ChangelogModal({ open, onClose }: Props) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="更新日志"
      maxWidthClass="max-w-xl"
      mobileSheet
    >
      <div className="max-h-[65cqh] space-y-6 overflow-y-auto pr-2">
        {CHANGELOG.map((entry, index) => (
          <section
            key={entry.id}
            className={
              index === 0
                ? "rounded-xl border border-orange-500/30 bg-orange-500/5 p-4"
                : "border-t border-gray-800 pt-5"
            }
          >
            <div className="mb-4 flex flex-wrap items-start justify-between gap-2">
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="font-bold text-white">{entry.title}</h3>
                  {index === 0 && (
                    <span className="rounded-full bg-orange-500 px-2 py-0.5 text-[10px] font-bold text-white">
                      最新
                    </span>
                  )}
                </div>
                <p className="mt-1 text-xs text-gray-500">{entry.date}</p>
              </div>
              <span className="rounded-md border border-gray-700 bg-gray-950 px-2 py-1 font-mono text-xs text-orange-300">
                v{entry.version}
              </span>
            </div>

            <div className="space-y-4">
              {entry.sections.map((section) => (
                <div key={section.category} className="flex items-start gap-3">
                  <span
                    className={`mt-0.5 shrink-0 rounded border px-2 py-0.5 text-[11px] font-bold ${CATEGORY_STYLE[section.category]}`}
                  >
                    {section.category}
                  </span>
                  <ul className="min-w-0 space-y-2 text-sm leading-6 text-gray-300">
                    {section.items.map((item) => (
                      <li key={item} className="flex gap-2">
                        <span className="text-gray-600">•</span>
                        <span>{item}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>

      <div className="mt-5 flex justify-end border-t border-gray-800 pt-4">
        <button
          type="button"
          onClick={onClose}
          className="rounded-lg bg-orange-500 px-5 py-2 text-sm font-bold text-white transition-colors hover:bg-orange-400"
        >
          我知道了
        </button>
      </div>
    </Modal>
  );
}
