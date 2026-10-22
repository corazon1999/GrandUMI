import { notFound } from "next/navigation";
import VisualEffectsLayoutVerification from "@/components/game/VisualEffectsLayoutVerification";

export const dynamic = "force-dynamic";

export default async function Page({ searchParams }: { searchParams: Promise<{ view?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view } = await searchParams;
  return <VisualEffectsLayoutVerification observer={view === "observer"} />;
}
