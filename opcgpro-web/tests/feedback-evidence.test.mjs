import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("反馈客户端只发送版本、连接和视口白名单，不再上传私有牌局镜像", async () => {
  const [overlay, builder, types, net] = await Promise.all([
    read("../src/components/game/FeedbackOverlay.tsx"),
    read("../src/lib/feedbackEvidence.ts"),
    read("../src/types/net.ts"),
    read("../src/net/NetManager.ts"),
  ]);

  assert.match(overlay, /buildClientFeedbackEvidence/);
  assert.match(overlay, /clientEvidence/);
  assert.doesNotMatch(overlay, /useGameStore|gameStore|clientInfo|userAgent|location\.href|netState\.account|playerName/);
  assert.match(builder, /NEXT_PUBLIC_GRANDUMI_COMMIT/);
  assert.match(builder, /CLIENT_VERSION/);
  assert.match(builder, /connectionGeneration/);
  assert.match(builder, /disconnectCategory: classifyDisconnectReason/);
  assert.doesNotMatch(builder, /lastDisconnectReason.*slice/);
  assert.doesNotMatch(builder, /useGameStore|\.account\b|password|authToken|userAgent|location\.href/);
  assert.match(types, /clientEvidence\?: ClientFeedbackEvidenceV1/);
  assert.match(types, /clientInfo\?: string/);
  assert.match(net, /connectionGeneration: this\.socketGeneration/);
});

test("部署构建把目标提交号注入客户端诊断", async () => {
  const directBuildScripts = await Promise.all([
    read("../../ops/server/deploy-test.sh"),
    read("../../ops/server/deploy-grandumi-candidate.sh"),
    read("../../ops/server/stage-grandumi-production.sh"),
    read("../../ops/server/promote-approved.sh"),
  ]);
  for (const script of directBuildScripts) assert.match(script, /NEXT_PUBLIC_GRANDUMI_COMMIT=/);

  const [windowsEmergencyEntry, emergencyProduction, stageProduction] = await Promise.all([
    read("../../deploy-hk.ps1"),
    read("../../ops/server/deploy-grandumi-production-emergency.sh"),
    read("../../ops/server/stage-grandumi-production.sh"),
  ]);
  assert.match(windowsEmergencyEntry, /deploy-grandumi-production-emergency\.sh/);
  assert.match(emergencyProduction, /stage-grandumi-production\.sh/);
  assert.match(stageProduction, /NEXT_PUBLIC_GRANDUMI_COMMIT="\$target"/);
});
