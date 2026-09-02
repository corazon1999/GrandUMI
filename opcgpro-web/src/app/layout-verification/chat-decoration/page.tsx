import { notFound } from "next/navigation";
import ChatDecorationLayoutVerification from "@/components/game/ChatDecorationLayoutVerification";

export default async function ChatDecorationLayoutVerificationPage({
  searchParams,
}: {
  searchParams: Promise<{ view?: string }>;
}) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view } = await searchParams;
  const normalized = view === "exchange" || view === "terminal" ? view : "opening";
  return <ChatDecorationLayoutVerification view={normalized} />;
}
