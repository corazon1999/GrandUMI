import { notFound } from "next/navigation";
import CloudReplayLayoutVerification from "@/components/home/CloudReplayLayoutVerification";

export const dynamic = "force-dynamic";

export default function CloudReplayLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <CloudReplayLayoutVerification />;
}
