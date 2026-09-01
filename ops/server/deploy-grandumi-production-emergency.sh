#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
git_url=https://github.com/corazon1999/GrandUMI.git
production_ip="${GRANDUMI_PRODUCTION_IP:-103.146.230.37}"
test_state_dir=/var/lib/grandumi-test-release
production_deployed_file=/var/lib/grandumi-production-deployed
production_staged_file=/var/lib/grandumi-production-staged
shared_dir=/data/grandumi-shared
release_lock=/run/lock/grandumi-production-emergency-deploy.lock
admin_deploy_lock=/run/lock/grandumi-admin-deploy.lock
mode="${1:-}"
target="${2:-}"
worktree=""
activation_started=0

die() { echo "错误：$*" >&2; exit 1; }

cleanup() {
  local status=$?
  trap - EXIT
  if [[ -n "$worktree" ]]; then
    case "$worktree" in
      /opt/grandumi-emergency-worktree-[0-9a-f]*-[0-9]*)
        git -C "$repo" worktree remove --force "$worktree" >/dev/null 2>&1 || true
        ;;
      *)
        echo "警告：临时 worktree 路径异常，未自动清理：$worktree" >&2
        ;;
    esac
  fi
  if (( status != 0 && activation_started == 1 )); then
    echo "紧急发布在开始激活后失败；切槽脚本会优先自动恢复旧槽，请立即核对 active-slot、/version 和发布快照，禁止手工回退共享账号权威。" >&2
  fi
  exit "$status"
}
trap cleanup EXIT

case "$mode" in
  --emergency|--preflight) ;;
  *) die "用法：deploy-grandumi-production-emergency.sh --preflight|--emergency <40位提交号>" ;;
esac
[[ "$production_ip" == 103.146.230.37 ]] || die "拒绝部署到未登记正式服主机：$production_ip"
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位目标提交号"
[[ "$(id -u)" == 0 ]] || die "正式服紧急发布必须由 root 执行"
[[ -d "$repo/.git" ]] || die "正式服仓库不存在：$repo"

# 与管理面板发布共用互斥锁，避免两个入口同时预构建或切槽；生产切槽本身另有独立锁。
exec 8>"$admin_deploy_lock"
flock -n 8 || die "管理面板发布正在执行，拒绝并发紧急发布"
exec 9>"$release_lock"
flock -n 9 || die "另一个正式服紧急发布正在执行"

verify_test_proof() {
  local tested proof target_tree
  tested="$(tr -d '\r\n' < "$test_state_dir/test-deployed" 2>/dev/null || true)"
  [[ "$tested" == "$target" ]] || die \
    "目标版本尚未成功部署到测试服：测试服 ${tested:-无记录}，目标 $target"

  proof="$test_state_dir/test-verified.json"
  [[ -s "$proof" ]] || die "测试服缺少统一验证证明"
  target_tree="$(git -C "$repo" rev-parse "$target^{tree}")"
  python3 - "$proof" "$target" "$target_tree" <<'PY'
import json
import sys

proof_path, target, target_tree = sys.argv[1:]
with open(proof_path, "r", encoding="utf-8") as handle:
    proof = json.load(handle)

if proof.get("schemaVersion") != "grandumi.verification-proof.v1":
    raise SystemExit("测试服统一验证证明格式无效")
if proof.get("commit") != target or proof.get("tree") != target_tree:
    raise SystemExit("测试服统一验证证明与目标提交或 Git tree 不一致")
suites = proof.get("suites")
if not isinstance(suites, list) or not suites:
    raise SystemExit("测试服统一验证证明没有测试套件")
failed = [str(item.get("name", "未知套件")) for item in suites if item.get("status") != "passed"]
if failed:
    raise SystemExit("测试服统一验证仍有未通过套件：" + "、".join(failed))
PY
}

verify_main() {
  local published_main remote_main pending
  published_main="$(git ls-remote "$git_url" refs/heads/main | awk 'NR == 1 { print $1 }')"
  [[ "$published_main" =~ ^[0-9a-f]{40}$ ]] || die "无法读取 GitHub main 最新提交"
  [[ "$published_main" == "$target" ]] || die \
    "目标提交已不是 GitHub main 最新版本：远端 $published_main，目标 $target"
  remote_main="$(git -C "$repo" rev-parse refs/remotes/origin/main)"
  [[ "$remote_main" == "$target" ]] || die \
    "目标提交已不是远端 main 最新版本：远端 $remote_main，目标 $target"
  git -C "$repo" cat-file -e "$target^{commit}" 2>/dev/null \
    || die "正式服仓库中不存在目标提交 $target"
  pending="$(git -C "$repo" ls-tree -r --name-only "$target" -- changelog-cache/pending \
    | grep -E '\.md$' || true)"
  [[ -z "$pending" ]] || die "目标提交仍有待发布更新日志记录，拒绝正式发布：$pending"
}

active_slot=""
standby_slot=""
active_port=""
deployed=""
current_commit=""

verify_production_state() {
  local ready version expected_backend expected_frontend
  deployed="$(tr -d '\r\n' < "$production_deployed_file" 2>/dev/null || true)"
  [[ "$deployed" =~ ^[0-9a-f]{40}$ ]] || die "正式服已部署版本标记缺失或无效"
  git -C "$repo" cat-file -e "$deployed^{commit}" 2>/dev/null \
    || die "正式服已部署版本对象不存在：$deployed"
  if ! git -C "$repo" merge-base --is-ancestor "$deployed" "$target"; then
    die "目标提交不是当前正式服版本的后继，拒绝覆盖或倒退发布"
  fi

  active_slot="$(tr -d '\r\n' < /var/lib/grandumi-ha/active-slot 2>/dev/null || true)"
  standby_slot="$(tr -d '\r\n' < /var/lib/grandumi-ha/standby-slot 2>/dev/null || true)"
  [[ "$active_slot" =~ ^[ab]$ && "$standby_slot" =~ ^[ab]$ \
      && "$active_slot" != "$standby_slot" ]] \
    || die "正式服 A/B 活动槽或备用槽状态无效"
  systemctl is-active --quiet "grandumi-production-backend@$active_slot.service" \
    || die "正式服活动后端槽未运行：$active_slot"
  systemctl is-active --quiet "grandumi-production-frontend@$active_slot.service" \
    || die "正式服活动前端槽未运行：$active_slot"
  ! systemctl is-active --quiet "grandumi-production-backend@$standby_slot.service" \
    || die "正式服备用后端槽意外运行，拒绝改写其发布链接"
  ! systemctl is-active --quiet "grandumi-production-frontend@$standby_slot.service" \
    || die "正式服备用前端槽意外运行，拒绝改写其发布链接"

  active_port=8080
  [[ "$active_slot" == b ]] && active_port=8082
  ready="$(curl -fsS "http://127.0.0.1:$active_port/ready")"
  python3 -c \
    'import json,sys; d=json.load(sys.stdin); r=d.get("recovery",{}); assert d.get("status")=="ready" and d.get("storage",{}).get("healthy") is True and r.get("pausedRooms",0)==0 and r.get("journalQueueDepth",0)==0 and r.get("snapshotQueueDepth",0)==0' \
    <<<"$ready" || die "正式服活动后端未处于健康就绪状态"
  version="$(curl -fsS "http://127.0.0.1:$active_port/version")"
  current_commit="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("commit", ""))' <<<"$version")"
  [[ "$current_commit" == "$deployed" ]] || die \
    "正式服活动进程与已部署版本标记不一致：进程 $current_commit，标记 $deployed"

  expected_backend="$repo/releases/$deployed/backend"
  expected_frontend="$repo/releases/$deployed/frontend"
  [[ "$(readlink -f "$repo/slots/$active_slot/backend")" == "$expected_backend" \
      && "$(readlink -f "$repo/slots/$active_slot/frontend")" == "$expected_frontend" ]] \
    || die "正式服活动槽链接与已部署版本不一致"
}

verify_shared_account_authority() {
  local test_unit
  [[ -s "$shared_dir/accounts.db" ]] || die "共享账号权威数据库缺失"
  [[ -f "$shared_dir/prepared" ]] || die "共享账号 prepared 标记缺失"
  [[ -f "$shared_dir/active" ]] || die "共享账号 active 标记缺失，禁止只发布代码而遗漏权威激活"
  [[ -x /usr/local/sbin/grandumi-shared-account-migration ]] \
    || die "共享账号迁移校验工具未安装"
  /usr/local/sbin/grandumi-shared-account-migration verify-test
  systemctl is-active --quiet grandumi-test-backend.service \
    || die "测试服后端未运行，无法确认共享账号跨环境权威状态"
  test_unit="$(systemctl cat grandumi-test-backend.service)"
  grep -Fq 'GRANDUMI_ACCOUNT_DB=/data/grandumi-shared/accounts.db' <<<"$test_unit" \
    || die "测试服后端未配置共享账号权威数据库"
  grep -Fq 'GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER=/data/grandumi-shared/active' <<<"$test_unit" \
    || die "测试服后端未配置共享账号激活标记"
}

verify_no_queued_admin_deploy() {
  local queued
  queued="$(find /var/lib/grandumi-admin-deploy/requests -maxdepth 1 -type f -name '*.request' -print -quit 2>/dev/null || true)"
  [[ -z "$queued" ]] || die "管理面板仍有待处理发布请求，拒绝与紧急发布竞争：$queued"
}

verify_release_candidate() {
  verify_main
  verify_test_proof
  verify_production_state
  verify_shared_account_authority
  verify_no_queued_admin_deploy
}

verify_release_candidate

if [[ "$mode" == --preflight ]]; then
  echo "正式服紧急发布只读预检通过：$target（当前正式版本 $deployed，活动槽位 $active_slot）"
  exit 0
fi

if [[ "$deployed" == "$target" ]]; then
  expected_backend="$repo/releases/$target/backend"
  expected_frontend="$repo/releases/$target/frontend"
  [[ "$(readlink -f "$repo/slots/a/backend")" == "$expected_backend" \
      && "$(readlink -f "$repo/slots/b/backend")" == "$expected_backend" \
      && "$(readlink -f "$repo/slots/a/frontend")" == "$expected_frontend" \
      && "$(readlink -f "$repo/slots/b/frontend")" == "$expected_frontend" ]] \
    || die "正式服版本标记已是目标提交，但 A/B 槽未收敛，拒绝把不一致状态当作成功"
  echo "正式服已经是目标版本，全部安全门禁与 A/B 槽状态复核通过：$target"
  exit 0
fi

echo "紧急授权已确认：跳过在线房间排空等待；继续执行测试证明、祖先关系、共享账号和切槽快照门禁。"
worktree="/opt/grandumi-emergency-worktree-${target:0:12}-$$"
git -C "$repo" worktree add --detach "$worktree" "$target" >/dev/null
[[ "$(git -C "$worktree" rev-parse HEAD)" == "$target" ]] \
  || die "紧急发布 worktree 未固定到目标提交"

GRANDUMI_PRODUCTION_IP="$production_ip" \
  bash "$worktree/ops/server/bootstrap-grandumi-production.sh"
GRANDUMI_PRODUCTION_IP="$production_ip" \
  bash "$worktree/ops/server/stage-grandumi-production.sh" "$target"
[[ "$(tr -d '\r\n' < "$production_staged_file" 2>/dev/null || true)" == "$target" ]] \
  || die "预构建完成后的版本标记与目标提交不一致"

# 构建可能持续数分钟；切流前重新读取 GitHub main、测试证明、生产槽和共享账号状态，
# 防止构建期间出现更晚提交、测试服换版、故障切槽或账号权威变化。
verify_release_candidate
/usr/local/sbin/grandumi-shared-account-migration \
  verify-target "$repo/releases/$target/backend"

activation_started=1
bash "$worktree/ops/server/activate-grandumi-production.sh" "$target"
activation_started=0

verify_production_state
[[ "$deployed" == "$target" && "$current_commit" == "$target" ]] \
  || die "正式切槽完成后版本未收敛到目标提交"
verify_shared_account_authority

expected_backend="$repo/releases/$target/backend"
expected_frontend="$repo/releases/$target/frontend"
[[ "$(readlink -f "$repo/slots/a/backend")" == "$expected_backend" \
    && "$(readlink -f "$repo/slots/b/backend")" == "$expected_backend" \
    && "$(readlink -f "$repo/slots/a/frontend")" == "$expected_frontend" \
    && "$(readlink -f "$repo/slots/b/frontend")" == "$expected_frontend" ]] \
  || die "发布成功后 A/B 槽没有收敛到同一目标版本"

active_frontend="$repo/slots/$active_slot/frontend"
python3 - "$active_frontend/public/network-endpoints.json" <<'PY'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    endpoints = json.load(handle).get("endpoints", [])
enabled = [item.get("url") for item in endpoints if item.get("enabled")]
expected = ["wss://direct.grand-umi.com/ws", "wss://ygo.grand-umi.com/ws"]
if enabled != expected:
    raise SystemExit(f"正式服 WebSocket 端点顺序无效：{enabled!r}")
PY
curl -fsS --resolve ygo.grand-umi.com:443:127.0.0.1 \
  https://ygo.grand-umi.com/backend/ready >/dev/null
curl -fsS --resolve direct.grand-umi.com:443:127.0.0.1 \
  https://direct.grand-umi.com/backend/ready >/dev/null

echo "正式服紧急 A/B 发布成功：$target（活动槽位 $active_slot）"
