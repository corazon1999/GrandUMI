"use client";

import Modal from "@/components/ui/Modal";

export interface PendingCounterEvent {
  handIndex: number;
  cardNumber: string;
  cardName: string;
  cost: number;
}

interface Props {
  pending: PendingCounterEvent | null;
  onCancel: () => void;
  onConfirm: () => void;
  mode?: "counter" | "main";
}

export default function CounterEventConfirmModal({
  pending,
  onCancel,
  onConfirm,
  mode = "counter",
}: Props) {
  const isMain = mode === "main";
  return (
    <Modal
      open={pending !== null}
      onClose={onCancel}
      title={isMain ? "确认使用主要事件" : "确认使用反击事件"}
      maxWidthClass="max-w-sm"
      mobileSheet
    >
      {pending && (
        <div className="space-y-4">
          <p className="text-sm leading-6 text-gray-200">
            确定打出“{pending.cardName}”作为{isMain ? "主要" : "反击"}事件吗？
          </p>
          <p className="rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs leading-5 text-amber-200">
            将支付 {pending.cost} 张活跃咚!!，卡牌随后进入废弃区；确认后无法撤销。
          </p>
          <div className="grid grid-cols-2 gap-3">
            <button
              type="button"
              onClick={onCancel}
              className="min-h-12 rounded-xl border border-gray-600 px-4 text-sm font-bold text-gray-200 hover:bg-gray-800"
            >
              取消
            </button>
            <button
              type="button"
              onClick={onConfirm}
              className="min-h-12 rounded-xl bg-amber-500 px-4 text-sm font-black text-black hover:bg-amber-400"
            >
              确认打出
            </button>
          </div>
        </div>
      )}
    </Modal>
  );
}
