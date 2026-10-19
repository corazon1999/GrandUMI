import { notFound } from "next/navigation";
import VisualEffectsLayoutVerification from "@/components/game/VisualEffectsLayoutVerification";

export const dynamic = "force-dynamic";

export default function Page() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <VisualEffectsLayoutVerification />;
}
