import { notFound } from "next/navigation";
import InactivityRecoveryLayoutVerification from "@/components/game/InactivityRecoveryLayoutVerification";

export default async function InactivityRecoveryLayoutVerificationPage({
  searchParams,
}: {
  searchParams: Promise<{ view?: string; device?: string }>;
}) {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  const { view, device } = await searchParams;
  const normalizedView = view === "reconnecting" || view === "recovering" || view === "failed"
    ? view
    : "warning";
  return (
    <InactivityRecoveryLayoutVerification
      view={normalizedView}
      mobile={device === "mobile"}
    />
  );
}
