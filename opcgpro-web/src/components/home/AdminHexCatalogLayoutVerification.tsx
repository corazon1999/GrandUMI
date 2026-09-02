"use client";

import type { AdminHexCatalogState } from "@/store/netStore";
import type { AdminDeploymentEnvironment, AdminHexCatalogEnvironmentState, HexTierSnapshot } from "@/types/net";
import AdminHexCatalogPanel from "./AdminHexCatalogPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";

const activeDigest = `sha256:${"a".repeat(64)}`;
const draftDigest = `sha256:${"b".repeat(64)}`;
const regularIds = Array.from({ length: 56 }, (_, index) => index + 1)
  .filter((candidate) => candidate !== 27 && candidate !== 30 && candidate !== 48);

function tierFor(id: number): HexTierSnapshot {
  if (id === 30) return "Silver";
  if (id === 48) return "Gold";
  const regularIndex = regularIds.indexOf(id);
  if (regularIndex < 18) return "Silver";
  if (regularIndex < 36) return "Gold";
  return "Rainbow";
}

function environmentState(environment: AdminDeploymentEnvironment): AdminHexCatalogEnvironmentState {
  return {
    environment,
    activeRevision: environment === "production" ? 12 : 18,
    activeDigest,
    activePublishedAt: Date.UTC(2026, 8, 2, 7, 30),
    activePublishedBy: "layout_admin",
    draftRevision: environment === "production" ? 7 : 11,
    baseActiveRevision: environment === "production" ? 12 : 18,
    baseActiveDigest: activeDigest,
    draftDigest,
    draftSavedAt: Date.UTC(2026, 8, 2, 8, 15),
    draftSavedBy: "layout_operator_with_long_name",
    entries: Array.from({ length: 56 }, (_, index) => index + 1).filter((id) => id !== 27).map((id) => {
      const tier = tierFor(id);
      return {
        id,
        name: `布局验证海克斯 ${id}`,
        description: `用于验证手机竖屏长文案、品质选择器和完整触控区域的海克斯效果说明 ${id}。`,
        tier,
        activeTier: id === 1 ? "Gold" : id === 19 ? "Silver" : tier,
        alternative: id === 30 || id === 48,
      };
    }),
    deployment: {
      environment,
      state: "idle",
      targetDigest: null,
      message: "安全执行器待命，当前没有排队中的配置发布。",
      updatedAt: Date.UTC(2026, 8, 2, 8, 20),
    },
  };
}

const PREVIEW_STATE: AdminHexCatalogState = {
  deploymentAvailable: true,
  test: environmentState("test"),
  production: environmentState("production"),
};

export default function AdminHexCatalogLayoutVerification() {
  return (
    <LayoutPreviewFrame mode="desktop">
      <main
        data-admin-hex-layout-verification
        className="h-full min-h-0 overflow-y-auto overflow-x-hidden bg-gray-950 p-3 pb-[max(0.75rem,var(--layout-safe-bottom,env(safe-area-inset-bottom)))] @[640px]:p-5"
      >
        <div className="mx-auto w-full max-w-6xl">
          <AdminHexCatalogPanel previewState={PREVIEW_STATE} />
        </div>
      </main>
    </LayoutPreviewFrame>
  );
}
