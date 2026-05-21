"use client";

import { useState } from "react";
import { useDeckStore } from "@/store/deckStore";
import {
  COLOR_DISPLAY_NAMES,
  COLOR_DISPLAY_TO_DATA,
  COLOR_DATA_TO_DISPLAY,
  COLOR_STYLES,
} from "@/lib/colorMap";

const PROPERTIES = ["", "斩", "打", "射", "智", "特"];
const DECK_TYPES  = ["", "Character", "Stage", "Event"];

const TYPE_LABELS: Record<string, string> = {
  Character: "角色", Stage: "场地", Event: "事件",
};
const RARITIES    = ["", "L", "SR", "R", "UC", "C", "SEC", "P"];
const COL_PRESETS = [4, 5, 6, 7, 8, 9, 10, 12];


export default function SearchPanel() {
  const {
    searchQuery, filterColor, filterType, filterProperty, filterRarity,
    gridColumns,
    setSearchQuery, setFilterColor, setFilterType, setFilterProperty, setFilterRarity,
    setGridColumns,
  } = useDeckStore();

  const [showSettings, setShowSettings] = useState(false);

  const isLeaderMode        = filterType === "Leader";
  const activeDisplayColor  = COLOR_DATA_TO_DISPLAY[filterColor] ?? "";
  const hasFilter           = !!(searchQuery || filterColor || filterType || filterProperty || filterRarity);

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
              <button onClick={() => setFilterColor("")}
                className={`px-2 py-0.5 rounded text-[10px] transition-colors ${
                  filterColor === ""
                    ? "bg-orange-500 text-white"
                    : "bg-gray-800 text-gray-400 hover:text-white"
                }`}>
                全部
              </button>
              {COLOR_DISPLAY_NAMES.map((name) => {
                const dataValue = COLOR_DISPLAY_TO_DATA[name];
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

          {/* 清除筛选 */}
          {hasFilter && (
            <button
              onClick={() => { setSearchQuery(""); setFilterColor(""); setFilterType(""); setFilterProperty(""); setFilterRarity(""); }}
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
