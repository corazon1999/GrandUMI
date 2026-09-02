import { notFound } from "next/navigation";
import AdminPanelLayoutVerification from "@/components/home/AdminPanelLayoutVerification";

export const dynamic = "force-dynamic";

export default function AdminPanelLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <AdminPanelLayoutVerification />;
}
