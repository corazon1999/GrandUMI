import { useId } from "react";
import styles from "./HunterIdentity.module.css";

function star(x: number, y: number, radius: number) {
  return Array.from({ length: 10 }, (_, i) => {
    const angle = (i * 36 - 90) * Math.PI / 180;
    const r = i % 2 ? radius * .43 : radius;
    return `${x + Math.cos(angle) * r},${y + Math.sin(angle) * r}`;
  }).join(" ");
}

/** 六枚独立雕刻徽章，使用实例独立渐变，兼容列表与旋转画布。 */
export default function SeasonTitleEmblem({ variant }: { variant: string }) {
  const id = useId().replace(/:/g, "");
  const metal = `${id}-metal`, face = `${id}-face`;
  const fill = `url(#${metal})`;
  return <svg viewBox="0 0 80 80" fill="none" aria-hidden="true" data-title-emblem={variant}
    className={`${styles.emblem} ${styles.titleEmblem}`}>
    <defs>
      <linearGradient id={metal} x1="18" y1="10" x2="62" y2="69" gradientUnits="userSpaceOnUse">
        <stop stopColor="rgb(var(--light))"/><stop offset=".28" stopColor="rgb(var(--accent))"/>
        <stop offset=".48" stopColor="#fff5d8"/><stop offset=".66" stopColor="rgb(var(--accent))"/>
        <stop offset="1" stopColor="rgb(var(--light))" stopOpacity=".7"/>
      </linearGradient>
      <radialGradient id={face}><stop stopColor="rgb(var(--accent))" stopOpacity=".24"/><stop offset="1" stopColor="#040711"/></radialGradient>
    </defs>
    <circle cx="40" cy="40" r="37" fill={`url(#${face})`} stroke={fill} strokeWidth="1.2"/>
    <circle cx="40" cy="40" r="33.5" stroke="currentColor" strokeOpacity=".45" strokeWidth=".8"/>
    {Array.from({ length: 24 }, (_, i) => <path key={i} d="M40 5v2.8" stroke={fill} strokeWidth={i % 3 ? .65 : 1.4} transform={`rotate(${i * 15} 40 40)`}/>)}
    <path d="M15 42c0 15 9 26 25 27M65 42c0 15-9 26-25 27" stroke={fill} strokeOpacity=".65" strokeWidth="1.2"/>
    {[0,1,2,3].map(i => <g key={i} transform={`translate(0 ${i * 5})`} fill={fill} opacity={.5 + i * .1}>
      <path d="m17 45 7 3-3 3-4-6Zm46 0-7 3 3 3 4-6Z"/>
    </g>)}
    {variant === "king" ? <g strokeLinejoin="round">
      <path d="m23 58 34-25m-34 0 34 25" stroke={fill} strokeWidth="3"/><path d="m20 60 6-5m28 0 6 5" stroke={fill} strokeWidth="2"/>
      <path d="M29 43c0-8 5-13 11-13s11 5 11 13c0 6-3 9-6 10v7H35v-7c-3-1-6-4-6-10Z" fill={fill} stroke="#ffe8a5" strokeWidth=".6"/>
      <path d="m32 44 6-1-2 5-4-1Zm16 0-6-1 2 5 4-1Zm-8 4-2 4h4l-2-4Z" fill="#261329"/>
      <path d="M37 55v5m6-5v5m-8-5h10" stroke="#6f3418" strokeWidth="1"/>
      <path d="m24 24 8 7 8-16 8 16 8-7-4 16H28l-4-16Z" fill={fill} stroke="#ffeabd" strokeWidth="1.1"/>
      <path d="M29 35h22m-20 3h18" stroke="#49221b" strokeWidth="1"/><path d="m40 25 3 5-3 5-3-5 3-5Z" fill="#fef08a" stroke="#a85316"/>
      {[24,40,56].map((x,i) => <circle key={x} cx={x} cy={i === 1 ? 15 : 24} r="2.1" fill="#fff6cb"/>)}
    </g> : variant === "emperors" ? <g strokeLinejoin="round">
      <path d="m40 14 9 17 17 9-17 9-9 17-9-17-17-9 17-9 9-17Z" fill={fill} fillOpacity=".28" stroke={fill} strokeWidth="1.6"/>
      {[0,90,180,270].map(angle => <g key={angle} transform={`rotate(${angle} 40 40)`}>
        <path d="m35 19 2-6 3 3 3-3 2 6-5 4-5-4Z" fill={fill}/><path d="M38 21h4" stroke="#fff5dd" strokeWidth=".8"/>
      </g>)}
      <path d="M29 31c3-5 7-7 11-7s8 2 11 7l-4 5 4 5-3 12-8 6-8-6-3-12 4-5-4-5Z" fill={fill} stroke="#ffcad6" strokeWidth=".8"/>
      <path d="m33 39 6 2-2 5-4-2Zm14 0-6 2 2 5 4-2ZM40 46l-3 5h6l-3-5Z" fill="#390f26"/>
      <path d="m34 32 6 3 6-3M34 52h12m-9 0v4m6-4v4" stroke="#55122f" strokeWidth="1.1"/>
    </g> : variant === "marshal" ? <g strokeLinejoin="round">
      <path d="m40 21-8 9-14-8 2 8-6 1 10 8-5 3 14 3 7-8 7 8 14-3-5-3 10-8-6-1 2-8-14 8-8-9Z" fill={fill} stroke="#d9f7ff" strokeWidth=".8"/>
      <path d="m22 29 11 6m-9 1 8 3m26-10-11 6m9 1-8 3" stroke="#12637f" strokeWidth="1.2"/>
      <circle cx="40" cy="37" r="8" fill="#062239" stroke={fill} strokeWidth="1.5"/>
      <path d="M40 32v25m-6-17h12m-21 8c2 10 11 14 15 14s13-4 15-14l-7 4m-16 0-7-4" stroke={fill} strokeWidth="2.7" strokeLinecap="round"/>
      <circle cx="40" cy="32" r="2.7" stroke={fill} strokeWidth="1.6"/>
      <polygon points={star(40,16,5)} fill={fill}/><path d="M31 65h18m-15-3h12" stroke={fill} strokeWidth="1.5"/>
    </g> : variant === "admiral" ? <g strokeLinejoin="round">
      <path d="m40 16 18 14-5 23-13 11-13-11-5-23 18-14Z" fill={fill} fillOpacity=".15" stroke={fill} strokeWidth="1.6"/>
      <path d="m40 21 13 11-4 18-9 9-9-9-4-18 13-11Z" stroke={fill} strokeWidth=".65"/>
      <path d="M40 24v31m-9-24v8l9 6 9-6v-8m-14-6 5-6 5 6m-17 5 3-4 4 4m10 0 4-4 3 4" stroke={fill} strokeWidth="2.2" strokeLinecap="round"/>
      <path d="m40 42 5 5-5 8-5-8 5-5Z" fill={fill} stroke="#edfbff" strokeWidth=".7"/>
      <path d="M35 62v4m5-3v4m5-5v4" stroke={fill} strokeWidth="2"/>
      <circle cx="24" cy="40" r="1.4" fill={fill}/><circle cx="56" cy="40" r="1.4" fill={fill}/>
    </g> : variant === "sovereign" ? <g strokeLinejoin="round">
      <circle cx="40" cy="37" r="20" stroke={fill} strokeOpacity=".5" strokeWidth="1"/>
      <path d="M24 45V27l7 7 9-18 9 18 7-7v18l-9 7H33l-9-7Z" fill={fill} stroke="#ffe9b3" strokeWidth="1.1"/>
      <path d="m29 39 11-6 11 6-11 6-11-6Z" fill="#281037" stroke="#e9b85d"/>
      <circle cx="40" cy="39" r="4.5" fill="#e9b85d"/><circle cx="40" cy="39" r="2.2" fill="#3b174e"/>
      <path d="M29 49h22m-18 4-4 9m18-9 4 9M25 63h30M30 67h20" stroke={fill} strokeWidth="2" strokeLinecap="round"/>
      <path d="M34 28h12m-13 1-3 8m17-8 3 8" stroke="#633965" strokeWidth=".8"/>
      <polygon points={star(40,12,3.3)} fill="#fff5ca"/><circle cx="21" cy="24" r="1.3" fill={fill}/><circle cx="59" cy="24" r="1.3" fill={fill}/>
    </g> : <g strokeLinejoin="round">
      <path d="m40 17 20 15-8 25H28l-8-25 20-15Z" stroke={fill} strokeWidth="1.2"/>
      <circle cx="40" cy="40" r="14" stroke={fill} strokeOpacity=".5" strokeWidth=".75"/>
      {[[40,17],[60,32],[52,57],[28,57],[20,32]].map(([x,y]) => <g key={`${x}-${y}`}>
        <path d={`M40 40  ${x} ${y}`} stroke={fill} strokeOpacity=".55" strokeWidth=".8"/>
        <polygon points={star(x,y,6.4)} fill={fill} stroke="#fff6d7" strokeWidth=".55"/>
      </g>)}
      <circle cx="40" cy="40" r="7" fill={fill}/><circle cx="40" cy="40" r="4.8" fill="#281642"/>
      <path d="m40 35 1.3 3.7 3.7 1.3-3.7 1.3-1.3 3.7-1.3-3.7-3.7-1.3 3.7-1.3 1.3-3.7Z" fill="#fff4b6"/>
      <path d="M33 66h14" stroke={fill} strokeWidth="1"/>
    </g>}
  </svg>;
}
