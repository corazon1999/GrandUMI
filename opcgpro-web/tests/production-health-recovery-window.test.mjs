import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import {
  chmod,
  mkdir,
  mkdtemp,
  readFile,
  rm,
  writeFile,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test, { after } from "node:test";

const root = new URL("../../", import.meta.url);
const read = (path) => readFile(new URL(path, root), "utf8");

const [healthCheck, healthService] = await Promise.all([
  read("ops/server/grandumi-production-health-check.sh"),
  read("ops/server/grandumi-production-health.service"),
]);

const rootPath = fileURLToPath(root);
let behaviorTempRoot;

const findBash = () => {
  if (process.platform !== "win32") {
    return "/bin/bash";
  }

  const gitExecutables = execFileSync("where.exe", ["git.exe"], {
    encoding: "utf8",
  })
    .split(/\r?\n/)
    .map((value) => value.trim())
    .filter(Boolean);
  for (const gitExecutable of gitExecutables) {
    const candidate = path.resolve(
      path.dirname(gitExecutable),
      "..",
      "bin",
      "bash.exe",
    );
    if (existsSync(candidate)) {
      return candidate;
    }
  }
  throw new Error("未找到可执行真实行为测试的 Bash");
};

const bash = findBash();

const toBashPath = (value) => {
  if (process.platform !== "win32") {
    return value;
  }
  const normalized = path.resolve(value);
  const match = /^([A-Za-z]):[\\/](.*)$/.exec(normalized);
  assert.ok(match, `无法转换为 Git Bash 路径：${normalized}`);
  return `/${match[1].toLowerCase()}/${match[2].replaceAll("\\", "/")}`;
};

const getBehaviorTempRoot = async () => {
  if (behaviorTempRoot) {
    return behaviorTempRoot;
  }

  let baseDirectory;
  if (process.platform === "win32") {
    const helper = path.join(rootPath, "ops", "windows", "GrandUmiTemp.ps1");
    const escapedHelper = helper.replaceAll("'", "''");
    baseDirectory = execFileSync(
      "powershell.exe",
      [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        `. '${escapedHelper}'; Get-GrandUmiTempDirectory -Category 'HealthRecoveryTests'`,
      ],
      { encoding: "utf8" },
    )
      .trim()
      .split(/\r?\n/)
      .at(-1);
  } else {
    baseDirectory = tmpdir();
  }

  behaviorTempRoot = await mkdtemp(
    path.join(baseDirectory, "production-health-"),
  );
  return behaviorTempRoot;
};

after(async () => {
  if (behaviorTempRoot) {
    await rm(behaviorTempRoot, { recursive: true, force: true });
  }
});

const writeExecutable = async (file, lines) => {
  await writeFile(file, `${lines.join("\n")}\n`, "utf8");
  await chmod(file, 0o755);
};

const readInteger = async (file) => {
  try {
    return Number.parseInt((await readFile(file, "utf8")).trim(), 10);
  } catch (error) {
    if (error?.code === "ENOENT") {
      return 0;
    }
    throw error;
  }
};

const bashQuote = (value) => `'${value.replaceAll("'", `'"'"'`)}'`;

const createBehaviorFixture = async ({
  activeEnterSeconds = 800,
  curlSucceedsOnCall = 0,
  deactivateOnSleepCall = 0,
  initialFailures = 2,
  mutateOnSleepCall = 0,
  sleepAdvanceSeconds = 30,
} = {}) => {
  const tempRoot = await getBehaviorTempRoot();
  const fixture = await mkdtemp(path.join(tempRoot, "case-"));
  const state = path.join(fixture, "state");
  const bin = path.join(fixture, "bin");
  const uptime = path.join(fixture, "uptime");
  await Promise.all([
    mkdir(state, { recursive: true }),
    mkdir(bin, { recursive: true }),
  ]);

  const initialInvocation = "a".repeat(32);
  await Promise.all([
    writeFile(path.join(state, "active-slot"), "b\n"),
    writeFile(path.join(state, "health-failures"), `${initialFailures}\n`),
    writeFile(path.join(state, "unit-active"), "1\n"),
    writeFile(path.join(state, "pid"), "111\n"),
    writeFile(path.join(state, "invocation"), `${initialInvocation}\n`),
    writeFile(
      path.join(state, "active-enter-usec"),
      `${activeEnterSeconds * 1_000_000}\n`,
    ),
    writeFile(uptime, "1000.00 0.00\n"),
  ]);

  const stubScripts = {
    flock: ["#!/usr/bin/env bash", "exit 0"],
    logger: [
      "#!/usr/bin/env bash",
      "set -Eeuo pipefail",
      "printf '%s\\n' \"$*\" >> \"$HEALTH_FIXTURE_STATE/logger.log\"",
    ],
    curl: [
      "#!/usr/bin/env bash",
      "set -Eeuo pipefail",
      'file="$HEALTH_FIXTURE_STATE/curl-count"',
      "count=0",
      '[[ -f "$file" ]] && read -r count < "$file"',
      "count=$((count + 1))",
      "printf '%s\\n' \"$count\" > \"$file\"",
      "if (( CURL_SUCCEEDS_ON_CALL > 0 && count >= CURL_SUCCEEDS_ON_CALL )); then",
      "  exit 0",
      "fi",
      "exit 22",
    ],
    sleep: [
      "#!/usr/bin/env bash",
      "set -Eeuo pipefail",
      'file="$HEALTH_FIXTURE_STATE/sleep-count"',
      "count=0",
      '[[ -f "$file" ]] && read -r count < "$file"',
      "count=$((count + 1))",
      "printf '%s\\n' \"$count\" > \"$file\"",
      'read -r uptime _ < "$HEALTH_UPTIME_FILE"',
      'uptime="${uptime%%.*}"',
      "uptime=$((uptime + SLEEP_ADVANCE_SECONDS))",
      "printf '%s.00 0.00\\n' \"$uptime\" > \"$HEALTH_UPTIME_FILE\"",
      "if (( DEACTIVATE_ON_SLEEP_CALL > 0 && count == DEACTIVATE_ON_SLEEP_CALL )); then",
      "  printf '0\\n' > \"$HEALTH_FIXTURE_STATE/unit-active\"",
      "fi",
      "if (( MUTATE_ON_SLEEP_CALL > 0 && count == MUTATE_ON_SLEEP_CALL )); then",
      "  printf '222\\n' > \"$HEALTH_FIXTURE_STATE/pid\"",
      "  printf '%s\\n' \"$(printf 'c%.0s' {1..32})\" > \"$HEALTH_FIXTURE_STATE/invocation\"",
      "fi",
    ],
    systemctl: [
      "#!/usr/bin/env bash",
      "set -Eeuo pipefail",
      'state="$HEALTH_FIXTURE_STATE"',
      'case "${1:-}" in',
      "  show)",
      '    property=""',
      "    while (( $# > 0 )); do",
      '      if [[ "$1" == "--property" ]]; then',
      '        property="$2"',
      "        shift 2",
      "      else",
      "        shift",
      "      fi",
      "    done",
      '    case "$property" in',
      '      MainPID) cat "$state/pid" ;;',
      '      InvocationID) cat "$state/invocation" ;;',
      '      ActiveEnterTimestampMonotonic) cat "$state/active-enter-usec" ;;',
      "      *) exit 2 ;;",
      "    esac",
      "    ;;",
      "  is-active)",
      '    [[ "$(cat "$state/unit-active")" == "1" ]]',
      "    ;;",
      "  restart)",
      '    file="$state/restart-count"',
      "    count=0",
      '    [[ -f "$file" ]] && read -r count < "$file"',
      "    printf '%s\\n' \"$((count + 1))\" > \"$file\"",
      "    printf '1\\n' > \"$state/unit-active\"",
      "    printf '333\\n' > \"$state/pid\"",
      "    printf '%s\\n' \"$(printf 'd%.0s' {1..32})\" > \"$state/invocation\"",
      '    read -r uptime _ < "$HEALTH_UPTIME_FILE"',
      '    uptime="${uptime%%.*}"',
      "    printf '%s\\n' \"$((uptime * 1000000))\" > \"$state/active-enter-usec\"",
      "    ;;",
      "  *) exit 2 ;;",
      "esac",
    ],
    "grandumi-production-switch": [
      "#!/usr/bin/env bash",
      "set -Eeuo pipefail",
      '[[ "${1:-}" == "--failover" ]]',
      'file="$HEALTH_FIXTURE_STATE/failover-count"',
      "count=0",
      '[[ -f "$file" ]] && read -r count < "$file"',
      "printf '%s\\n' \"$((count + 1))\" > \"$file\"",
      "exit 0",
    ],
  };

  await Promise.all(
    Object.entries(stubScripts).map(([name, lines]) =>
      writeExecutable(path.join(bin, name), lines),
    ),
  );

  const bashState = toBashPath(state);
  const bashUptime = toBashPath(uptime);
  const bashSwitch = toBashPath(path.join(bin, "grandumi-production-switch"));
  const renderedHealthCheck = healthCheck
    .replace(
      /^state_dir=\/var\/lib\/grandumi-ha$/m,
      `state_dir=${bashQuote(bashState)}`,
    )
    .replace(
      "read -r uptime_seconds ignored < /proc/uptime",
      `read -r uptime_seconds ignored < ${bashQuote(bashUptime)}`,
    )
    .replace(
      "/usr/local/sbin/grandumi-production-switch --failover",
      `${bashQuote(bashSwitch)} --failover`,
    );
  assert.notEqual(renderedHealthCheck, healthCheck);
  assert.doesNotMatch(renderedHealthCheck, /state_dir=\/var\/lib\/grandumi-ha/);
  assert.doesNotMatch(renderedHealthCheck, /< \/proc\/uptime/);
  assert.doesNotMatch(
    renderedHealthCheck,
    /\/usr\/local\/sbin\/grandumi-production-switch/,
  );

  const healthScript = path.join(fixture, "health-check.sh");
  await writeFile(healthScript, renderedHealthCheck, "utf8");
  await chmod(healthScript, 0o755);

  const result = spawnSync(
    bash,
    ["-c", 'export PATH="$HEALTH_STUB_BIN:$PATH"; exec bash "$HEALTH_SCRIPT"'],
    {
      encoding: "utf8",
      env: {
        ...process.env,
        CURL_SUCCEEDS_ON_CALL: String(curlSucceedsOnCall),
        DEACTIVATE_ON_SLEEP_CALL: String(deactivateOnSleepCall),
        GRANDUMI_PRODUCTION_RECOVERY_TIMEOUT_SECONDS: "60",
        HEALTH_FIXTURE_STATE: bashState,
        HEALTH_SCRIPT: toBashPath(healthScript),
        HEALTH_STUB_BIN: toBashPath(bin),
        HEALTH_UPTIME_FILE: bashUptime,
        MUTATE_ON_SLEEP_CALL: String(mutateOnSleepCall),
        SLEEP_ADVANCE_SECONDS: String(sleepAdvanceSeconds),
      },
      timeout: 10_000,
    },
  );

  return {
    failoverCount: await readInteger(path.join(state, "failover-count")),
    failures: await readInteger(path.join(state, "health-failures")),
    logs: await readFile(path.join(state, "logger.log"), "utf8").catch(() => ""),
    restartCount: await readInteger(path.join(state, "restart-count")),
    result,
    sleepCount: await readInteger(path.join(state, "sleep-count")),
  };
};

test("正式后端慢恢复使用六百秒有界窗口并固定进程世代", () => {
  assert.match(
    healthCheck,
    /GRANDUMI_PRODUCTION_RECOVERY_TIMEOUT_SECONDS:-600/,
  );
  assert.match(
    healthCheck,
    /recovery_timeout_seconds >= 60 && recovery_timeout_seconds <= 600/,
  );
  assert.match(healthCheck, /wait_for_backend_live\(\)/);
  assert.match(healthCheck, /--property MainPID --value/);
  assert.match(healthCheck, /--property InvocationID --value/);
  assert.match(healthCheck, /current_pid" != "\$expected_pid/);
  assert.match(healthCheck, /current_invocation" != "\$expected_invocation/);
  assert.match(healthCheck, /elapsed < timeout_seconds/);
  assert.match(healthCheck, /sleep 1/);
  assert.doesNotMatch(healthCheck, /for _ in \{1\.\.8\}/);
});

test("systemd 已启动的原槽先完成剩余恢复窗口，不被第三次探测重新打断", () => {
  assert.match(healthCheck, /--property ActiveEnterTimestampMonotonic --value/);
  assert.match(healthCheck, /\/proc\/uptime/);
  assert.match(healthCheck, /startup_age < recovery_timeout_seconds/);
  assert.match(
    healthCheck,
    /wait_for_backend_live "\$remaining_seconds" "现有启动恢复"/,
  );

  const protectExisting = healthCheck.indexOf('"现有启动恢复"');
  const restart = healthCheck.indexOf('systemctl restart "$unit"');
  const waitRestart = healthCheck.indexOf('"同槽重启恢复"');
  const failoverCommand = healthCheck.indexOf("grandumi-production-switch --failover");
  assert.ok(protectExisting >= 0 && restart > protectExisting && waitRestart > restart);
  assert.ok(failoverCommand >= 0);
  assert.equal(healthCheck.match(/systemctl restart "\$unit"/g)?.length, 1);
  assert.equal(healthCheck.match(/grandumi-production-switch --failover/g)?.length, 1);
});

test("健康任务执行上限覆盖同槽恢复、备用槽恢复和失败收束", () => {
  assert.match(healthService, /^Type=oneshot$/m);
  assert.match(healthService, /^TimeoutStartSec=1800$/m);
});

test("已健康的原槽立即清零失败计数且不触发重启或切槽", async () => {
  const fixture = await createBehaviorFixture({
    curlSucceedsOnCall: 1,
    initialFailures: 7,
  });
  assert.equal(fixture.result.status, 0, fixture.result.stderr);
  assert.equal(fixture.failures, 0);
  assert.equal(fixture.restartCount, 0);
  assert.equal(fixture.failoverCount, 0);
  assert.equal(fixture.sleepCount, 0);
});

test("systemd 已拉起的年轻进程可慢恢复且不会被再次重启", async () => {
  const fixture = await createBehaviorFixture({
    activeEnterSeconds: 970,
    curlSucceedsOnCall: 3,
    sleepAdvanceSeconds: 15,
  });
  assert.equal(fixture.result.status, 0, fixture.result.stderr);
  assert.equal(fixture.failures, 0);
  assert.equal(fixture.restartCount, 0);
  assert.equal(fixture.failoverCount, 0);
  assert.match(fixture.logs, /现有启动恢复 后恢复，等待 15 秒/);
});

test("成熟失活进程只重启一次并在完整窗口内恢复", async () => {
  const fixture = await createBehaviorFixture({
    activeEnterSeconds: 800,
    curlSucceedsOnCall: 3,
    sleepAdvanceSeconds: 30,
  });
  assert.equal(fixture.result.status, 0, fixture.result.stderr);
  assert.equal(fixture.failures, 0);
  assert.equal(fixture.restartCount, 1);
  assert.equal(fixture.failoverCount, 0);
  assert.match(fixture.logs, /同槽重启恢复 后恢复，等待 30 秒/);
});

test("恢复中的进程退出会结束宽限并只故障转移一次", async () => {
  const fixture = await createBehaviorFixture({
    activeEnterSeconds: 970,
    deactivateOnSleepCall: 1,
    sleepAdvanceSeconds: 10,
  });
  assert.equal(fixture.result.status, 0, fixture.result.stderr);
  assert.equal(fixture.restartCount, 0);
  assert.equal(fixture.failoverCount, 1);
  assert.match(fixture.logs, /现有启动恢复 期间停止运行/);
});

test("恢复中的 PID 或 Invocation 变化会结束宽限并只故障转移一次", async () => {
  const fixture = await createBehaviorFixture({
    activeEnterSeconds: 970,
    mutateOnSleepCall: 1,
    sleepAdvanceSeconds: 10,
  });
  assert.equal(fixture.result.status, 0, fixture.result.stderr);
  assert.equal(fixture.restartCount, 0);
  assert.equal(fixture.failoverCount, 1);
  assert.match(fixture.logs, /现有启动恢复 期间发生进程世代变化/);
});

test("同槽恢复超时后不会重复重启并只故障转移一次", async () => {
  const fixture = await createBehaviorFixture({
    activeEnterSeconds: 800,
    sleepAdvanceSeconds: 30,
  });
  assert.equal(fixture.result.status, 0, fixture.result.stderr);
  assert.equal(fixture.failures, 0);
  assert.equal(fixture.restartCount, 1);
  assert.equal(fixture.failoverCount, 1);
  assert.equal(fixture.sleepCount, 2);
  assert.match(fixture.logs, /同槽重启恢复 的 60 秒窗口内未恢复/);
});
