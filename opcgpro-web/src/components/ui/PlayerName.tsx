"use client";

import { useEffect } from "react";
import { useNetStore } from "@/store/netStore";
import { usePlayerIdentityStore } from "@/store/playerIdentityStore";
import { watchPublicPlayerIdentity } from "@/net/publicPlayerIdentities";
import { EquippedSeasonTitleBadge } from "./HunterIdentity";
import styles from "./PlayerName.module.css";

/** 名字与佩戴称号各占一行，外层仍可与冠军称号、段位和操作按钮自然换行。 */
export default function PlayerName({ name, seasonTitle, fallbackSeasonTitle, className = "", align = "start" }: {
  name: string;
  seasonTitle?: string | null;
  fallbackSeasonTitle?: string | null;
  className?: string;
  align?: "start" | "center" | "end";
}) {
  const cached = usePlayerIdentityStore((state) => state.identities[name]);
  const connected = useNetStore((state) => state.connState === "connected" && state.loggedIn);
  useEffect(() => {
    if (seasonTitle !== undefined || !connected) return;
    return watchPublicPlayerIdentity(name);
  }, [connected, name, seasonTitle]);
  const title = seasonTitle === undefined
    ? cached ? cached.equippedSeasonTitle : fallbackSeasonTitle
    : seasonTitle;
  return <span data-player-nameplate={name} data-nameplate-align={align} className={`${styles.identity} ${className}`}>
    <span data-player-name-text className={styles.name}>{name}</span>
    <EquippedSeasonTitleBadge identity={{ equippedSeasonTitle: title ?? null }} compact />
  </span>;
}
