import { notFound } from "next/navigation";
import TournamentLayoutVerification from "@/components/home/TournamentLayoutVerification";
import TournamentGameLayoutVerification from "@/components/game/TournamentGameLayoutVerification";

export const dynamic = "force-dynamic";
export default async function TournamentVerificationPage({ searchParams }: { searchParams: Promise<{ view?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const params = await searchParams;
  if (params.view === "game") return <TournamentGameLayoutVerification />;
  return <TournamentLayoutVerification manual={params.view === "manual"} />;
}
