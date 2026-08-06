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
    id: "2026-08-06-starting-player-card-fixes",
    version: "2026.08.06.2",
    date: "2026-08-06",
    title: "骰点先后手与卡牌效果修复",
    sections: [
      {
        category: "新增",
        items: [
          "对局开局改为双方投掷六面骰决定选择权：点数较大者可选择先手或后手，点数相同时自动重新投掷。",
          "新增开局投骰动画、先后手选择界面，以及等待对手选择的状态提示。",
          "补齐 OP15-020「火拳」的完整主要效果，包括力量变化、可选弃牌与后续 K.O. 结算。",
        ],
      },
      {
        category: "修复",
        items: [
          "修复 OP08-058「夏洛特·布玲」领袖费用错误显示为“-”的问题，现正确显示为 4。",
          "修复 OP14-110、OP14-111 的触发效果缺失问题，现可从废弃区选择符合条件的《恐怖之船海盗团》角色以休息状态登场。",
          "修复 OP15-002「路西」抽牌条件与发动时机错误；现需在本回合已经发动过原始费用不低于 3 的事件后，发动启动主要效果抽 1 张牌。",
          "修复 OP16-015「蒙奇·D·路飞」错误调整当前总力量的问题；现会正确将原本力量变为 7000，并与其它力量加减效果叠加。",
          "修复 OP17-021、OP17-032、OP17-033、OP17-037 无法识别包含《红发海盗团》的复合特征问题。",
        ],
      },
      {
        category: "优化",
        items: [
          "观战时现在可以查看双方咚!!区域，更容易理解当前费用与赋予状态。",
          "先后手骰点与选择结果现会写入对局日志和重放数据，断线恢复及对局重建更加准确。",
        ],
      },
    ],
  },
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
