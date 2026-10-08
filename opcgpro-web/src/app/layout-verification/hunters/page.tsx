import { notFound } from "next/navigation";
import HuntersLayoutVerification from "@/components/home/HuntersLayoutVerification";

export const dynamic = "force-dynamic";
export default async function Page({ searchParams }: { searchParams: Promise<{ view?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view } = await searchParams;
  return <HuntersLayoutVerification view={view ?? "effects"} />;
}
