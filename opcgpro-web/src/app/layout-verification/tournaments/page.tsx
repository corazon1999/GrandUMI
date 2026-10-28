import { notFound } from "next/navigation";
import TournamentLayoutVerification from "@/components/home/TournamentLayoutVerification";
import TournamentGameLayoutVerification from "@/components/game/TournamentGameLayoutVerification";

export const dynamic = "force-dynamic";
export default async function TournamentVerificationPage({ searchParams }: { searchParams: Promise<{ view?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const params = await searchParams;
  if (params.view === "game" || params.view === "game-overdue" || params.view === "game-final")
    return <TournamentGameLayoutVerification timing={params.view === "game-overdue" ? "overdue" : params.view === "game-final" ? "final" : undefined} />;
  return <TournamentLayoutVerification manual={params.view === "manual"} bye={params.view === "bye"} timing={params.view === "overdue" || params.view === "final" ? params.view : undefined} registration={params.view === "checkin" || params.view === "checkedin" || params.view === "host" ? params.view : undefined} />;
}
