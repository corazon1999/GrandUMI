import { useId } from "react";
import styles from "./HunterIdentity.module.css";

export type SeasonTitleEffect = "solar" | "ember" | "fleet" | "ice" | "eclipse" | "constellation";

/** 纹饰与主徽章共用色板；只旋转内部刻度，雕花和称号文字保持稳定。 */
export default function SeasonTitleOrnament({ effect }: { effect: SeasonTitleEffect }) {
  const id = useId().replace(/:/g, "");
  const metal = `${id}-engraving`, glow = `${id}-aura`;
  const ink = `url(#${metal})`;
  const celestial = effect === "eclipse" || effect === "constellation";
  return <svg viewBox="0 0 240 120" fill="none" aria-hidden="true" focusable="false"
    data-title-ornament={effect} className={styles.titleOrnament}>
    <defs>
      <linearGradient id={metal} x1="0" y1="15" x2="240" y2="105" gradientUnits="userSpaceOnUse">
        <stop stopColor="rgb(var(--accent))" stopOpacity=".18"/>
        <stop offset=".28" stopColor="rgb(var(--light))" stopOpacity=".85"/>
        <stop offset=".5" stopColor="rgb(var(--accent))" stopOpacity=".45"/>
        <stop offset=".72" stopColor="rgb(var(--light))" stopOpacity=".85"/>
        <stop offset="1" stopColor="rgb(var(--accent))" stopOpacity=".18"/>
      </linearGradient>
      <radialGradient id={glow}>
        <stop stopColor="rgb(var(--accent))" stopOpacity=".2"/>
        <stop offset=".7" stopColor="rgb(var(--accent))" stopOpacity=".06"/>
        <stop offset="1" stopColor="rgb(var(--accent))" stopOpacity="0"/>
      </radialGradient>
    </defs>
    <ellipse cx="120" cy="58" rx="116" ry="58" fill={`url(#${glow})`}/>
    {/* 两侧雕花由内向外收束，窄卡片裁切时仍保留完整的主环。 */}
    {[false, true].map(mirror => <g key={String(mirror)} transform={mirror ? "translate(240 0) scale(-1 1)" : undefined}
      stroke={ink} strokeLinecap="round" strokeLinejoin="round">
      <path d="M73 58H57c-8 0-9-11-17-11-7 0-9 9-3 12 6 3 10-5 3-7M60 58c-12 0-18 17-32 17-8 0-12-7-8-12 4-4 10 0 7 4" strokeWidth=".9"/>
      <path d="M65 51c-10-17-29-18-42-8M62 65c-10 18-30 19-43 8M50 49l-6-11m9 34-5 9M18 55l8-6m-8 6 8 6" strokeWidth=".65" opacity=".65"/>
      <path d="m47 41-8-6 3 8 5-2Zm-7 31-9 7 10-1-1-6Zm-14-28-8-1 6 5 2-4Z" fill={ink} strokeWidth=".45"/>
      <path d="M6 58h7m-3-3v6M55 58l4-4 4 4-4 4-4-4Z" strokeWidth=".85"/>
      <circle cx="15" cy="58" r="1.5" fill={ink} stroke="none"/>
      <path d="M28 27h17l7 7M25 90h20l7-7" strokeWidth=".55" opacity=".4"/>
    </g>)}
    <g stroke={ink} strokeLinejoin="round">
      {effect === "solar" ? <>
        <circle cx="120" cy="58" r="45" strokeWidth=".7"/>
        <circle cx="120" cy="58" r="50" strokeWidth=".5" strokeDasharray="22 4 2 4"/>
        {Array.from({ length: 12 }, (_, i) => <g key={i} transform={`rotate(${i * 30} 120 58)`}>
          <path d="m116 13 4-9 4 9-4-2-4 2Z" fill={ink} strokeWidth=".45"/>
          <path d="M120 16v6m-5-10-3-4m13 4 3-4" strokeWidth=".65"/>
        </g>)}
        <path d="M85 85c-3-5-7-6-9-10 5 0 8 3 9 7m3 8c-3-4-6-4-7-8 4 0 7 3 7 6m67-3c3-5 7-6 9-10-5 0-8 3-9 7m-3 8c3-4 6-4 7-8-4 0-7 3-7 6" strokeWidth=".8"/>
      </> : effect === "ember" ? <>
        <path d="m93 16-19 19v46l19 19h54l19-19V35l-19-19H93Z" strokeWidth="1"/>
        <path d="m96 22-16 16v40l16 16h48l16-16V38l-16-16H96Z" strokeWidth=".55"/>
        {[0, 90, 180, 270].map(angle => <g key={angle} transform={`rotate(${angle} 120 58)`}>
          <path d="m114 17 6-6 6 6-6 6-6-6Z" fill={ink} strokeWidth=".45"/>
          <path d="m113 28 7-4 7 4m-16-9-6-4m24 4 6-4" strokeWidth=".7"/>
          <path d="M105 16c-5 5-8 0-7-4-5 8-1 12 3 12m34-8c5 5 8 0 7-4 5 8 1 12-3 12" strokeWidth=".7"/>
        </g>)}
        <circle cx="120" cy="58" r="39" strokeWidth=".45" strokeDasharray="2 5"/>
      </> : effect === "fleet" ? <>
        <circle cx="120" cy="58" r="46" strokeWidth=".7"/>
        <path d="M88 27a45 45 0 0 1 64 0M88 89a45 45 0 0 0 64 0" strokeWidth="2"/>
        {[0, 90, 180, 270].map(angle => <g key={angle} transform={`rotate(${angle} 120 58)`}>
          <path d="m120 5-5 16 5-3 5 3-5-16Z" fill={ink} strokeWidth=".65"/>
          <path d="M111 13h-8m26 0h8m-17 9v4" strokeWidth=".55"/>
        </g>)}
        <path d="M75 80c7-4 8 4 15 0m60 0c7-4 8 4 15 0M92 98c7-4 8 4 15 0m26 0c7-4 8 4 15 0" strokeWidth=".7"/>
      </> : effect === "ice" ? <>
        <path d="m120 5 37 16 16 37-16 37-37 16-37-16-16-37 16-37 37-16Z" strokeWidth=".8"/>
        <path d="m120 13 32 13 13 32-13 32-32 13-32-13-13-32 13-32 32-13Z" strokeWidth=".45"/>
        {[0, 45, 90, 135, 180, 225, 270, 315].map(angle => <g key={angle} transform={`rotate(${angle} 120 58)`}>
          <path d="m120 5-4 10 4 11 4-11-4-10Zm0 10v11M111 17l9 9 9-9" strokeWidth=".55"/>
          <path d="m117 36 3-5 3 5-3 5-3-5Z" fill={ink} strokeWidth=".45"/>
        </g>)}
      </> : effect === "eclipse" ? <>
        <circle cx="120" cy="58" r="46" strokeWidth=".8"/>
        <ellipse cx="120" cy="58" rx="53" ry="32" transform="rotate(-30 120 58)" strokeWidth=".65"/>
        <path d="M147 17a47 47 0 0 1-27 88m-28-84a47 47 0 0 0 0 74" strokeWidth="1.6"/>
        <path d="m120 6 3 6 7 2-7 2-3 6-3-6-7-2 7-2 3-6ZM77 51l2 5 5 2-5 2-2 5-2-5-5-2 5-2 2-5Z" fill={ink} strokeWidth=".4"/>
        <path d="M153 21c-7 0-10 8-5 12-1-7 4-10 5-12Z" fill={ink} strokeWidth=".4"/>
        <circle cx="162" cy="82" r="2" fill={ink}/>
      </> : <>
        <circle cx="120" cy="58" r="48" strokeWidth=".6"/>
        <ellipse cx="120" cy="58" rx="52" ry="34" transform="rotate(24 120 58)" strokeWidth=".55"/>
        <path d="m120 10 46 33-18 54H92L74 43l46-33Zm0 0 28 87-74-54h92L92 97l28-87Z" strokeWidth=".5" opacity=".6"/>
        {[[120, 10], [166, 43], [148, 97], [92, 97], [74, 43]].map(([x, y]) => <g key={`${x}-${y}`}>
          <circle cx={x} cy={y} r="4" strokeWidth=".6"/>
          <path d={`m${x} ${y - 3} 1 2 2 1-2 1-1 2-1-2-2-1 2-1 1-2Z`} fill={ink} strokeWidth=".35"/>
        </g>)}
      </>}
    </g>
    <g data-ornament-motion className={`${styles.ornamentWheel} ${celestial ? styles.ornamentReverse : ""}`} stroke={ink}>
      {Array.from({ length: 32 }, (_, i) => <path key={i} d={i % 4 ? "M120 18v2" : "M120 16v5"}
        strokeWidth={i % 4 ? .45 : .8} transform={`rotate(${i * 11.25} 120 58)`}/>)}
      {[0, 180].map(angle => <g key={angle} transform={`rotate(${angle} 120 58)`}>
        <path d="M151 34a39 39 0 0 1 8 24" strokeWidth="1.2"/>
        <circle cx="159" cy="58" r="1.6" fill="rgb(var(--light))" stroke="none"/>
      </g>)}
    </g>
  </svg>;
}
