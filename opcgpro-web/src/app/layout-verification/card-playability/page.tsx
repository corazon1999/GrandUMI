import { notFound } from "next/navigation";
import CardCatalogPanel from "@/components/home/CardCatalogPanel";

export const dynamic = "force-dynamic";

export default function CardPlayabilityLayoutVerificationPage() {
  if (process.env.GRANDUMI_LAYOUT_VERIFICATION !== "1") notFound();
  return (
    <main
      data-card-playability-layout-verification
      className="@container h-dvh w-full overflow-hidden bg-gray-950"
    >
      <CardCatalogPanel />
    </main>
  );
}
