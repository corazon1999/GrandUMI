import { notFound } from "next/navigation";
import FriendlyHexLayoutVerification from "@/components/home/FriendlyHexLayoutVerification";

export const dynamic = "force-dynamic";

export default async function FriendlyHexLayoutVerificationPage({
  searchParams,
}: {
  searchParams: Promise<{ view?: string }>;
}) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view } = await searchParams;
  return <FriendlyHexLayoutVerification view={view === "room" ? "room" : "lobby"} />;
}
