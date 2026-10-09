import { notFound } from "next/navigation";
import PlayerIdentityLayoutVerification from "@/components/home/PlayerIdentityLayoutVerification";

export const dynamic = "force-dynamic";
export default async function Page({ searchParams }: { searchParams: Promise<{ view?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view } = await searchParams;
  return <PlayerIdentityLayoutVerification view={view ?? "names"} />;
}
