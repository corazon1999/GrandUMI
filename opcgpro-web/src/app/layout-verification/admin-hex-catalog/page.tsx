import { notFound } from "next/navigation";
import AdminHexCatalogLayoutVerification from "@/components/home/AdminHexCatalogLayoutVerification";

export const dynamic = "force-dynamic";

export default function AdminHexCatalogLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return <AdminHexCatalogLayoutVerification />;
}
