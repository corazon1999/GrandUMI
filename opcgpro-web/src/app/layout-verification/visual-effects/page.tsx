import { notFound } from "next/navigation";
import VisualEffectsLayoutVerification from "@/components/game/VisualEffectsLayoutVerification";

export const dynamic = "force-dynamic";

export default async function Page({ searchParams }: { searchParams: Promise<{ view?: string; hand?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view, hand } = await searchParams;
  return <VisualEffectsLayoutVerification observer={view === "observer"} handLayout={hand === "12"} />;
}
