import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { chmod, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const stage = await readFile(new URL("../../ops/server/stage-grandumi-production.sh", import.meta.url), "utf8");
const activate = await readFile(new URL("../../ops/server/activate-grandumi-production.sh", import.meta.url), "utf8");
const nginx = await readFile(new URL("../../ops/server/grandumi-production.nginx", import.meta.url), "utf8");
const ygoNginx = await readFile(new URL("../../ops/server/grandumi-production-ygo.nginx", import.meta.url), "utf8");
const ygoAcmeNginx = await readFile(new URL("../../ops/server/grandumi-ygo-acme.nginx", import.meta.url), "utf8");
const ygoPrecutNginx = await readFile(new URL("../../ops/server/grandumi-ygo-precut.nginx", import.meta.url), "utf8");
const backendService = await readFile(new URL("../../ops/server/grandumi-production-backend.service", import.meta.url), "utf8");
const candidateNginx = await readFile(new URL("../../ops/server/grandumi-candidate-tls.nginx", import.meta.url), "utf8");
const candidateBackendService = await readFile(new URL("../../ops/server/grandumi-candidate-backend.service", import.meta.url), "utf8");
const candidateFrontendService = await readFile(new URL("../../ops/server/grandumi-candidate-frontend.service", import.meta.url), "utf8");
const candidateBackup = await readFile(new URL("../../ops/server/grandumi-candidate-backup.sh", import.meta.url), "utf8");
const candidateDeploy = await readFile(new URL("../../ops/server/deploy-grandumi-candidate.sh", import.meta.url), "utf8");
const productionBootstrap = await readFile(new URL("../../ops/server/bootstrap-grandumi-production.sh", import.meta.url), "utf8");
const deploy = await readFile(new URL("../../deploy-new-hk-production.ps1", import.meta.url), "utf8");
const emergencyDeploy = await readFile(new URL("../../deploy-hk.ps1", import.meta.url), "utf8");
const emergencyProduction = await readFile(new URL("../../ops/server/deploy-grandumi-production-emergency.sh", import.meta.url), "utf8");
const directTls = await readFile(new URL("../../ops/server/enable-grandumi-production-direct-tls.sh", import.meta.url), "utf8");
const directTlsRenewHook = await readFile(new URL("../../ops/server/renew-grandumi-direct-certificate.sh", import.meta.url), "utf8");
const directTlsCompatChain = await readFile(new URL("../../ops/server/isrg-root-x2-cross-signed.pem", import.meta.url), "utf8");
const emergencyDirectRelay = await readFile(new URL("../../ops/server/grandumi-emergency-direct-relay.caddy", import.meta.url), "utf8");
const enableEmergencyDirectRelay = await readFile(new URL("../../ops/server/enable-grandumi-emergency-direct-relay.sh", import.meta.url), "utf8");
const assetsNginx = await readFile(new URL("../../ops/server/grandumi-assets.nginx", import.meta.url), "utf8");
const enableAssets = await readFile(new URL("../../ops/server/enable-grandumi-assets.sh", import.meta.url), "utf8");
const productionSwitch = await readFile(new URL("../../ops/server/grandumi-production-switch.sh", import.meta.url), "utf8");
const prepareYgoTls = await readFile(new URL("../../ops/server/prepare-grandumi-ygo-tls.sh", import.meta.url), "utf8");
const switchPrimaryDomain = await readFile(new URL("../../ops/server/switch-grandumi-primary-domain.sh", import.meta.url), "utf8");
const promoteApproved = await readFile(new URL("../../ops/server/promote-approved.sh", import.meta.url), "utf8");
const bridge = await readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8");

const repositoryRoot = fileURLToPath(new URL("../..", import.meta.url));
const primaryDomainSwitchPath = path.join(repositoryRoot, "ops", "server", "switch-grandumi-primary-domain.sh");

function findBash() {
  if (process.env.GRANDUMI_BASH && existsSync(process.env.GRANDUMI_BASH)) {
    return process.env.GRANDUMI_BASH;
  }
  if (process.platform !== "win32") {
    return "bash";
  }

  const gitExecPath = spawnSync("git", ["--exec-path"], { encoding: "utf8" });
  assert.equal(gitExecPath.status, 0, `无法定位 Git Bash：${gitExecPath.stderr}`);
  const bashPath = path.resolve(gitExecPath.stdout.trim(), "..", "..", "..", "bin", "bash.exe");
  assert.ok(existsSync(bashPath), `Git Bash 不存在：${bashPath}`);
  return bashPath;
}

function toBashPath(bashPath, nativePath) {
  if (process.platform !== "win32") {
    return nativePath;
  }
  const converted = spawnSync(
    bashPath,
    ["-lc", 'cygpath -u "$1"', "grandumi-domain-cutover-test", nativePath],
    { encoding: "utf8" },
  );
  assert.equal(converted.status, 0, `路径转换失败：${converted.stderr}`);
  return converted.stdout.trim();
}

function resolveGrandUmiTestBase() {
  if (process.platform !== "win32") {
    return process.env.GRANDUMI_TEMP_ROOT ?? path.join(tmpdir(), "GrandUMI-Temp", "Tests");
  }

  const helperPath = path.join(repositoryRoot, "ops", "windows", "GrandUmiTemp.ps1");
  const escapedHelperPath = helperPath.replaceAll("'", "''");
  const result = spawnSync(
    "powershell.exe",
    [
      "-NoProfile",
      "-Command",
      `. '${escapedHelperPath}'; Get-GrandUmiTempDirectory -Category 'Tests'`,
    ],
    { encoding: "utf8" },
  );
  assert.equal(result.status, 0, `无法取得 GrandUMI 测试临时目录：${result.stderr}`);
  return result.stdout.trim();
}

const mockCommands = {
  chmod: `#!/usr/bin/env bash
exit 0
`,
  chown: `#!/usr/bin/env bash
exit 0
`,
  flock: `#!/usr/bin/env bash
exit 0
`,
  install: `#!/usr/bin/env bash
set -euo pipefail
directory_mode=0
operands=()
while (( $# > 0 )); do
  case "$1" in
    -d)
      directory_mode=1
      shift
      ;;
    -m)
      shift 2
      ;;
    *)
      operands+=("$1")
      shift
      ;;
  esac
done
if (( directory_mode == 1 )); then
  mkdir -p "\${operands[@]}"
else
  operand_count="\${#operands[@]}"
  cp "\${operands[operand_count-2]}" "\${operands[operand_count-1]}"
fi
`,
  ln: `#!/usr/bin/env bash
set -euo pipefail
args=("$@")
source_path="\${args[\${#args[@]}-2]}"
target_path="\${args[\${#args[@]}-1]}"
rm -f "$target_path"
printf '%s\\n' "$source_path" > "$target_path"
`,
  nginx: `#!/usr/bin/env bash
exit 0
`,
  openssl: `#!/usr/bin/env bash
exit 0
`,
  ss: `#!/usr/bin/env bash
exit 0
`,
  systemctl: `#!/usr/bin/env bash
if [[ "\${1:-}" == is-active ]]; then
  exit 3
fi
exit 0
`,
  curl: `#!/usr/bin/env bash
set -euo pipefail
root="$GRANDUMI_DOMAIN_CUTOVER_TEST_ROOT"
count_file="$root/mock/curl-count"
count=0
if [[ -f "$count_file" ]]; then
  count="$(<"$count_file")"
fi
count=$((count + 1))
printf '%s\\n' "$count" > "$count_file"

mode=legacy
if [[ -f "$root/etc/grandumi/primary-domain-mode" ]]; then
  mode="$(tr -d '[:space:]' < "$root/etc/grandumi/primary-domain-mode")"
fi
if [[ "$GRANDUMI_DOMAIN_CUTOVER_TEST_ACTION" == cutover ]]; then
  selected_mode=ygo
  previous_mode=legacy
else
  selected_mode=legacy
  previous_mode=ygo
fi

response_mode="$mode"
attempt=$(( (count - 1) / 6 + 1 ))
if [[ "$mode" == "$selected_mode" ]]; then
  if [[ "$GRANDUMI_DOMAIN_CUTOVER_TEST_SCENARIO" == stale-then-converge && "$attempt" -le 2 ]]; then
    response_mode="$previous_mode"
  elif [[ "$GRANDUMI_DOMAIN_CUTOVER_TEST_SCENARIO" == permanent-stale ]]; then
    response_mode="$previous_mode"
  fi
fi

url="\${!#}"
scheme="\${url%%://*}"
remainder="\${url#*://}"
domain="\${remainder%%/*}"
code=000
if [[ "$response_mode" == legacy ]]; then
  case "$scheme|$domain" in
    "http|grand-umi.com") code=308 ;;
    "https|grand-umi.com") code=502 ;;
    "http|ygo.grand-umi.com"|"https|ygo.grand-umi.com") code=503 ;;
    "https|direct.grand-umi.com") code=502 ;;
    "https|assets.grand-umi.com") code=200 ;;
  esac
else
  case "$scheme|$domain" in
    "http|grand-umi.com"|"https|grand-umi.com") code=403 ;;
    "http|ygo.grand-umi.com") code=308 ;;
    "https|ygo.grand-umi.com") code=502 ;;
    "https|direct.grand-umi.com") code=502 ;;
    "https|assets.grand-umi.com") code=200 ;;
  esac
fi
printf '%s' "$code"
`,
};

const mockCurlFunction = `curl() {
${mockCommands.curl.split(/\r?\n/).slice(2).join("\n")}
}
export -f curl
`;

async function writeExecutable(filePath, content) {
  await writeFile(filePath, content, "utf8");
  await chmod(filePath, 0o755);
}

async function prepareDomainSwitchSandbox({ action, scenario }) {
  const bashPath = findBash();
  const tempBase = resolveGrandUmiTestBase();
  await mkdir(tempBase, { recursive: true });
  const nativeRoot = await mkdtemp(path.join(tempBase, "domain-cutover-"));
  const bashRoot = toBashPath(bashPath, nativeRoot);
  const initialMode = action === "cutover" ? "legacy" : "ygo";
  const modeWasPresent = action === "rollback";
  const availableWasPresent = action === "cutover";
  const enabledWasPresent = action === "cutover";

  const directories = [
    "etc/nginx/sites-available",
    "etc/nginx/sites-enabled",
    "etc/grandumi",
    "var/lib/grandumi-domain-cutover",
    "run/lock",
    "opt/grandumi/slots/a/frontend/public",
    "opt/grandumi/slots/b/frontend/public",
    "mock",
    "fake-bin",
  ];
  for (const directory of directories) {
    await mkdir(path.join(nativeRoot, ...directory.split("/")), { recursive: true });
  }
  for (const domain of [
    "grand-umi.com",
    "ygo.grand-umi.com",
    "direct.grand-umi.com",
    "assets.grand-umi.com",
  ]) {
    const certificateDirectory = path.join(nativeRoot, "etc", "letsencrypt", "live", domain);
    await mkdir(certificateDirectory, { recursive: true });
    await writeFile(path.join(certificateDirectory, "fullchain.pem"), `测试证书：${domain}\n`, "utf8");
  }

  const paths = {
    liveConfig: path.join(nativeRoot, "etc", "nginx", "sites-available", "grandumi-production"),
    liveSite: path.join(nativeRoot, "etc", "nginx", "sites-enabled", "grandumi-production"),
    mode: path.join(nativeRoot, "etc", "grandumi", "primary-domain-mode"),
    precutAvailable: path.join(nativeRoot, "etc", "nginx", "sites-available", "grandumi-ygo-precut"),
    precutEnabled: path.join(nativeRoot, "etc", "nginx", "sites-enabled", "grandumi-ygo-precut"),
    runtimeA: path.join(nativeRoot, "opt", "grandumi", "slots", "a", "frontend", "public", "network-endpoints.json"),
    runtimeB: path.join(nativeRoot, "opt", "grandumi", "slots", "b", "frontend", "public", "network-endpoints.json"),
    stateRoot: path.join(nativeRoot, "var", "lib", "grandumi-domain-cutover"),
  };
  const before = {
    liveConfig: `切换前 Nginx：${initialMode}\n`,
    runtimeA: `切换前线路：${initialMode}-a\n`,
    runtimeB: `切换前线路：${initialMode}-b\n`,
    precutAvailable: "切换前 ygo 隔离配置\n",
    precutEnabled: `${bashRoot}/etc/nginx/sites-available/grandumi-ygo-precut\n`,
  };

  await writeFile(path.join(nativeRoot, ".grandumi-domain-cutover-test-root"), "仅供自动化测试\n", "utf8");
  await writeFile(paths.liveConfig, before.liveConfig, "utf8");
  await writeFile(paths.liveSite, `${bashRoot}/etc/nginx/sites-available/grandumi-production\n`, "utf8");
  await writeFile(paths.runtimeA, before.runtimeA, "utf8");
  await writeFile(paths.runtimeB, before.runtimeB, "utf8");
  if (modeWasPresent) {
    await writeFile(paths.mode, `${initialMode}\n`, "utf8");
  }
  if (availableWasPresent) {
    await writeFile(paths.precutAvailable, before.precutAvailable, "utf8");
  }
  if (enabledWasPresent) {
    await writeFile(paths.precutEnabled, before.precutEnabled, "utf8");
  }

  for (const [name, content] of Object.entries(mockCommands)) {
    await writeExecutable(path.join(nativeRoot, "fake-bin", name), content);
  }
  await writeFile(path.join(nativeRoot, "mock", "curl-function.sh"), mockCurlFunction, "utf8");

  return {
    action,
    scenario,
    initialMode,
    modeWasPresent,
    availableWasPresent,
    enabledWasPresent,
    bashPath,
    bashRoot,
    nativeRoot,
    tempBase,
    paths,
    before,
  };
}

function runDomainSwitchSandbox(sandbox) {
  const scriptPath = toBashPath(sandbox.bashPath, primaryDomainSwitchPath);
  const result = spawnSync(
    sandbox.bashPath,
    [
      "-lc",
      'export PATH="$GRANDUMI_DOMAIN_CUTOVER_FAKE_BIN:$PATH"; source "$GRANDUMI_DOMAIN_CUTOVER_CURL_FUNCTION"; bash "$GRANDUMI_DOMAIN_CUTOVER_SCRIPT" "$GRANDUMI_DOMAIN_CUTOVER_TEST_ACTION"',
    ],
    {
      encoding: "utf8",
      env: {
        ...process.env,
        GRANDUMI_DOMAIN_CUTOVER_FAKE_BIN: `${sandbox.bashRoot}/fake-bin`,
        GRANDUMI_DOMAIN_CUTOVER_CURL_FUNCTION: `${sandbox.bashRoot}/mock/curl-function.sh`,
        GRANDUMI_DOMAIN_CUTOVER_SCRIPT: scriptPath,
        GRANDUMI_DOMAIN_CUTOVER_TEST_ACTION: sandbox.action,
        GRANDUMI_DOMAIN_CUTOVER_TEST_MODE: "1",
        GRANDUMI_DOMAIN_CUTOVER_TEST_ROOT: sandbox.bashRoot,
        GRANDUMI_DOMAIN_CUTOVER_TEST_SCENARIO: sandbox.scenario,
        GRANDUMI_DOMAIN_CUTOVER_PROBE_TIMEOUT_SECONDS: "10",
        GRANDUMI_DOMAIN_CUTOVER_PROBE_INTERVAL_SECONDS: "1",
      },
      timeout: 30_000,
    },
  );
  return { ...result, output: `${result.stdout ?? ""}${result.stderr ?? ""}` };
}

async function getOnlyStateDirectory(stateRoot) {
  const entries = (await readdir(stateRoot, { withFileTypes: true })).filter((entry) => entry.isDirectory());
  assert.equal(entries.length, 1, `应只产生一个状态目录，实际为：${entries.map((entry) => entry.name).join(", ")}`);
  return path.join(stateRoot, entries[0].name);
}

async function pathExists(filePath) {
  try {
    await readFile(filePath);
    return true;
  } catch (error) {
    if (error?.code === "ENOENT") {
      return false;
    }
    throw error;
  }
}

async function cleanupDomainSwitchSandbox(sandbox) {
  const relativePath = path.relative(path.resolve(sandbox.tempBase), path.resolve(sandbox.nativeRoot));
  assert.ok(relativePath && !relativePath.startsWith("..") && !path.isAbsolute(relativePath));
  assert.match(path.basename(sandbox.nativeRoot), /^domain-cutover-/);
  await rm(sandbox.nativeRoot, { recursive: true, force: true });
}

test("新正式服预构建固定使用正式 HTTPS/WSS 域名", () => {
  assert.match(stage, /NEXT_PUBLIC_WS_URL='wss:\/\/ygo\.grand-umi\.com\/ws'/);
  assert.match(stage, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/assets\.grand-umi\.com'/);
  assert.match(stage, /"hosts":\["ygo\.grand-umi\.com","direct\.grand-umi\.com"\]/);
  assert.match(stage, /wss:\/\/direct\.grand-umi\.com\/ws/);
  assert.match(stage, /wss:\/\/ygo\.grand-umi\.com\/ws/);
  assert.doesNotMatch(stage, /wss:\/\/candidate\.grand-umi\.com\/ws/);
  assert.match(stage, /尚未切换服务/);
  assert.match(emergencyDeploy, /deploy-grandumi-production-emergency\.sh/);
  assert.match(emergencyProduction, /stage-grandumi-production\.sh/);
  assert.match(promoteApproved, /NEXT_PUBLIC_WS_URL='wss:\/\/ygo\.grand-umi\.com\/ws'/);
  assert.doesNotMatch(
    `${stage}\n${emergencyDeploy}\n${promoteApproved}`,
    /NEXT_PUBLIC_WS_URL='wss:\/\/grand-umi\.com\/ws'/,
  );
  assert.match(bridge, /"ygo\.grand-umi\.com" => "ygo\.grand-umi\.com"/);
});

test("新正式服独立承载静态资源域名并跟随活动槽切换", () => {
  assert.match(assetsNginx, /server_name assets\.grand-umi\.com;/);
  assert.match(assetsNginx, /live\/assets\.grand-umi\.com\/fullchain\.pem/);
  assert.match(assetsNginx, /grandumi-active-frontend-files\.conf/);
  assert.match(assetsNginx, /rewrite \^\/_next\/static\/\(\.\*\)\$ \/\.next\/static\/\$1 break/);
  assert.match(assetsNginx, /grandumi-active-assets\.conf/);
  assert.match(assetsNginx, /grandumi-active-backend\.conf/);
  assert.match(assetsNginx, /\/card-back-images\//);
  assert.match(assetsNginx, /respond 404|return 404/);
  assert.match(productionSwitch, /grandumi-active-frontend-files\.conf/);
  assert.match(productionBootstrap, /enable-grandumi-assets/);
  assert.match(productionBootstrap, /checkhost assets\.grand-umi\.com/);
  assert.match(enableAssets, /certbot certonly --webroot/);
  assert.match(enableAssets, /--deploy-hook "systemctl reload nginx"/);
  assert.match(enableAssets, /sprites-thumb\/CardBack\.webp/);
});

test("正式服发布槽始终挂载不进入 Git 的共享卡图资源", () => {
  assert.match(stage, /shared_asset_root=\/www/);
  assert.match(stage, /card_asset_dirs=\(cards-thumb cards-webp\)/);
  assert.match(stage, /rsync -a "\$source_dir\/" "\$shared_dir\/"/);
  assert.match(stage, /ln -s "\$shared_asset_root\/\$asset_dir" "\$slot_asset_path"/);
  assert.match(stage, /正式服共享卡图目录为空/);
  assert.match(stage, /check-card-image-manifest\.mjs/);
  assert.match(stage, /public\/data\/imageManifest\.json/);
  assert.match(stage, /"\$shared_asset_root"/);
});

test("切换前模板继续承载旧主域，预构建不会提前拒绝现网", () => {
  assert.match(nginx, /server_name grand-umi\.com;/);
  assert.match(nginx, /live\/grand-umi\.com\/fullchain\.pem/);
  assert.match(nginx, /server_name direct\.grand-umi\.com;/);
  assert.match(nginx, /live\/direct\.grand-umi\.com\/fullchain\.pem/);
  assert.doesNotMatch(nginx, /server_name candidate\.grand-umi\.com/);
  assert.equal((nginx.match(/grandumi-production-proxy\.conf/g) ?? []).length, 2);
  assert.match(candidateNginx, /server_name candidate\.grand-umi\.com;/);
  assert.match(candidateNginx, /live\/candidate\.grand-umi\.com\/fullchain\.pem/);
  assert.doesNotMatch(candidateNginx, /default_server/);
});

test("切换后新主域与直连共享正式代理，旧主域只返回 403", () => {
  assert.match(ygoNginx, /server_name ygo\.grand-umi\.com;/);
  assert.match(ygoNginx, /live\/ygo\.grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoNginx, /server_name direct\.grand-umi\.com;/);
  assert.match(ygoNginx, /live\/direct\.grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoNginx, /server_name grand-umi\.com;[\s\S]*return 403;/);
  assert.equal((ygoNginx.match(/grandumi-production-proxy\.conf/g) ?? []).length, 2);
});

test("新主域证书准备始终保持 503 隔离，不会提前开放正式站点", () => {
  assert.match(ygoAcmeNginx, /server_name ygo\.grand-umi\.com;/);
  assert.match(ygoAcmeNginx, /live\/grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoAcmeNginx, /return 503;/);
  assert.match(ygoPrecutNginx, /live\/ygo\.grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoPrecutNginx, /return 503;/);
  assert.match(prepareYgoTls, /HTTP-01 预检/);
  assert.match(prepareYgoTls, /certbot certonly --webroot/);
  assert.match(prepareYgoTls, /-checkhost "\$domain"/);
  assert.match(prepareYgoTls, /strict_code[\s\S]*503/);
});

test("主域切换只允许停机显式执行，并带并发锁、失败回滚和双槽配置更新", () => {
  assert.match(switchPrimaryDomain, /cutover\|rollback/);
  assert.match(switchPrimaryDomain, /flock -n 9/);
  assert.match(switchPrimaryDomain, /systemctl is-active --quiet "\$unit"/);
  assert.match(switchPrimaryDomain, /请先完成维护排空并停服/);
  assert.match(switchPrimaryDomain, /8080\/8082 仍在监听/);
  assert.match(switchPrimaryDomain, /rollback_failed_switch\(\)/);
  assert.match(switchPrimaryDomain, /rollback_failed_switch 1 "\$message"/);
  assert.match(switchPrimaryDomain, /trap 'rollback_failed_switch \$\?/);
  assert.match(switchPrimaryDomain, /rollback_failed_switch 130 "收到中断信号"/);
  assert.match(switchPrimaryDomain, /wait_for_mode\(\)/);
  assert.match(switchPrimaryDomain, /探测总截止时间/);
  assert.match(switchPrimaryDomain, /--noproxy '\*'/);
  assert.match(switchPrimaryDomain, /assets\.grand-umi\.com/);
  assert.match(switchPrimaryDomain, /restore-complete/);
  assert.match(switchPrimaryDomain, /last_old_http_code[\s\S]*last_old_https_code[\s\S]*== 403/);
  assert.match(switchPrimaryDomain, /for slot in a b/);
  assert.match(switchPrimaryDomain, /primary-domain-mode/);
  assert.match(productionBootstrap, /cat "\$domain_mode_file"[\s\S]*echo legacy/);
  assert.match(productionBootstrap, /grandumi-production-ygo\.nginx/);
  assert.doesNotMatch(productionBootstrap, /switch-grandumi-primary-domain[^\n]*cutover/);
  assert.match(activate, /primary_domain=ygo\.grand-umi\.com/);
  assert.match(activate, /旧主域未拒绝访问/);
});

test("主域名切换会等待旧 Nginx worker 收敛，并在永久失败时恢复 cutover 与 rollback 的完整状态", async (t) => {
  for (const action of ["cutover", "rollback"]) {
    await t.test(`${action} 会容忍两轮陈旧 worker 后收敛`, async () => {
      const sandbox = await prepareDomainSwitchSandbox({ action, scenario: "stale-then-converge" });
      try {
        const result = runDomainSwitchSandbox(sandbox);
        assert.equal(result.error, undefined, result.output);
        assert.equal(result.signal, null, result.output);
        assert.equal(result.status, 0, result.output);
        assert.match(result.output, /第 3 次探测/);
        assert.match(result.output, /已收敛到 (?:ygo|legacy) 模式/);

        const expectedMode = action === "cutover" ? "ygo" : "legacy";
        const expectedHost = action === "cutover" ? "ygo.grand-umi.com" : "grand-umi.com";
        assert.equal((await readFile(sandbox.paths.mode, "utf8")).trim(), expectedMode);
        assert.match(await readFile(sandbox.paths.runtimeA, "utf8"), new RegExp(expectedHost.replaceAll(".", "\\.")));
        assert.match(await readFile(sandbox.paths.runtimeB, "utf8"), new RegExp(expectedHost.replaceAll(".", "\\.")));
        assert.equal(await pathExists(sandbox.paths.precutEnabled), action === "rollback");
        assert.equal(await pathExists(sandbox.paths.precutAvailable), true);

        const stateDirectory = await getOnlyStateDirectory(sandbox.paths.stateRoot);
        assert.equal((await readFile(path.join(stateDirectory, "completed-mode"), "utf8")).trim(), expectedMode);
        assert.equal(await pathExists(path.join(stateDirectory, "restore-started")), false);
      } finally {
        await cleanupDomainSwitchSandbox(sandbox);
      }
    });

    await t.test(`${action} 永久不收敛时恢复执行前状态`, async () => {
      const sandbox = await prepareDomainSwitchSandbox({ action, scenario: "permanent-stale" });
      try {
        const result = runDomainSwitchSandbox(sandbox);
        assert.equal(result.error, undefined, result.output);
        assert.equal(result.signal, null, result.output);
        assert.equal(result.status, 1, result.output);
        assert.match(result.output, /未在总截止时间内收敛/);
        assert.match(result.output, new RegExp(`已恢复执行前的 ${sandbox.initialMode} 配置并验证收敛`));

        assert.equal(await readFile(sandbox.paths.liveConfig, "utf8"), sandbox.before.liveConfig);
        assert.equal(
          await readFile(sandbox.paths.liveSite, "utf8"),
          `${sandbox.bashRoot}/etc/nginx/sites-available/grandumi-production\n`,
        );
        assert.equal(await readFile(sandbox.paths.runtimeA, "utf8"), sandbox.before.runtimeA);
        assert.equal(await readFile(sandbox.paths.runtimeB, "utf8"), sandbox.before.runtimeB);
        assert.equal(await pathExists(`${sandbox.paths.runtimeA}.next`), false);
        assert.equal(await pathExists(`${sandbox.paths.runtimeB}.next`), false);
        if (sandbox.modeWasPresent) {
          assert.equal((await readFile(sandbox.paths.mode, "utf8")).trim(), sandbox.initialMode);
        } else {
          assert.equal(await pathExists(sandbox.paths.mode), false);
        }
        assert.equal(await pathExists(sandbox.paths.precutAvailable), sandbox.availableWasPresent);
        assert.equal(await pathExists(sandbox.paths.precutEnabled), sandbox.enabledWasPresent);
        if (sandbox.availableWasPresent) {
          assert.equal(await readFile(sandbox.paths.precutAvailable, "utf8"), sandbox.before.precutAvailable);
        }
        if (sandbox.enabledWasPresent) {
          assert.equal(await readFile(sandbox.paths.precutEnabled, "utf8"), sandbox.before.precutEnabled);
        }

        const stateDirectory = await getOnlyStateDirectory(sandbox.paths.stateRoot);
        assert.equal(await pathExists(path.join(stateDirectory, "restore-started")), true);
        assert.equal(await pathExists(path.join(stateDirectory, "restore-complete")), true);
        assert.equal(await pathExists(path.join(stateDirectory, "restore-failed")), false);
        assert.equal(await pathExists(path.join(stateDirectory, "completed-mode")), false);
      } finally {
        await cleanupDomainSwitchSandbox(sandbox);
      }
    });
  }
});

test("直连启用前必须完成 DNS 独占、证书主机名和活动槽运行时配置校验", () => {
  assert.match(directTls, /direct\.grand-umi\.com/);
  assert.match(directTls, /resolved_ipv4/);
  assert.match(directTls, /103\.146\.230\.37/);
  assert.match(directTls, /openssl x509[\s\S]*-checkhost/);
  assert.match(directTls, /network-endpoints\.json/);
  assert.match(directTls, /wss:\/\/direct\.grand-umi\.com\/ws/);
  assert.match(directTls, /backend\/ready/);
  assert.match(directTls, /--key-type rsa --rsa-key-size 2048/);
  assert.match(directTls, /grandumi-direct-certificate/);
  assert.match(directTlsRenewHook, /isrg-root-x2-cross-signed\.pem/);
  assert.match(directTlsRenewHook, /tail -c "\$compat_bytes"/);
  assert.match(directTlsRenewHook, /openssl x509[\s\S]*-checkhost/);
  assert.match(directTlsCompatChain, /BEGIN CERTIFICATE/);
  assert.match(directTlsCompatChain, /END CERTIFICATE/);
  assert.match(productionBootstrap, /缺少 direct\.grand-umi\.com 证书/);
});

test("候选服使用独立端口、独立数据目录和较低资源上限", () => {
  assert.match(candidateBackendService, /GrandUMIServer\.dll 18080/);
  assert.match(candidateBackendService, /GRANDUMI_DATA_DIR=\/data\/grandumi-candidate/);
  assert.match(candidateBackendService, /MemoryMax=1G/);
  assert.match(candidateFrontendService, /-p 13000/);
  assert.match(candidateNginx, /127\.0\.0\.1:18080\/ws/);
  assert.match(candidateNginx, /127\.0\.0\.1:13000/);
  assert.match(candidateBackup, /data_dir=\/data\/grandumi-candidate/);
  assert.doesNotMatch(candidateBackup, /data_dir=\/data\/grandumi\n/);
  assert.match(candidateDeploy, /GRANDUMI_CANDIDATE_ASSET_ORIGIN:-https:\/\/\$candidate_host/);
  assert.doesNotMatch(productionBootstrap, /rm -f \/etc\/nginx\/sites-enabled\/grandumi-candidate/);
});

test("正式数据未就绪时拒绝激活，失败时恢复候选服务", () => {
  assert.match(activate, /import_dir=\/data\/grandumi-import\/final/);
  assert.match(activate, /\[\[ -f "\$import_dir\/\.ready" \]\]/);
  assert.match(activate, /PRAGMA integrity_check/);
  assert.match(activate, /rollback\(\)/);
  assert.match(activate, /systemctl start grandumi-candidate-backend\.service grandumi-candidate-frontend\.service/);
  assert.match(backendService, /GRANDUMI_NODE_ID=hk-production-01/);
});

test("正式激活会在数据切换前清理候选服重复站点", () => {
  const removeCandidateSite = activate.indexOf("rm -f /etc/nginx/sites-enabled/grandumi-candidate");
  const stopCandidateService = activate.indexOf("systemctl stop grandumi-candidate-frontend.service");
  assert.ok(removeCandidateSite >= 0);
  assert.ok(stopCandidateService > removeCandidateSite);
  assert.match(activate, /systemctl daemon-reload/);
  assert.match(activate, /nginx -t/);
});

test("Windows 部署入口只允许新正式服 IP 且仅做预构建", () => {
  assert.match(deploy, /root@103\.146\.230\.37/);
  assert.doesNotMatch(deploy, /8\.210\.155\.25/);
  assert.match(deploy, /stage-grandumi-production\.sh/);
  assert.match(deploy, /worktree add --detach/);
  assert.doesNotMatch(deploy, /checkout --detach/);
  assert.match(deploy, /尚未切流/);
  assert.match(deploy, /Resolve-DnsName -Type A direct\.grand-umi\.com/);
  assert.match(deploy, /\$_\.Section -eq "Answer"/);
  assert.match(deploy, /\(\[string\]\$_\.Name\)\.TrimEnd\(\[char\]'\.'\)/);
  assert.match(deploy, /\[StringComparison\]::OrdinalIgnoreCase/);
  assert.match(deploy, /低延迟直连 TLS\/健康检查失败/);
});

test("应急直连中转按持久主域模式安全选择上游并保留自动回滚", () => {
  assert.match(emergencyDirectRelay, /direct\.grand-umi\.com/);
  assert.match(emergencyDirectRelay, /reverse_proxy https:\/\/103\.146\.230\.37/);
  assert.match(emergencyDirectRelay, /header_up Host __GRANDUMI_PRIMARY_DOMAIN__/);
  assert.match(emergencyDirectRelay, /tls_server_name __GRANDUMI_PRIMARY_DOMAIN__/);
  assert.doesNotMatch(emergencyDirectRelay, /header_up Host (?:grand-umi|ygo\.grand-umi)\.com/);
  assert.match(enableEmergencyDirectRelay, /primary-domain-mode/);
  assert.match(enableEmergencyDirectRelay, /legacy\) upstream_host=grand-umi\.com/);
  assert.match(enableEmergencyDirectRelay, /ygo\) upstream_host=ygo\.grand-umi\.com/);
  assert.match(enableEmergencyDirectRelay, /未知正式主域模式/);
  assert.match(enableEmergencyDirectRelay, /flock -n 9/);
  assert.match(enableEmergencyDirectRelay, /--resolve "\$upstream_host:443:103\.146\.230\.37"/);
  assert.match(enableEmergencyDirectRelay, /"https:\/\/\$upstream_host\/backend\/ready"/);
  assert.match(enableEmergencyDirectRelay, /placeholder_count[\s\S]*-eq 2/);
  assert.match(enableEmergencyDirectRelay, /direct\.grand-umi\.com\.caddy\.pre-relay-/);
  assert.match(enableEmergencyDirectRelay, /rollback\(\)/);
  assert.match(enableEmergencyDirectRelay, /caddy validate/);
  assert.match(enableEmergencyDirectRelay, /systemctl reload caddy/);
});
