import { notFound } from "next/navigation";
import HuntersLayoutVerification from "@/components/home/HuntersLayoutVerification";

export const dynamic = "force-dynamic";
export default async function Page({ searchParams }: { searchParams: Promise<{ view?: string; deck?: string }> }) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view, deck } = await searchParams;
  return <HuntersLayoutVerification view={view ?? "effects"} previewDeck={deck === "preview"} />;
}
