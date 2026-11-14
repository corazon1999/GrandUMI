export interface TournamentStatusMeta {
  label: string;
  accent: string;
  soft: string;
  strong: string;
}

const FALLBACK_STATUS: TournamentStatusMeta = {
  label: "其他状态",
  accent: "#94a3b8",
  soft: "#1e293b",
  strong: "#e2e8f0",
};

/** 赛事大厅使用同一组颜色语义，文字仍保留以满足色觉差异和无障碍阅读。 */
export const TOURNAMENT_STATUS_META: Record<string, TournamentStatusMeta> = {
  pending: { label: "待审核", accent: "#a78bfa", soft: "#2e1065", strong: "#ddd6fe" },
  rejected: { label: "未通过", accent: "#fb7185", soft: "#4c0519", strong: "#fecdd3" },
  approved: { label: "报名与签到", accent: "#38bdf8", soft: "#082f49", strong: "#bae6fd" },
  swiss: { label: "进行中 · 瑞士轮", accent: "#34d399", soft: "#064e3b", strong: "#a7f3d0" },
  cut: { label: "进行中 · 淘汰赛", accent: "#f59e0b", soft: "#451a03", strong: "#fde68a" },
  completed: { label: "已结束", accent: "#94a3b8", soft: "#1e293b", strong: "#e2e8f0" },
  cancelled: { label: "已取消", accent: "#f43f5e", soft: "#4c0519", strong: "#fecdd3" },
};

export const tournamentStatusMeta = (status: string) =>
  TOURNAMENT_STATUS_META[status] ?? { ...FALLBACK_STATUS, label: status || FALLBACK_STATUS.label };

export const TOURNAMENT_LIST_STATUS_KEYS = [
  "pending",
  "rejected",
  "approved",
  "swiss",
  "cut",
  "completed",
  "cancelled",
] as const;
