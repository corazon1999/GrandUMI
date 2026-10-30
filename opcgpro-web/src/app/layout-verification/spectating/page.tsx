import { notFound } from "next/navigation";
import SpectatingLayoutVerification from "@/components/game/SpectatingLayoutVerification";

export const dynamic = "force-dynamic";

export default async function SpectatingVerificationPage({ searchParams }: {
  searchParams: Promise<{ view?: string; admin?: string }>;
}) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view, admin } = await searchParams;
  return <SpectatingLayoutVerification game={view === "game"} administrator={admin !== "false"} />;
}
