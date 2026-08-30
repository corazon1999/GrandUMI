import { notFound } from "next/navigation";
import OperationsWorkbenchLayoutVerification from "@/components/home/OperationsWorkbenchLayoutVerification";

export const dynamic = "force-dynamic";

export default function OperationsWorkbenchLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <OperationsWorkbenchLayoutVerification />;
}
