import { formatHunterDefeats } from "@/lib/rankAffiliation";

/** 圆润、无血腥元素的排位计数图标，尺寸随文字字号缩放。 */
export function HunterSkullIcon({ className = "" }: { className?: string }) {
  return <svg data-hunter-skull viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false"
    className={`inline-block h-[1em] w-[1em] min-h-3 min-w-3 shrink-0 align-middle ${className}`}>
    <path d="M12 3a8 8 0 0 0-8 8c0 3.1 1.3 5.2 4 6v3h8v-3c2.7-.8 4-2.9 4-6a8 8 0 0 0-8-8Z"
      fill="currentColor" fillOpacity=".1" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round"/>
    <ellipse cx="8.5" cy="11.5" rx="1.5" ry="1.8" fill="currentColor"/>
    <ellipse cx="15.5" cy="11.5" rx="1.5" ry="1.8" fill="currentColor"/>
    <path d="m12 14.5-1.2 1.8h2.4L12 14.5Z" fill="currentColor"/>
    <path d="M10 17.5V20m4-2.5V20" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round"/>
  </svg>;
}

export default function HunterDefeatCount({ value, signed = false, className = "" }: {
  value: number;
  signed?: boolean;
  className?: string;
}) {
  return <span data-hunter-defeats={value} title="击败数量"
    className={`inline-flex max-w-full items-center gap-1 whitespace-nowrap align-middle tabular-nums ${className}`}>
    <span className="sr-only">击败数量 </span>
    <span>{signed && value >= 0 ? "+" : ""}{formatHunterDefeats(value)}</span>
    <HunterSkullIcon />
  </span>;
}
