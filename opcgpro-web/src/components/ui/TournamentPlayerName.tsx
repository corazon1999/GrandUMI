"use client";

import { createContext, useContext, type ReactNode } from "react";
import type { TournamentContact } from "@/types/tournament";

export const TournamentContactsContext = createContext<readonly TournamentContact[] | null>(null);

export function useTournamentNameContact(name: string) {
  const contacts = useContext(TournamentContactsContext);
  if (contacts === null) return undefined;
  const matches = contacts.filter(contact => contact.name === name);
  // 历史重名账号不猜测QQ归属，仍可在赛事对阵页按账号核对。
  return { qq: matches.length === 1 ? matches[0].qq : null, ambiguous: matches.length > 1 };
}

export function TournamentQq({ qq, ambiguous = false }: { qq: string | null; ambiguous?: boolean }) {
  return <small data-tournament-qq className="ml-1 inline-block max-w-full break-all font-normal leading-normal text-slate-400" style={{ fontSize: "max(10px, 0.8em)" }}>
    {ambiguous ? "QQ请见赛事对阵" : qq ? `QQ ${qq}` : "未绑定QQ"}
  </small>;
}

export default function TournamentPlayerName({ name, qq, children }: { name: string; qq: string | null; children?: ReactNode }) {
  return <span data-tournament-player-name={name} className="inline-flex max-w-full min-w-0 flex-wrap items-baseline gap-y-0.5 align-middle">
    <span className="min-w-0 break-words">{name}</span><TournamentQq qq={qq} />{children}
  </span>;
}

export function TournamentScopedName({ name }: { name: string }) {
  const contact = useTournamentNameContact(name);
  return <>{name}{contact && <TournamentQq {...contact} />}</>;
}
