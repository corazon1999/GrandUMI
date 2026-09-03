import { notFound } from "next/navigation";
import HexActionsLayoutVerification from "@/components/game/HexActionsLayoutVerification";

export const dynamic = "force-dynamic";

export default function HexActionsLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <HexActionsLayoutVerification />;
}
