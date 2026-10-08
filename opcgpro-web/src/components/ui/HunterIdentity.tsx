import type { RankFaction } from "@/types/net";
import { RANK_AFFILIATION_NAMES, isHunterAffiliation } from "@/lib/rankAffiliation";
import styles from "./HunterIdentity.module.css";

export const HUNTER_SEAS = [
  { id: "east", subtitle: "晨曦航路", motto: "逐浪而行，向光而生", symbol: "wave" },
  { id: "west", subtitle: "暮影航路", motto: "隐于夜幕，猎取荣光", symbol: "moon" },
  { id: "south", subtitle: "翠风航路", motto: "循风远航，破浪追猎", symbol: "compass" },
  { id: "north", subtitle: "极光航路", motto: "越过寒潮，点燃锋芒", symbol: "star" },
] as const;

function Emblem({ variant }: { variant: string }) {
  return <svg viewBox="0 0 64 64" fill="none" aria-hidden="true" className={styles.emblem}>
    <circle cx="32" cy="32" r="27" stroke="currentColor" strokeOpacity=".3" strokeDasharray="2 5" />
    <circle cx="32" cy="32" r="22" stroke="currentColor" strokeOpacity=".4" />
    {variant === "wave" ? <><path d="M12 34c9-19 13 20 23-1s12-3 17 0M12 42c9-14 13 14 23-1s12-3 17 0" stroke="currentColor" strokeWidth="2.5"/><path d="m32 13 7 11H25l7-11Z" fill="currentColor"/></>
      : variant === "moon" ? <><path d="M40 15a19 19 0 1 0 9 29A20 20 0 0 1 40 15Z" fill="currentColor" fillOpacity=".6"/><path d="m40 22 3 7 7 3-7 3-3 7-3-7-7-3 7-3 3-7Z" stroke="currentColor"/></>
      : variant === "compass" ? <><path d="m32 9 7 23-7 23-7-23 7-23Zm-23 23 23-7 23 7-23 7-23-7Z" stroke="currentColor" strokeWidth="1.5"/><path d="m32 18 5 14-5 14-5-14 5-14Z" fill="currentColor"/></>
      : variant === "crown" ? <><path d="m14 25 9 7 9-17 9 17 9-7-5 21H19l-5-21Z" fill="currentColor" fillOpacity=".35" stroke="currentColor" strokeWidth="2"/><path d="M20 50h24M24 39h16" stroke="currentColor" strokeWidth="2"/></>
      : variant === "wing" ? <><path d="m32 18 7 14-7 14-7-14 7-14ZM13 23l10 7-10 6m38-13-10 7 10 6M16 41l8-3m24 3-8-3" stroke="currentColor" strokeWidth="2.5"/></>
      : variant === "fourstar" ? <><path d="m32 10 7 15 15 7-15 7-7 15-7-15-15-7 15-7 7-15Z" stroke="currentColor" strokeWidth="2"/><circle cx="32" cy="32" r="6" fill="currentColor" fillOpacity=".6"/></>
      : <><path d="m32 11 5 14 15 1-12 10 4 15-12-8-12 8 4-15-12-10 15-1 5-14Z" stroke="currentColor" strokeWidth="2"/><circle cx="32" cy="32" r="5" fill="currentColor"/></>}
  </svg>;
}

export function SeaNameBadge({ sea }: { sea: RankFaction }) {
  return <span className={`${styles.seaName} ${styles[sea] ?? ""}`} data-sea-effect={sea}>
    <span className={styles.seaSpark} aria-hidden="true" />{RANK_AFFILIATION_NAMES[sea]}
  </span>;
}

const HONORS: Record<string, { faction: string; variant: string; effect: string; label: string }> = {
  "海贼王": { faction: "pirate", variant: "crown", effect: "solar", label: "赤金王冠·霸气炎环" },
  "四皇": { faction: "pirate", variant: "fourstar", effect: "ember", label: "绯红四芒·余烬流光" },
  "海军元帅": { faction: "marine", variant: "wing", effect: "fleet", label: "冰蓝舰徽·翼光巡航" },
  "海军大将": { faction: "marine", variant: "compass", effect: "ice", label: "苍蓝棱镜·寒潮脉冲" },
  "世界之王": { faction: "government", variant: "crown", effect: "eclipse", label: "紫金王冠·日蚀星环" },
  "五老星": { faction: "government", variant: "star", effect: "constellation", label: "金紫五芒·星轨辉光" },
};

export function SeasonHonorBadge({ title }: { title: string }) {
  const base = title.replace(/^S\d+\s*/, "");
  const honor = HONORS[base];
  if (!honor) return <span>{title}</span>;
  return <span className={`${styles.honor} ${styles[honor.faction]} ${styles[honor.effect]}`}
    data-season-honor={base} title={`${title} · ${honor.label}`}>
    <span className={styles.honorOrbit} aria-hidden="true" />
    <Emblem variant={honor.variant} />
    <span className={styles.honorText}><small>{title.match(/^S\d+/)?.[0] ?? "S1"} 荣誉</small><strong>{base}</strong></span>
    <span className={styles.honorSheen} aria-hidden="true" />
  </span>;
}

export function SeasonHonorList({ titles, compact = false }: { titles?: string[]; compact?: boolean }) {
  return titles?.length ? <div className={`${styles.honorList} ${compact ? styles.compact : ""}`}>{titles.map(title => <SeasonHonorBadge key={title} title={title}/>)}</div> : null;
}

export function SeaChoiceCard({ sea, selected, disabled, pending, onSelect }: {
  sea: typeof HUNTER_SEAS[number]; selected?: boolean; disabled?: boolean; pending?: boolean; onSelect: () => void;
}) {
  return <button type="button" className={`${styles.seaCard} ${styles[sea.id]} ${selected ? styles.selected : ""}`}
    data-sea-choice={sea.id} aria-pressed={!!selected} disabled={disabled} onClick={onSelect}>
    <span className={styles.seaGrid} aria-hidden="true"/><span className={styles.seaHalo} aria-hidden="true"/>
    <Emblem variant={sea.symbol}/><span className={styles.seaEyebrow}>{sea.subtitle}</span>
    <SeaNameBadge sea={sea.id}/><span className={styles.seaMotto}>{sea.motto}</span>
    <span className={styles.seaAction}>{pending ? "正在锁定航线…" : selected ? "已启航" : disabled ? "航线已锁定" : "选择海域 →"}</span>
  </button>;
}

export function AffiliationBadge({ faction }: { faction: RankFaction }) {
  return isHunterAffiliation(faction) ? <SeaNameBadge sea={faction}/> : <span>{RANK_AFFILIATION_NAMES[faction]}</span>;
}
