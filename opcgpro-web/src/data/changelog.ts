export type ChangelogCategory = "新增" | "修复" | "优化";

export interface ChangelogSection {
  category: ChangelogCategory;
  items: string[];
}

export interface ChangelogEntry {
  /** 每次发布必须使用新的唯一 ID，用于判断玩家是否已阅读本次更新。 */
  id: string;
  version: string;
  date: string;
  title: string;
  sections: ChangelogSection[];
}

/**
 * 更新日志按时间倒序排列，最新版本放在第一项。
 * 发布新版本时新增一项并使用新的 id，玩家下次登录便会自动看到更新内容。
 */
export const CHANGELOG: ChangelogEntry[] = [
  {
    id: "2026-08-06-changelog",
    version: "2026.08.06",
    date: "2026-08-06",
    title: "更新日志上线",
    sections: [
      {
        category: "新增",
        items: [
          "新增更新日志窗口，集中展示新功能、问题修复与体验优化。",
          "每次版本更新后，玩家首次登录主页会自动看到本次更新内容。",
          "主页左侧新增“更新日志”入口，可随时回顾历史更新。",
        ],
      },
    ],
  },
];

export const LATEST_CHANGELOG = CHANGELOG[0];
