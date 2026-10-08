"use client";

import { useEffect, useRef, useState } from "react";
import type { MsgEquipSeasonTitle, RankProfileSnapshot } from "@/types/net";
import { HomeRequest } from "@/net/HomeProtocol";
import { eventBus } from "@/net/eventBus";
import { displayedSeasonTitle } from "@/lib/seasonTitles";
import { SeasonHonorBadge } from "@/components/ui/HunterIdentity";
import { showMessage } from "@/components/ui/MessageBox";
import styles from "@/components/ui/HunterIdentity.module.css";

const ORDER = ["海贼王", "四皇", "海军元帅", "海军大将", "世界之王", "五老星"];

export default function SeasonTitleCenter({ profile, onEquip }: {
  profile?: RankProfileSnapshot | null;
  onEquip?: (title: string | null) => string | null;
}) {
  const [pending, setPending] = useState<{ id: string; title: string | null } | null>(null);
  const pendingId = useRef<string | null>(null);
  const equipped = displayedSeasonTitle(profile);
  const titles = [...new Set(profile?.seasonTitles ?? [])].sort((a, b) => {
    const rank = (title: string) => { const index = ORDER.indexOf(title.replace(/^S\d+\s*/, "")); return index < 0 ? ORDER.length : index; };
    return rank(a) - rank(b) || a.localeCompare(b, "zh-CN");
  });
  useEffect(() => {
    const handler = (message: { proto: string }) => {
      if (message.proto !== "MsgEquipSeasonTitle") return;
      const reply = message as MsgEquipSeasonTitle;
      if (!pendingId.current || reply.requestId !== pendingId.current) return;
      pendingId.current = null;
      setPending(null);
    };
    eventBus.on("message", handler);
    return () => { pendingId.current = null; eventBus.off("message", handler); };
  }, []);
  useEffect(() => {
    if (!pending) return;
    const timer = setTimeout(() => {
      if (pendingId.current !== pending.id) return;
      pendingId.current = null;
      setPending(null);
      showMessage("称号保存超时，请刷新个人资料后重试", "error");
      HomeRequest.requestRankSnapshot("standard");
    }, 10_000);
    return () => clearTimeout(timer);
  }, [pending]);
  const select = (title: string | null) => {
    if (pendingId.current || title === equipped) return;
    const id = onEquip ? onEquip(title) : HomeRequest.equipSeasonTitle(title);
    if (!id) { showMessage("服务器未连接，请重连后选择称号", "error"); return; }
    pendingId.current = id;
    setPending({ id, title });
  };
  return <section data-season-title-center className={styles.titleCenter} aria-label="称号中心" aria-busy={!!pending}>
    <div className={styles.titleCenterHeading}><div><h3>称号中心</h3>
      <p>选择一个已拥有的称号，展示在资料、排行榜与排位对局中。</p></div>
      <span className={styles.titleCount}>{titles.length} 枚已拥有</span>
    </div>
    <div className={styles.titleWornRow}>
      <div><span className={styles.titleCaption}>当前佩戴</span><div data-title-current className={styles.titleCurrent}>
        {equipped ? <SeasonHonorBadge title={equipped}/> : <span className={styles.titleEmpty}>未佩戴称号</span>}
      </div></div>
      <button type="button" data-title-unequip disabled={!equipped || !!pending} onClick={() => select(null)} className={styles.titleUnequip}>取消佩戴</button>
    </div>
    <p role="status" aria-live="polite" className={styles.titleSaveStatus}>{pending ? "正在保存称号…" : "标准与狂野模式共用佩戴选择"}</p>
    <div className={styles.titleGrid}>
      {titles.map(title => <button type="button" key={title} data-title-choice={title}
        aria-label={`佩戴${title}`} aria-pressed={equipped === title} disabled={!!pending} onClick={() => select(title)}
        className={`${styles.titleOption} ${equipped === title ? styles.titleOptionSelected : ""}`}>
        <SeasonHonorBadge title={title}/>
        <span className={styles.titleOptionState}>{pending?.title === title ? "切换中…" : equipped === title ? "✓ 已佩戴" : "点击佩戴"}</span>
      </button>)}
    </div>
    {!titles.length && <p className={styles.titleEmpty}>{profile ? "赛季结算获得的特殊称号会收录于此。" : "正在读取称号…"}</p>}
  </section>;
}
