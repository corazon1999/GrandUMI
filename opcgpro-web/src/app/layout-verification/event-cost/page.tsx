import { notFound } from "next/navigation";
import EventCostLayoutVerification from "@/components/game/EventCostLayoutVerification";

export const dynamic = "force-dynamic";

export default async function EventCostLayoutVerificationPage({
  searchParams,
}: { searchParams: Promise<{ device?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { device } = await searchParams;
  return <EventCostLayoutVerification mobile={device === "mobile"} />;
}
