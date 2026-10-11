import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const monitor = fileURLToPath(new URL("../../ops/server/grandumi-network-monitor.sh", import.meta.url));
const bash = process.env.GRANDUMI_BASH ?? "bash";
const bashAvailable = spawnSync(bash, ["--version"], { encoding: "utf8" }).status === 0;
const shellPath = value => process.platform === "win32"
  ? value.replace(/\\/g, "/").replace(/^([A-Za-z]):/, (_, drive) => `/${drive.toLowerCase()}`) : value;

test("网络监控读取活动槽位，切换 B 槽后不再抓取 A 槽", { skip: !bashAvailable }, () => {
  const root = process.env.GRANDUMI_TEST_TEMP_ROOT;
  assert.ok(root, "网络监控回归必须配置隔离临时目录");
  const temp = mkdtempSync(path.join(root, "monitor-slot-"));
  const slotFile = path.join(temp, "active-slot");
  const run = (override = "") => spawnSync(bash, [shellPath(monitor), "--metrics-url"], {
    encoding: "utf8",
    env: { ...process.env, GRANDUMI_ACTIVE_SLOT_FILE: shellPath(slotFile), GRANDUMI_METRICS_URL: override },
  });
  try {
    let result = run();
    assert.equal(result.status, 0, result.error?.message ?? result.stderr);
    assert.equal(result.stdout.trim(), "http://127.0.0.1:8080/metrics");
    writeFileSync(slotFile, "a\n");
    assert.equal(run().stdout.trim(), "http://127.0.0.1:8080/metrics");
    writeFileSync(slotFile, "b\n");
    assert.equal(run().stdout.trim(), "http://127.0.0.1:8082/metrics");
    assert.equal(run("http://127.0.0.1:8081/metrics").stdout.trim(), "http://127.0.0.1:8081/metrics");
    writeFileSync(slotFile, "无效槽位\n");
    result = run();
    assert.equal(result.status, 1);
    assert.equal(result.stdout, "");
    assert.match(result.stderr, /活动后端槽位无效/);
  } finally {
    rmSync(temp, { recursive: true });
  }
});

test("监控服务不固定 A 槽端口，输出明确标记应用指标可用性", { skip: !bashAvailable }, () => {
  const unit = readFileSync(new URL("../../ops/server/grandumi-network-monitor.service", import.meta.url), "utf8");
  assert.doesNotMatch(unit, /GRANDUMI_METRICS_URL=.*8080/);
  assert.match(unit, /GRANDUMI_ACTIVE_SLOT_FILE=\/var\/lib\/grandumi-ha\/active-slot/);
  const syntax = spawnSync(bash, ["-n", shellPath(monitor)], { encoding: "utf8" });
  assert.equal(syntax.status, 0, syntax.error?.message ?? syntax.stderr);
  assert.match(readFileSync(monitor, "utf8"), /"metrics_available":%s/);
});
