import { notFound } from "next/navigation";
import GamePreparationLayoutVerification from "@/components/game/GamePreparationLayoutVerification";

export const dynamic = "force-dynamic";

export default async function GamePreparationVerificationPage({ searchParams }: {
  searchParams: Promise<{ view?: string; device?: string }>;
}) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view, device } = await searchParams;
  return <GamePreparationLayoutVerification mobile={device === "mobile"} failed={view === "failed"} />;
}
