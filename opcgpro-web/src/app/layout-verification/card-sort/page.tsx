import { notFound } from "next/navigation";
import CardSortLayoutVerification from "@/components/game/CardSortLayoutVerification";

export const dynamic = "force-dynamic";

export default async function CardSortLayoutVerificationPage({
  searchParams,
}: { searchParams: Promise<{ device?: string; observer?: string; crowded?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { device, observer, crowded } = await searchParams;
  return <CardSortLayoutVerification mobile={device === "mobile"} observer={observer === "1"} crowded={crowded === "1"} />;
}
