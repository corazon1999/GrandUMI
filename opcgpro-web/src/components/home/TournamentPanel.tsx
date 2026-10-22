"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useNetStore } from "@/store/netStore";
import { loadAllDecks, subscribeDecksUpdated } from "@/data/DeckMapper";
import { NetManager } from "@/net/NetManager";
import { HomeRequest } from "@/net/HomeProtocol";
import { requestTournament } from "@/net/TournamentClient";
import Modal from "@/components/ui/Modal";
import TournamentPlayerName from "@/components/ui/TournamentPlayerName";
import type { TournamentDetail, TournamentResponse, TournamentRound, TournamentSummary, TournamentTable } from "@/types/tournament";

const BUTTON = "min-h-11 min-w-11 rounded-xl border border-slate-700 px-4 py-2 text-sm font-bold text-slate-100 transition hover:border-amber-400 disabled:cursor-not-allowed disabled:opacity-40";
const PRIMARY = `${BUTTON} border-amber-500 bg-amber-500 text-slate-950 hover:bg-amber-400`;
const INPUT = "min-h-11 w-full min-w-0 rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-amber-400";
const STATUS: Record<string, string> = { pending: "待审核", rejected: "未通过", approved: "报名与签到", swiss: "瑞士轮", cut: "淘汰赛", completed: "已结束", cancelled: "已取消", registered: "正式名额", waitlist: "候补", active: "参赛中", withdrawn: "已退赛", removed: "名额已释放", live: "进行中", manual: "待手动结算", settled: "本轮已结算", waiting: "等待落座", starting: "正在开局", playing: "对局中", finished: "已结算", blocked: "开局待处理", open: "待处理", resolved: "已处理" };
const OUTCOME: Record<string, string> = { p0: "甲方胜", p1: "乙方胜", doubleLoss: "双败", bye: "轮空胜", void: "空桌" };
const ACTION: Record<string, string> = { apply: "提交申请", edit: "修改并重审", approve: "审核通过", reject: "驳回申请", register: "报名或换卡组", checkin: "签到", withdraw: "退赛", openRound: "开启轮次", seat: "落座", endRound: "主动结束本轮", settle: "逐桌确认", correct: "改判", announce: "发布公告", appeal: "申诉", resolveAppeal: "处理申诉", cancel: "取消赛事" };
const stamp = (value: number) => new Date(value).toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false });
const localInput = (value: number) => { const date = new Date(value); return new Date(value - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16); };
const accountList = (value: string) => value.split(/[,，\n]/).map(a => a.trim()).filter(Boolean);

interface Props {
  preview?: TournamentDetail;
  initialTab?: string;
}

export default function TournamentPanel({ preview, initialTab = "all" }: Props) {
  const account = useNetStore(s => s.account);
  const connected = useNetStore(s => s.connState === "connected");
  const [items, setItems] = useState<TournamentSummary[]>([]);
  const [detail, setDetail] = useState<TournamentDetail | null>(preview ?? null);
  const [selected, setSelected] = useState<string | null>(preview?.id ?? null);
  const [filter, setFilter] = useState(initialTab);
  const [search, setSearch] = useState("");
  const [canReview, setCanReview] = useState(preview?.canReview ?? false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(!preview);
  const [feedback, setFeedback] = useState<{ text: string; error: boolean } | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState(false);
  const [endConfirmation, setEndConfirmation] = useState<{ token: string; tournament: TournamentDetail } | null>(null);
  const [endReason, setEndReason] = useState("");
  const [withdrawOpen, setWithdrawOpen] = useState(false);
  const [checkinOpen, setCheckinOpen] = useState(false);
  const selectedRef = useRef(selected);
  const activeRequest = useRef(0);
  const [now, setNow] = useState(() => Date.now());
  const [offset, setOffset] = useState(0);
  const [deckNames, setDeckNames] = useState<string[]>([]);
  const [deckName, setDeckName] = useState("");
  const [section, setSection] = useState("overview");

  useEffect(() => { selectedRef.current = selected; }, [selected]);
  useEffect(() => {
    const update = () => setDeckNames(Object.keys(loadAllDecks()));
    update(); return subscribeDecksUpdated(update);
  }, [account]);
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, []);
  const accept = useCallback((response: TournamentResponse) => {
    if (response.items) { setItems(response.items); setCanReview(response.canReview === true); }
    if (response.tournament) setDetail(response.tournament);
    if (response.serverNow) setOffset(response.serverNow - Date.now());
  }, []);
  const refresh = useCallback(async (quiet = false) => {
    if (preview) return;
    const sequence = ++activeRequest.current;
    const id = selectedRef.current;
    try {
      const response = await requestTournament(id ? "detail" : "list", id ? { id } : {});
      if (sequence === activeRequest.current && id === selectedRef.current) accept(response);
    } catch (error) { if (!quiet) setFeedback({ text: (error as Error).message, error: true }); }
    finally { setLoading(false); }
  }, [accept, preview]);
  useEffect(() => {
    if (preview || !connected) return;
    void refresh();
    const timer = setInterval(() => { if (!document.hidden) void refresh(true); }, 4000);
    return () => { clearInterval(timer); activeRequest.current++; };
  }, [refresh, connected, selected, preview]);

  const mutate = async (action: string, values: Record<string, unknown> = {}) => {
    if (preview) { setFeedback({ text: "布局预览不执行赛事操作。", error: false }); return; }
    setBusy(true); setFeedback(null); activeRequest.current++;
    try {
      const response = await requestTournament(action, { id: detail?.id, revision: detail?.revision, ...values });
      accept(response);
      if (response.tournament) { selectedRef.current = response.tournament.id; setSelected(response.tournament.id); }
      setFeedback({ text: `${ACTION[action] || "操作"}成功。`, error: false });
      return response;
    } catch (error) { setFeedback({ text: (error as Error).message, error: true }); void refresh(true); }
    finally { setBusy(false); }
  };

  const choose = (id: string | null) => { activeRequest.current++; selectedRef.current = id; setSelected(id); setDetail(null); setLoading(true); setSection("overview"); setFeedback(null); setCheckinOpen(false); setWithdrawOpen(false); };
  const prepareEnd = async () => {
    if (!detail) return;
    if (preview) { setEndReason(""); setEndConfirmation({ token: "layout-preview", tournament: detail }); return; }
    setBusy(true);
    try {
      const response = await requestTournament("previewEnd", { id: detail.id });
      if (response.confirmationToken && response.tournament) {
        accept(response); setEndReason(""); setEndConfirmation({ token: response.confirmationToken, tournament: response.tournament });
      }
    } catch (error) { setFeedback({ text: (error as Error).message, error: true }); }
    finally { setBusy(false); }
  };
  const current = detail?.rounds.at(-1);
  const ownTable = current?.tables.find(m => m.player0 === account || m.player1 === account);
  const time = now + offset;
  const contact = (key: string) => detail?.contacts?.find(person => person.account.toLowerCase() === key.toLowerCase());
  const person = (key: string | null): ReactNode => key === null ? "轮空" : key === "system" ? "系统" :
    <TournamentPlayerName name={contact(key)?.name || detail?.entrants.find(e => e.account === key)?.name || key} qq={contact(key)?.qq ?? null} />;
  const filtered = items.filter(item => {
    if (search && !`${item.name} ${item.ownerName}`.toLowerCase().includes(search.toLowerCase())) return false;
    if (filter === "mine") return item.mine || item.canManage;
    if (filter === "review") return item.status === "pending";
    if (filter === "active") return ["swiss", "cut"].includes(item.status);
    if (filter === "finished") return ["completed", "cancelled"].includes(item.status);
    if (filter === "open") return item.status === "approved";
    return true;
  });

  return <section aria-label="赛事系统" className="h-full min-w-0 overflow-y-auto overscroll-contain bg-slate-950 text-slate-100" style={{ paddingBottom: "max(24px, var(--layout-safe-bottom, 0px))" }}>
    <div className="mx-auto max-w-6xl space-y-5 p-3 sm:p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div><p className="text-xs font-semibold tracking-widest text-amber-400">GrandUMI 社区赛事</p><h1 className="mt-1 text-2xl font-black">{detail?.name || "赛事大厅"}</h1></div>
        {selected ? <button className={BUTTON} onClick={() => choose(null)}>返回赛事大厅</button> : <button className={PRIMARY} disabled={busy || !connected && !preview} onClick={() => { setEditing(false); setFormOpen(true); }}>申请举办赛事</button>}
      </header>
      {feedback && <p role="status" className={`rounded-xl border p-3 text-sm ${feedback.error ? "border-red-800 bg-red-950/40 text-red-200" : "border-emerald-800 bg-emerald-950/30 text-emerald-200"}`}>{feedback.text}</p>}
      {!connected && !preview && <p className="text-sm text-amber-200">网络未连接，连接后可查看最新赛事状态。</p>}
      {loading && !detail && <p className="py-10 text-center text-slate-400">正在读取赛事…</p>}

      {!selected && <>
        <p className="max-w-3xl text-sm leading-6 text-slate-400">玩家申请，管理员审核。8至256人固定规格，瑞士轮后进入淘汰赛；同桌两人落座即可开赛。</p>
        <nav aria-label="赛事分类" className="flex gap-2 overflow-x-auto pb-1">
          {[["all", "全部赛事"], ["open", "报名与签到"], ["active", "进行中"], ["mine", "我的赛事"], ["finished", "已结束"], ...(canReview ? [["review", "待审核"]] : [])].map(([key, label]) => <button key={key} className={`${BUTTON} shrink-0 ${filter === key ? "border-amber-400 bg-amber-950/40 text-amber-200" : ""}`} aria-pressed={filter === key} onClick={() => setFilter(key)}>{label}</button>)}
        </nav>
        <label className="block text-sm text-slate-400">搜索赛事或举办者<input value={search} onChange={e => setSearch(e.target.value)} className={`${INPUT} mt-2`} placeholder="输入赛事名称" /></label>
        <div className="grid gap-4 md:grid-cols-2">{filtered.map(item => <article key={item.id} className="min-w-0 rounded-2xl border border-slate-800 bg-slate-900/50 p-4">
          <div className="flex items-start justify-between gap-3"><h2 className="break-words text-lg font-bold">{item.name}</h2><span className="shrink-0 text-xs text-amber-300">{STATUS[item.status]}</span></div>
          <p className="mt-2 text-sm text-slate-400"><TournamentPlayerName name={item.ownerName} qq={item.ownerQq ?? null} /> 举办 · {item.format === "standard" ? "标准" : "狂野"} · {item.registered}/{item.capacity}人</p>
          <p className="mt-2 text-sm text-slate-300">开赛 {stamp(item.startsAt)}</p><button className={`${BUTTON} mt-4 w-full`} onClick={() => choose(item.id)}>查看赛事</button>
        </article>)}</div>
        {!loading && filtered.length === 0 && <p className="rounded-2xl border border-dashed border-slate-800 py-14 text-center text-slate-400">暂时没有符合条件的赛事。</p>}
      </>}

      {detail && <>
        <div className="flex flex-wrap items-center gap-2 text-xs"><span className="rounded-full bg-amber-950 px-3 py-2 text-amber-200">{STATUS[detail.status]}</span><span className="text-slate-400">{detail.capacity}人 · {detail.swissRounds}轮瑞士轮 · 前{detail.cutSize}名晋级 · BO1</span></div>
        {detail.announcement && <p className="whitespace-pre-wrap rounded-xl border border-slate-700 p-4 text-sm leading-6"><strong className="text-amber-300">赛事公告　</strong>{detail.announcement}</p>}
        {ownTable && <article className="rounded-2xl border border-amber-800 bg-amber-950/15 p-4" aria-label="我的当前对阵">
          <p className="text-sm text-amber-300">{current?.stage === "swiss" ? "瑞士轮" : "淘汰赛"}第{current?.number}轮 · 第{ownTable.number}桌</p>
          <p className="mt-2 break-words font-bold">{person(ownTable.player0)} 对 {person(ownTable.player1)}</p>
          <p className="mt-2 text-sm text-slate-300">{STATUS[ownTable.status]} · {clockLabel(ownTable, time)}{ownTable.confirmed ? ` · ${OUTCOME[ownTable.outcome || ""] || "已确认"}` : ""}</p>
          {ownTable.reason && <p className="mt-2 text-sm text-slate-400">{ownTable.reason}</p>}
          {current?.status === "live" && ["waiting", "blocked"].includes(ownTable.status) && ownTable.player1 && <button className={`${PRIMARY} mt-3 w-full sm:w-auto`} disabled={busy} onClick={() => void mutate("seat", { tableId: ownTable.id })}>{(ownTable.player0 === account ? ownTable.seated0 : ownTable.seated1) ? "已落座，等待对手" : ownTable.status === "blocked" ? "重新落座" : "落座"}</button>}
          {ownTable.status === "playing" && <button className={`${PRIMARY} mt-3 w-full sm:w-auto`} onClick={() => { NetManager.send({ proto: "MsgRequestState" }); useNetStore.getState().setNavigateTo("/game"); }}>进入比赛</button>}
          {current?.status === "manual" && <p className="mt-3 text-sm text-amber-200">本轮已停止，等待赛事管理员逐桌确认结果。</p>}
        </article>}
        <nav className="flex gap-2 overflow-x-auto pb-1" aria-label="赛事详情导航">{[["overview", "赛事介绍"], ["registration", "报名签到"], ["rounds", "赛程对阵"], ["standings", "积分排名"], ...((detail.canManage || detail.canJudge || detail.canReview || detail.canEdit) ? [["manage", "赛事管理"]] : [])].map(([key, label]) => <button key={key} className={`${BUTTON} shrink-0 ${section === key ? "border-amber-400 bg-amber-950/30" : ""}`} aria-pressed={section === key} onClick={() => setSection(key)}>{label}</button>)}</nav>

        {section === "overview" && <div className="space-y-4">
          <p className="text-sm leading-6 text-slate-400">预计对局时间约{Math.ceil((detail.swissRounds + Math.log2(detail.cutSize)) * 30 / 60 * 10) / 10}小时，另需预留签到、落座、轮间休息及裁判处理时间。观战固定隐藏手牌，赛事结束后才可公开分享回放。</p><div className="grid gap-3 sm:grid-cols-3">{[["报名时间", `${stamp(detail.registrationOpensAt)} 至 ${stamp(detail.registrationClosesAt)}`], ["正式开赛", stamp(detail.startsAt)], ["赛制", `${detail.swissRounds}轮瑞士轮 + ${detail.cutSize}强单败淘汰赛`]].map(([label, value]) => <div key={label} className="rounded-xl border border-slate-800 p-4"><p className="text-xs text-slate-400">{label}</p><p className="mt-2 break-words text-sm font-bold">{value}</p></div>)}</div>
          <p className="whitespace-pre-wrap text-sm leading-7">{detail.description || "举办者尚未填写赛事简介。"}</p>
          <p className="text-sm leading-6 text-slate-400">全程使用锁定卡组；每桌两人落座后独立计时30分钟。瑞士轮胜1分、负0分，超时双败；同分依次比较对手胜率、对手的对手胜率及固定抽签结果。全部桌结算后，由管理员开启下一轮。</p>
          <p className="whitespace-pre-wrap text-sm leading-6">奖励说明：{detail.rewards || "未设置奖励"}</p>
          <div className="space-y-2 break-words text-sm text-slate-400"><p>举办者：{person(detail.owner)}</p><p>赛事管理员：{detail.managers.map(key => <span key={key} className="mr-3 inline-block max-w-full">{person(key)}</span>)}</p><p>裁判：{detail.judges.length ? detail.judges.map(key => <span key={key} className="mr-3 inline-block max-w-full">{person(key)}</span>) : "由赛事管理员处理"}</p></div>
          {detail.reviewReason && <p className="text-sm text-amber-200">审核说明：{detail.reviewReason}</p>}
          {detail.champion && <p className="text-xl font-black text-amber-300">冠军　{person(detail.champion)}</p>}
        </div>}

        {section === "registration" && <div className="space-y-4">
          {detail.status === "approved" && <div className="rounded-2xl border border-slate-800 p-4">
            <p className="text-sm leading-6 text-slate-300">报名后即可提前签到，签到后不能退出本场赛事。正式名额满后进入候补；开赛前5分钟释放未签到名额，必须满{detail.capacity}人有效签到后才能开赛。</p>
            <label className="mt-4 block text-sm text-slate-400">参赛卡组<select className={`${INPUT} mt-2`} value={deckName || detail.mine?.deckName || ""} onChange={e => setDeckName(e.target.value)}><option value="">请选择云端卡组</option>{deckNames.map(name => <option key={name}>{name}</option>)}</select></label>
            <div className="mt-3 flex flex-wrap gap-3"><button className={PRIMARY} disabled={busy || time < detail.registrationOpensAt || time >= detail.registrationClosesAt || !deckNames.length} onClick={() => void mutate("register", { deckName: deckName || detail.mine?.deckName || "" })}>{detail.mine ? "更新参赛卡组" : "报名参赛"}</button><button className={BUTTON} disabled={busy || !detail.mine || !["registered", "waitlist"].includes(detail.mine.status) || time < detail.registrationOpensAt || time >= detail.startsAt || detail.mine.checkedIn} onClick={() => setCheckinOpen(true)}>{detail.mine?.checkedIn ? "已签到" : "签到"}</button></div>
          </div>}
          {detail.mine && <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-800 p-4"><p className="text-sm">我的状态：{STATUS[detail.mine.status]} · {detail.mine.checkedIn ? "已签到" : "未签到"} · {detail.mine.deckName}</p>{!["removed", "withdrawn"].includes(detail.mine.status) && !["completed", "cancelled"].includes(detail.status) && (detail.mine.checkedIn ? <p className="text-sm text-amber-200">已签到，不能退出本场赛事</p> : <button className={BUTTON} disabled={busy} onClick={() => setWithdrawOpen(true)}>退出赛事</button>)}</div>}
          <div className="grid gap-2 sm:grid-cols-2">{detail.entrants.map(entrant => <div key={entrant.account} className="min-w-0 rounded-xl border border-slate-800 p-3 text-sm"><p className="break-words font-bold">{person(entrant.account)} <span className="font-normal text-slate-400">· {STATUS[entrant.status]}</span></p><p className="mt-1 text-xs text-slate-400">{entrant.checkedIn ? "已签到" : "未签到"} · 领航 {entrant.leader}</p>{entrant.deckRaw && <details className="mt-2"><summary className="min-h-11 cursor-pointer py-3 text-amber-300">裁判查看锁定卡组</summary><pre className="max-h-48 overflow-auto whitespace-pre-wrap text-xs">{entrant.deckRaw}</pre></details>}</div>)}</div>
        </div>}

        {section === "rounds" && <div className="space-y-4">{detail.rounds.length === 0 && <p className="py-8 text-slate-400">等待赛事管理员开启首轮。</p>}{detail.rounds.map(round => <details key={round.number} open={round.number === current?.number} className="rounded-2xl border border-slate-800 p-4"><summary className="min-h-11 cursor-pointer py-2 font-bold">{round.stage === "swiss" ? "瑞士轮" : "淘汰赛"}第{round.number}轮 · {STATUS[round.status]} · {round.tables.filter(m => m.confirmed).length}/{round.tables.length}桌已确认</summary><div className="mt-3 grid gap-3 lg:grid-cols-2">{round.tables.map(table => <div key={table.id} className="rounded-xl bg-slate-900 p-4"><p className="text-xs text-amber-300">第{table.number}桌 · {STATUS[table.status]}</p><p className="mt-2 break-words text-sm"><strong>甲方</strong> {person(table.player0)} {table.seated0 ? "✓" : ""}</p><p className="mt-2 break-words text-sm"><strong>乙方</strong> {person(table.player1)} {table.seated1 ? "✓" : ""}</p><p className="mt-2 text-sm text-slate-400">{table.confirmed ? OUTCOME[table.outcome || ""] : clockLabel(table, time)}</p>{table.reason && <p className="mt-1 text-xs leading-5 text-slate-400">{table.reason}</p>}{table.status === "playing" && table.roomId && table.player0 !== account && table.player1 !== account && <button className={`${BUTTON} mt-3`} onClick={() => HomeRequest.spectateRoom(table.roomId!)}>观战本桌（隐藏手牌）</button>}{round.number === current?.number && (table.player0 === account || table.player1 === account) && ["swiss", "cut"].includes(detail.status) && <AppealForm busy={busy} onSubmit={reason => mutate("appeal", { tableId: table.id, reason })} />}</div>)}</div></details>)}</div>}

        {section === "standings" && <div className="overflow-x-auto rounded-2xl border border-slate-800"><table className="w-full min-w-[540px] text-left text-sm"><thead className="bg-slate-900 text-slate-400"><tr>{["排名", "玩家", "积分", "胜/负", "轮空", "对手胜率", "对手的对手"].map(label => <th key={label} className="p-3 font-normal">{label}</th>)}</tr></thead><tbody>{detail.standings.map(player => <tr key={player.account} className="border-t border-slate-800"><td className="p-3">{player.placement || player.rank}</td><td className="max-w-44 break-words p-3 font-semibold">{person(player.account)}{player.status === "withdrawn" && <span className="ml-1 text-xs text-slate-500">已退赛</span>}</td><td className="p-3 font-bold text-amber-300">{player.points}</td><td className="p-3">{player.wins}/{player.losses}</td><td className="p-3">{player.byes}</td><td className="p-3 tabular-nums">{(player.omw * 100).toFixed(2)}%</td><td className="p-3 tabular-nums">{(player.oomw * 100).toFixed(2)}%</td></tr>)}</tbody></table></div>}

        {section === "manage" && <div className="space-y-5">
          {detail.canEdit && <button className={BUTTON} disabled={busy} onClick={() => { setEditing(true); setFormOpen(true); }}>修改申请并重新审核</button>}
          {detail.canReview && detail.status === "pending" && <ReviewForm tournament={detail} busy={busy} onReview={(action, values) => mutate(action, values)} />}
          {detail.canManage && ["approved", "swiss", "cut"].includes(detail.status) && <RoundControls tournament={detail} busy={busy} onOpen={values => mutate("openRound", values)} onEnd={() => void prepareEnd()} />}
          {detail.canManage && current?.status === "manual" && <div className="space-y-3 rounded-2xl border border-amber-800 p-4"><h2 className="font-bold text-amber-200">逐桌手动结算</h2><p className="text-sm text-slate-400">已确认{current.tables.filter(m => m.confirmed).length}/{current.tables.length}桌。包括已自然结束的桌，也需要逐桌确认。</p>{current.tables.map(table => <ResultEditor key={table.id} table={table} stage={current.stage} label={<>第{table.number}桌　{person(table.player0)} 对 {person(table.player1)}</>} labelText={`第${table.number}桌`} busy={busy} mode="settle" onSubmit={(outcome, reason) => mutate("settle", { tableId: table.id, outcome, reason })} />)}</div>}
          {detail.canJudge && current && ["swiss", "cut"].includes(detail.status) && current.tables.some(m => m.confirmed || m.status === "blocked") && <details className="rounded-xl border border-slate-800 p-4"><summary className="min-h-11 cursor-pointer py-2 font-bold">本轮结果裁定与改判</summary><div className="mt-2 space-y-3">{current.tables.filter(m => m.confirmed || m.status === "blocked").map(table => <ResultEditor key={table.id} table={table} stage={current.stage} label={<>第{table.number}桌　{person(table.player0)} 对 {person(table.player1)}</>} labelText={`第${table.number}桌`} busy={busy} mode="correct" onSubmit={(outcome, reason) => mutate("correct", { tableId: table.id, outcome, reason })} />)}</div></details>}
          {detail.canJudge && detail.appeals.length > 0 && <div className="space-y-3"><h2 className="font-bold">申诉与裁定</h2>{detail.appeals.map(appeal => <AppealResolution key={appeal.id} appeal={appeal} player={person(appeal.account)} busy={busy} onSubmit={reason => mutate("resolveAppeal", { appealId: appeal.id, reason })} />)}</div>}
          {(detail.canManage || detail.canEdit) && <AnnouncementForm initial={detail.announcement} busy={busy} onSubmit={announcement => mutate("announce", { announcement })} />}
          {(detail.canEdit || detail.canManage) && !["completed", "cancelled"].includes(detail.status) && <CancelForm busy={busy} onSubmit={reason => mutate("cancel", { reason })} />}
          {detail.audit.length > 0 && <details className="rounded-xl border border-slate-800 p-4"><summary className="min-h-11 cursor-pointer py-2 font-bold">最近操作记录</summary><ol className="mt-3 space-y-3 text-xs text-slate-400">{[...detail.audit].reverse().map((entry, i) => <li key={`${entry.at}-${i}`} className="break-words">{stamp(entry.at)} · {person(entry.actor)} · {ACTION[entry.action] || entry.action} {entry.detail}</li>)}</ol></details>}
        </div>}
      </>}
    </div>

    <Modal open={formOpen} onClose={busy ? undefined : () => setFormOpen(false)} title={editing ? "修改赛事并重新提交审核" : "申请举办赛事"} mobileSheet>
      {formOpen && <ApplicationForm initial={editing ? detail : null} account={account} busy={busy} onSubmit={async values => { const response = await mutate(editing ? "edit" : "apply", values); if (response) { setFormOpen(false); setSection("manage"); } }} />}
    </Modal>
    <Modal open={!!endConfirmation} onClose={busy ? undefined : () => setEndConfirmation(null)} title="确认结束当前瑞士轮" mobileSheet maxWidthClass="max-w-lg">
      {endConfirmation && <div className="space-y-4"><p className="text-sm leading-6 text-amber-200">即将结束第{endConfirmation.tournament.rounds.at(-1)?.number}轮。进行中{endConfirmation.tournament.rounds.at(-1)?.tables.filter(m => ["playing", "starting"].includes(m.status)).length}桌，未开赛{endConfirmation.tournament.rounds.at(-1)?.tables.filter(m => ["waiting", "blocked"].includes(m.status)).length}桌，共{endConfirmation.tournament.rounds.at(-1)?.tables.length}桌。</p><p className="text-sm leading-6 text-slate-300">确认后立即停止本轮对局，每桌都需要手动结算。全部结果确认后，才能开启下一轮。</p><label className="block text-sm">结束原因<textarea className={`${INPUT} mt-2 min-h-24`} value={endReason} maxLength={500} onChange={e => setEndReason(e.target.value)} /></label><div className="grid grid-cols-2 gap-3"><button className={BUTTON} disabled={busy} onClick={() => setEndConfirmation(null)}>继续本轮</button><button className={`${BUTTON} border-red-700 bg-red-950 text-red-200`} disabled={busy || !endReason.trim()} onClick={async () => { const response = await mutate("endRound", { revision: endConfirmation.tournament.revision, confirmationToken: endConfirmation.token, reason: endReason }); if (response) { setEndConfirmation(null); setSection("manage"); } }}>结束并手动结算</button></div></div>}
    </Modal>
    <Modal open={checkinOpen} onClose={busy ? undefined : () => setCheckinOpen(false)} title="确认赛事签到" mobileSheet maxWidthClass="max-w-lg">
      <div className="space-y-4"><p className="text-sm leading-7 text-amber-200">签到后将不能退出本场赛事，请确认能够按时参加并完成比赛。</p><p className="text-sm leading-6 text-slate-300">候补选手签到后同样不能主动退出。报名截止前仍可更新参赛卡组，更新不会撤销签到。赛事中会在姓名旁展示绑定QQ，方便群内联系。</p><div className="grid grid-cols-1 gap-3 sm:grid-cols-2"><button className={BUTTON} disabled={busy} onClick={() => setCheckinOpen(false)}>暂不签到</button><button className={PRIMARY} disabled={busy || detail?.mine?.checkedIn || detail?.status !== "approved" || !detail.mine || !["registered", "waitlist"].includes(detail.mine.status)} onClick={async () => { if (await mutate("checkin", { confirmCheckin: true })) setCheckinOpen(false); }}>我已了解，确认签到</button></div></div>
    </Modal>
    <Modal open={withdrawOpen} onClose={busy ? undefined : () => setWithdrawOpen(false)} title="确认退出赛事" mobileSheet maxWidthClass="max-w-md"><p className="mb-4 text-sm leading-6">未签到时可以退出赛事；签到后将不能退出。</p><div className="grid grid-cols-2 gap-3"><button className={BUTTON} onClick={() => setWithdrawOpen(false)}>继续参赛</button><button className={BUTTON} disabled={busy || detail?.mine?.checkedIn} onClick={async () => { if (await mutate("withdraw")) setWithdrawOpen(false); }}>确认退赛</button></div></Modal>
  </section>;
}

function clockLabel(table: TournamentTable, now: number) {
  if (table.extraTurnLimit !== null) return "追加回合中";
  if (table.startedAt === null) return table.player1 ? "双方落座后独立计时" : "本轮轮空";
  const seconds = Math.max(0, Math.ceil((table.startedAt + 1_800_000 - now) / 1000));
  return `本桌剩余 ${Math.floor(seconds / 60).toString().padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`;
}

function ApplicationForm({ initial, account, busy, onSubmit }: { initial: TournamentDetail | null; account: string; busy: boolean; onSubmit: (values: Record<string, unknown>) => Promise<void> }) {
  const [capacity, setCapacity] = useState(initial?.capacity || 8);
  const [error, setError] = useState("");
  const start = Date.now() + 7_200_000;
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = Object.fromEntries(new FormData(event.currentTarget));
    const opens = new Date(String(data.registrationOpensAt)).getTime(); const closes = new Date(String(data.registrationClosesAt)).getTime(); const starts = new Date(String(data.startsAt)).getTime();
    if (![opens, closes, starts].every(Number.isFinite) || opens >= closes || closes > starts - 300_000) { setError("请检查时间，报名截止须早于开赛至少5分钟。"); return; }
    setError("");
    await onSubmit({ ...data, capacity, managers: accountList(String(data.managers)), judges: accountList(String(data.judges)), registrationOpensAt: opens, registrationClosesAt: closes, startsAt: starts });
  };
  return <form onSubmit={submit} className="space-y-4">
    <label className="block text-sm">赛事名称<input className={`${INPUT} mt-2`} name="name" maxLength={60} required defaultValue={initial?.name} /></label>
    <div className="grid gap-4 sm:grid-cols-2"><label className="block text-sm">人数规格<select className={`${INPUT} mt-2`} name="capacity" value={capacity} onChange={e => setCapacity(Number(e.target.value))}>{[8, 16, 32, 64, 128, 256].map(n => <option key={n} value={n}>{n}人</option>)}</select></label><label className="block text-sm">参赛卡池<select className={`${INPUT} mt-2`} name="format" defaultValue={initial?.format || "standard"}><option value="standard">平台标准</option><option value="wild">平台狂野</option></select></label></div>
    <p className="text-sm leading-6 text-amber-200">{Math.log2(capacity)}轮瑞士轮，前{capacity <= 16 ? 4 : capacity <= 128 ? 8 : 16}名晋级。全程BO1，每桌30分钟；满员签到后开赛。</p>
    {[["registrationOpensAt", "开放报名", initial?.registrationOpensAt ?? Date.now()], ["registrationClosesAt", "报名截止", initial?.registrationClosesAt ?? start - 600_000], ["startsAt", "正式开赛", initial?.startsAt ?? start]].map(([name, label, value]) => <label key={String(name)} className="block text-sm">{label}<input className={`${INPUT} mt-2`} type="datetime-local" name={String(name)} required defaultValue={localInput(Number(value))} /></label>)}
    <label className="block text-sm">赛事管理员账号<input className={`${INPUT} mt-2`} name="managers" required defaultValue={initial?.managers.join("，") || account} placeholder="多个账号以逗号分隔" /></label>
    <label className="block text-sm">裁判账号<input className={`${INPUT} mt-2`} name="judges" defaultValue={initial?.judges.join("，")} placeholder="可选，多个账号以逗号分隔" /></label>
    <p className="text-xs leading-5 text-slate-400">管理人员与裁判不参加自己管理的赛事。若举办者需要参赛，请指定其他账号管理。</p>
    <label className="block text-sm">赛事简介<textarea className={`${INPUT} mt-2 min-h-24`} name="description" maxLength={4000} defaultValue={initial?.description} /></label>
    <label className="block text-sm">奖励说明<textarea className={`${INPUT} mt-2 min-h-20`} name="rewards" maxLength={1000} defaultValue={initial?.rewards} /></label>
    {error && <p role="alert" className="text-sm text-red-300">{error}</p>}
    <button type="submit" className={`${PRIMARY} w-full`} disabled={busy}>{busy ? "正在提交…" : "提交管理员审核"}</button>
  </form>;
}

function RoundControls({ tournament: t, busy, onOpen, onEnd }: { tournament: TournamentDetail; busy: boolean; onOpen: (values: Record<string, unknown>) => Promise<unknown>; onEnd: () => void }) {
  const current = t.rounds.at(-1);
  const pending = current?.tables.filter(m => !m.confirmed).length || 0;
  const appeals = t.appeals.filter(a => a.status === "open").length;
  const [allowRepeat, setAllowRepeat] = useState(false); const [reason, setReason] = useState("");
  const ready = (!current || current.status === "settled") && !pending && !appeals;
  return <div className="space-y-3 rounded-2xl border border-slate-700 p-4"><h2 className="font-bold">轮次控制</h2><p className="text-sm leading-6 text-slate-400">{current ? `第${current.number}轮 · ${STATUS[current.status]} · 未结算${pending}桌 · 未决申诉${appeals}条` : "等待满员签到后开启首轮"}。全部结果确定后，选择合适时间开启下一轮。</p><div className="flex flex-wrap gap-3"><button className={PRIMARY} disabled={busy || !ready || allowRepeat && !reason.trim()} onClick={() => void onOpen({ allowRepeat, reason })}>{!current ? "开启首轮并公布对阵" : current.stage === "swiss" && t.rounds.filter(r => r.stage === "swiss").length === t.swissRounds ? "确认晋级并开启淘汰赛" : "开启下一轮并公布对阵"}</button>{current?.stage === "swiss" && current.status === "live" && <button className={`${BUTTON} border-red-800 text-red-200`} disabled={busy} onClick={onEnd}>结束当前瑞士轮</button>}</div><details><summary className="min-h-11 cursor-pointer py-3 text-xs text-slate-400">配对异常处理</summary><label className="flex min-h-11 items-center gap-2 text-sm"><input type="checkbox" checked={allowRepeat} onChange={e => setAllowRepeat(e.target.checked)} />裁判已确认必要的重复交手</label>{allowRepeat && <input aria-label="裁判确认重复交手说明" className={INPUT} value={reason} maxLength={500} onChange={e => setReason(e.target.value)} placeholder="填写必须重复交手的原因" />}</details></div>;
}

function ResultEditor({ table, stage, label, labelText, busy, mode, onSubmit }: { table: TournamentTable; stage: string; label: ReactNode; labelText: string; busy: boolean; mode: "settle" | "correct"; onSubmit: (outcome: string, reason: string) => Promise<unknown> }) {
  const [outcome, setOutcome] = useState(table.originalOutcome || table.outcome || (table.player1 ? "" : "bye"));
  const [reason, setReason] = useState(mode === "settle" && table.originalOutcome ? "确认原对局结果" : "");
  const locked = mode === "settle" && table.confirmed;
  return <form className="space-y-3 rounded-xl border border-slate-700 p-3" onSubmit={event => { event.preventDefault(); void onSubmit(outcome, reason); }}><p className="break-words text-sm font-bold">{label}</p>{table.originalOutcome && <p className="text-xs text-slate-400">原结果：{OUTCOME[table.originalOutcome]}</p>}{locked ? <p className="text-sm text-emerald-300">已确认 · {OUTCOME[table.outcome || ""]}</p> : <><label className="block text-xs text-slate-400">最终结果<select className={`${INPUT} mt-1`} required value={outcome} onChange={e => setOutcome(e.target.value)}><option value="">请选择结果</option>{(table.player1 ? ["p0", "p1", ...(stage === "swiss" ? ["doubleLoss"] : [])] : ["bye"]).map(value => <option key={value} value={value}>{OUTCOME[value]}</option>)}</select></label><input className={INPUT} aria-label={`${labelText}结算原因`} placeholder={mode === "settle" ? "填写结算原因" : "填写改判原因"} maxLength={500} value={reason} required onChange={e => setReason(e.target.value)} /><button className={`${BUTTON} w-full`} type="submit" disabled={busy || !outcome || !reason.trim()}>{mode === "settle" ? "确认本桌结果" : "提交改判"}</button></>}</form>;
}

function ReviewForm({ tournament, busy, onReview }: { tournament: TournamentDetail; busy: boolean; onReview: (action: string, values: Record<string, unknown>) => Promise<unknown> }) {
  const [managers, setManagers] = useState(tournament.managers.join("，")); const [judges, setJudges] = useState(tournament.judges.join("，")); const [reason, setReason] = useState("");
  return <div className="space-y-3 rounded-2xl border border-amber-800 p-4"><h2 className="font-bold">平台审核</h2><label className="block text-sm">赛事管理员<input className={`${INPUT} mt-2`} value={managers} onChange={e => setManagers(e.target.value)} /></label><label className="block text-sm">裁判<input className={`${INPUT} mt-2`} value={judges} onChange={e => setJudges(e.target.value)} /></label><label className="block text-sm">审核说明<textarea className={`${INPUT} mt-2`} maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label><div className="grid grid-cols-2 gap-3"><button className={PRIMARY} disabled={busy || !managers.trim()} onClick={() => void onReview("approve", { managers: accountList(managers), judges: accountList(judges), reason })}>审核通过</button><button className={BUTTON} disabled={busy || !reason.trim()} onClick={() => void onReview("reject", { reason })}>驳回并说明原因</button></div></div>;
}

function AppealForm({ busy, onSubmit }: { busy: boolean; onSubmit: (reason: string) => Promise<unknown> }) {
  const [reason, setReason] = useState("");
  return <details className="mt-3"><summary className="min-h-11 cursor-pointer py-3 text-xs text-amber-300">对本桌结果发起申诉</summary><form className="space-y-2" onSubmit={e => { e.preventDefault(); void onSubmit(reason); }}><textarea aria-label="申诉说明" className={INPUT} maxLength={1000} required value={reason} onChange={e => setReason(e.target.value)} /><button className={BUTTON} disabled={busy || !reason.trim()}>提交申诉</button></form></details>;
}
function AppealResolution({ appeal, player, busy, onSubmit }: { appeal: TournamentDetail["appeals"][number]; player: ReactNode; busy: boolean; onSubmit: (reason: string) => Promise<unknown> }) {
  const [reason, setReason] = useState("");
  return <form className="space-y-3 rounded-xl border border-slate-700 p-3" onSubmit={e => { e.preventDefault(); void onSubmit(reason); }}><p className="break-words text-sm">{player}：{appeal.description}</p>{appeal.status === "open" ? <><input aria-label="申诉裁定说明" className={INPUT} required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /><button className={BUTTON} disabled={busy || !reason.trim()}>确认处理申诉</button></> : <p className="text-sm text-emerald-300">已处理：{appeal.resolution}</p>}</form>;
}
function AnnouncementForm({ initial, busy, onSubmit }: { initial: string; busy: boolean; onSubmit: (text: string) => Promise<unknown> }) {
  const [value, setValue] = useState(initial);
  return <form className="space-y-3 rounded-xl border border-slate-800 p-4" onSubmit={e => { e.preventDefault(); void onSubmit(value); }}><label className="block text-sm font-bold">赛事公告<textarea className={`${INPUT} mt-2 min-h-24`} value={value} maxLength={2000} onChange={e => setValue(e.target.value)} /></label><button className={BUTTON} disabled={busy || !value.trim()}>发布赛事公告</button></form>;
}
function CancelForm({ busy, onSubmit }: { busy: boolean; onSubmit: (text: string) => Promise<unknown> }) {
  const [value, setValue] = useState("");
  return <details className="rounded-xl border border-slate-800 p-4"><summary className="min-h-11 cursor-pointer py-2 text-sm text-red-300">取消赛事</summary><form className="mt-3 space-y-3" onSubmit={e => { e.preventDefault(); void onSubmit(value); }}><p className="text-xs text-slate-400">进行中的赛事须先结束并结算当前轮次。取消后不再接受报名或开启新轮次。</p><input className={INPUT} aria-label="取消赛事原因" placeholder="填写取消原因" required maxLength={1000} value={value} onChange={e => setValue(e.target.value)} /><button className={BUTTON} disabled={busy || !value.trim()}>确认取消赛事</button></form></details>;
}
