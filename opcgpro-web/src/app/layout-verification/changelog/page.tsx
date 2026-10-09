import { notFound } from "next/navigation";
import ChangelogLayoutVerification from "@/components/home/ChangelogLayoutVerification";

export const dynamic = "force-dynamic";

export default function ChangelogLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <ChangelogLayoutVerification />;
}
