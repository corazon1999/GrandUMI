"use client";

import { useState } from "react";
import { useDeckStore } from "@/store/deckStore";
import {
  COLOR_DISPLAY_NAMES,
  COLOR_STYLES,
} from "@/lib/colorMap";

const PROPERTIES = ["", "斩", "打", "射", "智", "特"];
const DECK_TYPES  = ["", "Character", "Stage", "Event"];

const TYPE_LABELS: Record<string, string> = {
  Character: "角色", Stage: "场地", Event: "事件",
};
const RARITIES    = ["", "L", "SR", "R", "UC", "C", "SEC", "P"];
const COSTS       = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
const COL_PRESETS = [4, 5, 6, 7, 8, 9, 10, 12];


// 按弹数筛选用的卡集列表（与 cardSets.ts 同步；新卡集发售时手动追加）
const SET_GROUPS: { label: string; sets: string[] }[] = [
  { label: "OP 主弹", sets: ["OP01","OP02","OP03","OP04","OP05","OP06","OP07","OP08","OP09","OP10","OP11","OP12","OP13","OP14","OP15","OP16"] },
  { label: "ST 起始", sets: ["ST01","ST02","ST03","ST04","ST05","ST06","ST07","ST08","ST09","ST10","ST11","ST12","ST13","ST14","ST15","ST16","ST17","ST18","ST19","ST20","ST21","ST22","ST23","ST24","ST25","ST26","ST27","ST28","ST29","ST30","ST31","ST32","ST33","ST34","ST35"] },
  { label: "EB/PRB", sets: ["EB01","EB02","EB03","EB04","PRB01","PRB02"] },
  { label: "P/其他", sets: ["P"] },
];

export default function SearchPanel() {
  const {
    leader,
    searchQuery, filterColor, filterType, filterProperty, filterRarity, filterCost,
    filterSets, filterShowSub1,
    gridColumns,
    setSearchQuery, setFilterColor, setFilterType, setFilterProperty, setFilterRarity, setFilterCost,
    toggleFilterSet, clearFilterSets, setFilterShowSub1,
    setGridColumns,
  } = useDeckStore();

  const [showSettings, setShowSettings] = useState(false);
  const [setGroupExpanded, setSetGroupExpanded] = useState<number | null>(null);

  const isLeaderMode        = filterType === "Leader";
  // 数据层颜色已统一为标准色，filterColor 直接即显示色
  const activeDisplayColor  = filterColor;
  const hasFilter           = !!(searchQuery || filterColor || filterType || filterProperty || filterRarity || filterCost !== null || filterSets.length > 0 || filterShowSub1);

  // 已选领航且非领航模式时，颜色筛选收缩为领航拥有的颜色（双色剩 2、单色剩 1）；否则显示全部 6 色
  const leaderDataColors    = leader && !isLeaderMode ? leader.color.split("/") : null;
  const visibleColorNames   = leaderDataColors
    ? COLOR_DISPLAY_NAMES.filter((name) => leaderDataColors.includes(name))
    : COLOR_DISPLAY_NAMES;
  // 单色领航时「全部」与该色等价，隐藏以免冗余
  const showAllColorBtn     = !(leaderDataColors && leaderDataColors.length === 1);

  return (
    <div className="flex flex-col h-full">
      {/* 标题 */}
      <div className="px-3 pt-3 pb-2 border-b border-gray-800 shrink-0">
        <h2 className="text-white font-bold text-sm">搜索卡牌</h2>
      </div>

      {/* 筛选区（可滚动） */}
      <div className="flex-1 overflow-y-auto">
        <div className="flex flex-col gap-3 p-3">
          {/* 搜索框 */}
          <input
            className="w-full bg-gray-800 text-white text-xs rounded-lg px-2.5 py-2 outline-none border border-gray-700 focus:border-orange-500 transition-colors"
            placeholder="卡名 / 关键词 / 效果..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />

          {/* 领航卡切换 */}
          <button
            onClick={() => setFilterType(isLeaderMode ? "" : "Leader")}
            className={`w-full py-1.5 rounded-lg text-xs font-bold transition-colors border ${
              isLeaderMode
                ? "bg-yellow-500 text-black border-yellow-400"
                : "bg-gray-800 text-gray-400 border-gray-700 hover:text-white"
            }`}
          >
            {isLeaderMode ? "✦ 领航模式（退出）" : "选择领航卡"}
          </button>

          {/* 普通类型 */}
          {!isLeaderMode && (
            <div className="flex flex-col gap-1">
              <label className="text-gray-500 text-[10px]">类型</label>
              <div className="flex flex-wrap gap-1">
                {DECK_TYPES.map((t) => (
                  <button key={t} onClick={() => setFilterType(t)}
                    className={`px-2 py-0.5 rounded text-[10px] transition-colors ${
                      filterType === t
                        ? "bg-orange-500 text-white"
                        : "bg-gray-800 text-gray-400 hover:text-white"
                    }`}>
                    {t ? TYPE_LABELS[t] ?? t : "全部"}
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* 颜色 */}
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-[10px]">颜色</label>
            <div className="flex flex-wrap gap-1">
              {showAllColorBtn && (
                <button onClick={() => setFilterColor("")}
                  className={`px-2 py-0.5 rounded text-[10px] transition-colors ${
                    filterColor === ""
                      ? "bg-orange-500 text-white"
                      : "bg-gray-800 text-gray-400 hover:text-white"
                  }`}>
                  全部
                </button>
              )}
              {visibleColorNames.map((name) => {
                const dataValue = name;
                const styles    = COLOR_STYLES[name];
                const isActive  = activeDisplayColor === name;
                return (
                  <button key={name} onClick={() => setFilterColor(dataValue)}
                    className={`px-2 py-0.5 rounded text-[10px] font-bold transition-all ${
                      isActive
                        ? `${styles.bg} text-white scale-105`
                        : `bg-gray-800 ${styles.text} hover:scale-105`
                    }`}>
                    {name}
                  </button>
                );
              })}
            </div>
          </div>

          {/* 属性 */}
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-[10px]">属性</label>
            <div className="flex flex-wrap gap-1">
              {PROPERTIES.map((p) => (
                <button key={p} onClick={() => setFilterProperty(p)}
                  className={`px-2 py-0.5 rounded text-[10px] transition-colors ${
                    filterProperty === p
                      ? "bg-orange-500 text-white"
                      : "bg-gray-800 text-gray-400 hover:text-white"
                  }`}>
                  {p || "全部"}
                </button>
              ))}
            </div>
          </div>

          {/* 费用（领航卡无费用，仅普通模式显示） */}
          {!isLeaderMode && (
            <div className="flex flex-col gap-1">
              <label className="text-gray-500 text-[10px]">费用</label>
              <div className="flex flex-wrap gap-1">
                <button onClick={() => setFilterCost(null)}
                  className={`px-2 py-0.5 rounded text-[10px] transition-colors ${
                    filterCost === null
                      ? "bg-orange-500 text-white"
                      : "bg-gray-800 text-gray-400 hover:text-white"
                  }`}>
                  全部
                </button>
                {COSTS.map((c) => (
                  <button key={c} onClick={() => setFilterCost(c)}
                    className={`w-6 py-0.5 rounded text-[10px] transition-colors ${
                      filterCost === c
                        ? "bg-orange-500 text-white"
                        : "bg-gray-800 text-gray-400 hover:text-white"
                    }`}>
                    {c}
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* 稀有度 */}
          <div className="flex flex-col gap-1">
            <label className="text-gray-500 text-[10px]">稀有度</label>
            <div className="flex flex-wrap gap-1">
              {RARITIES.map((r) => (
                <button key={r} onClick={() => {
                  setFilterRarity(r);
                  if (r === "L") setFilterType("Leader");
                }}
                  className={`px-2 py-0.5 rounded text-[10px] transition-colors ${
                    filterRarity === r
                      ? "bg-orange-500 text-white"
                      : "bg-gray-800 text-gray-400 hover:text-white"
                  }`}>
                  {r || "全部"}
                </button>
              ))}
            </div>
          </div>

          {/* 弹数筛选（按组展开，多选） */}
          <div className="flex flex-col gap-1">
            <div className="flex items-center justify-between">
              <label className="text-gray-500 text-[10px]">弹数</label>
              {filterSets.length > 0 && (
                <button onClick={clearFilterSets}
                  className="text-gray-600 hover:text-orange-400 text-[9px]">清空</button>
              )}
            </div>
            <div className="flex flex-col gap-1">
              {SET_GROUPS.map((g, gi) => {
                const isOpen = setGroupExpanded === gi;
                const selectedInGroup = g.sets.filter((s) => filterSets.includes(s)).length;
                return (
                  <div key={g.label} className="flex flex-col gap-1">
                    <button
                      onClick={() => setSetGroupExpanded(isOpen ? null : gi)}
                      className={`w-full text-left px-2 py-1 rounded text-[10px] font-bold transition-colors flex items-center justify-between ${
                        selectedInGroup > 0
                          ? "bg-gray-800 text-orange-300"
                          : "bg-gray-800 text-gray-400 hover:text-white"
                      }`}>
                      <span>{g.label} {selectedInGroup > 0 && `(${selectedInGroup})`}</span>
                      <span className="text-gray-600">{isOpen ? "▾" : "▸"}</span>
                    </button>
                    {isOpen && (
                      <div className="flex flex-wrap gap-0.5 pl-1">
                        {g.sets.map((s) => (
                          <button key={s} onClick={() => toggleFilterSet(s)}
                            className={`px-1.5 py-0.5 rounded text-[9px] font-bold transition-colors ${
                              filterSets.includes(s)
                                ? "bg-orange-500 text-white"
                                : "bg-gray-700 text-gray-400 hover:text-white"
                            }`}>
                            {s}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          {/* 角标=1 切换（默认隐藏，按下显示） */}
          <button
            onClick={() => setFilterShowSub1(!filterShowSub1)}
            className={`w-full py-1.5 rounded-lg text-[10px] font-bold transition-colors border ${
              filterShowSub1
                ? "bg-blue-600/40 text-blue-200 border-blue-600"
                : "bg-gray-800 text-gray-500 border-gray-700 hover:text-white"
            }`}
            title="角标=1 通常是旧环境/早期版本卡，默认隐藏"
          >
            {filterShowSub1 ? "✓ 显示角标 1 卡" : "已隐藏角标 1 卡"}
          </button>

          {/* 清除筛选 */}
          {hasFilter && (
            <button
              onClick={() => {
                setSearchQuery(""); setFilterColor(""); setFilterType(""); setFilterProperty(""); setFilterRarity(""); setFilterCost(null);
                clearFilterSets(); setFilterShowSub1(false);
              }}
              className="text-gray-600 hover:text-white text-[10px] transition-colors text-center"
            >
              清除筛选
            </button>
          )}
        </div>
      </div>

      {/* 底部设置区 */}
      <div className="border-t border-gray-800 shrink-0">
        {/* 设置面板（展开时显示在按钮上方） */}
        {showSettings && (
          <div className="px-3 py-2.5 bg-gray-900 border-b border-gray-800">
            <p className="text-gray-400 text-[10px] mb-2">每行卡牌数</p>

            {/* 快捷预设 */}
            <div className="flex flex-wrap gap-1 mb-2">
              {COL_PRESETS.map((n) => (
                <button key={n} onClick={() => setGridColumns(n)}
                  className={`w-7 h-6 rounded text-[10px] font-bold transition-colors ${
                    gridColumns === n
                      ? "bg-orange-500 text-white"
                      : "bg-gray-800 text-gray-400 hover:text-white"
                  }`}>
                  {n}
                </button>
              ))}
            </div>

            {/* 微调 +/- */}
            <div className="flex items-center gap-2">
              <button onClick={() => setGridColumns(gridColumns - 1)}
                disabled={gridColumns <= 4}
                className="w-7 h-7 rounded bg-gray-800 text-white text-base font-bold disabled:opacity-30 hover:bg-gray-700 transition-colors">
                −
              </button>
              <span className="flex-1 text-center text-white font-bold text-sm">
                {gridColumns} 列
              </span>
              <button onClick={() => setGridColumns(gridColumns + 1)}
                disabled={gridColumns >= 16}
                className="w-7 h-7 rounded bg-gray-800 text-white text-base font-bold disabled:opacity-30 hover:bg-gray-700 transition-colors">
                +
              </button>
            </div>
          </div>
        )}

        {/* 设置按钮 */}
        <button
          onClick={() => setShowSettings((s) => !s)}
          className={`w-full flex items-center gap-1.5 px-3 py-2 text-[10px] transition-colors ${
            showSettings
              ? "text-orange-400 bg-gray-900"
              : "text-gray-500 hover:text-white hover:bg-gray-900"
          }`}
        >
          <span className="text-xs">⚙</span>
          <span>显示设置</span>
          <span className="ml-auto text-gray-600">{gridColumns} 列</span>
        </button>
      </div>
    </div>
  );
}
