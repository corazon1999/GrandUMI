#!/usr/bin/env bash
set -Eeuo pipefail

shared_dir=/data/grandumi-shared
shared_db="$shared_dir/accounts.db"
active_marker="$shared_dir/active"
prepared_marker="$shared_dir/prepared"
rollback_dir="$shared_dir/rollback"
precommit_marker="$shared_dir/authority-precommit-snapshot"
formal_players=/data/grandumi/players.db
test_players=/data/grandumi-test/players.db
test_backend=/opt/grandumi-test/服务端WebSocket/publish
lock_file=/run/lock/grandumi-shared-account-migration.lock
mode="${1:-}"
backend_dir="${2:-}"

die() { echo "错误：$*" >&2; exit 1; }

require_shared_marker() {
  local directory="$1"
  [[ -f "$directory/.grandumi-shared-account-v1" ]] \
    || die "后端发布包不支持共享账号库：$directory"
}

validate_database() {
  local database="$1"
  [[ -s "$database" ]] || die "共享账号库不存在或为空：$database"
  [[ "$(sqlite3 -readonly "$database" 'PRAGMA integrity_check;')" == ok ]] \
    || die "共享账号库完整性检查失败：$database"
  [[ "$(sqlite3 -readonly "$database" 'PRAGMA user_version;')" == 1 ]] \
    || die "共享账号库版本不受支持：$database"
  [[ "$(sqlite3 -readonly "$database" \
      'SELECT EXISTS(SELECT 1 FROM shared_account_migration_audit WHERE schema_version=1 AND source_count>0);')" == 1 ]] \
    || die "共享账号库缺少受控源数据迁移审计：$database"
}

validate_prepared_marker() {
  [[ -f "$prepared_marker" ]] || die "共享账号库准备标记不存在"
  [[ "$(wc -l < "$prepared_marker")" -eq 2 ]] \
    && grep -Fxq 'schema=1' "$prepared_marker" \
    && grep -Eq '^prepared_at_utc=[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' \
      "$prepared_marker" \
    || die "共享账号库准备标记内容无效"
}

validate_active_marker() {
  [[ -f "$active_marker" ]] || die "共享账号库激活标记不存在"
  [[ "$(wc -l < "$active_marker")" -eq 2 ]] \
    && grep -Fxq 'schema=1' "$active_marker" \
    && grep -Eq '^activated_at_utc=[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' \
      "$active_marker" \
    || die "共享账号库激活标记内容无效"
}

write_prepared_marker() {
  local next="$shared_dir/prepared.next"
  printf 'schema=1\nprepared_at_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$next"
  chown grandumi:grandumi "$next"
  chmod 0640 "$next"
  mv -f -- "$next" "$prepared_marker"
}

write_active_marker() {
  local next="$shared_dir/active.next"
  printf 'schema=1\nactivated_at_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$next"
  chown grandumi:grandumi "$next"
  chmod 0640 "$next"
  mv -f -- "$next" "$active_marker"
}

require_prepared_database() {
  validate_prepared_marker
  validate_database "$shared_db"
}

write_precommit_snapshot() {
  [[ ! -f "$active_marker" ]] || die "共享账号权威已经激活，拒绝覆盖提交前快照"
  require_prepared_database
  command -v sha256sum >/dev/null || die "缺少 sha256sum，无法校验共享账号提交前快照"

  local timestamp snapshot snapshot_next available required accounts_hash prepared_hash marker_next
  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  snapshot="$rollback_dir/pre-authority-$timestamp-$$"
  snapshot_next="$snapshot.next"
  [[ "$snapshot" =~ ^/data/grandumi-shared/rollback/pre-authority-[0-9]{8}T[0-9]{6}Z-[0-9]+$ ]] \
    || die "共享账号提交前快照路径不安全：$snapshot"
  [[ ! -e "$snapshot" && ! -e "$snapshot_next" ]] \
    || die "共享账号提交前快照目录已存在：$snapshot"

  available="$(df -PB1 "$rollback_dir" | awk 'NR==2 {print $4}')"
  required=$(($(stat -c %s "$shared_db") + 64 * 1024 * 1024))
  [[ "$available" =~ ^[0-9]+$ && "$available" -ge "$required" ]] \
    || die "共享账号提交前快照空间不足"

  # 快照先写入同文件系统的 .next 目录；只有数据库、prepared 和 active=absent
  # 三项全部核对后才原子发布目录与指针，任何部分失败都不会留下可提交标记。
  install -d -o grandumi -g grandumi -m 0700 "$snapshot_next"
  cp -a -- "$shared_db" "$snapshot_next/accounts.db"
  install -o grandumi -g grandumi -m 0600 "$prepared_marker" "$snapshot_next/prepared"
  printf 'absent\n' > "$snapshot_next/active.state"
  chown grandumi:grandumi "$snapshot_next/active.state"
  chmod 0600 "$snapshot_next/active.state"
  validate_database "$snapshot_next/accounts.db"

  accounts_hash="$(sha256sum "$shared_db" | awk '{print $1}')"
  prepared_hash="$(sha256sum "$prepared_marker" | awk '{print $1}')"
  [[ "$(sha256sum "$snapshot_next/accounts.db" | awk '{print $1}')" == "$accounts_hash" ]] \
    || die "共享账号提交前快照与当前 accounts.db 不一致"
  [[ "$(sha256sum "$snapshot_next/prepared" | awk '{print $1}')" == "$prepared_hash" ]] \
    || die "共享账号提交前快照与当前 prepared 标记不一致"
  {
    printf 'status=complete\n'
    printf 'schema=1\n'
    printf 'created_at_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'accounts_sha256=%s\n' "$accounts_hash"
    printf 'prepared_sha256=%s\n' "$prepared_hash"
    printf 'active_state=absent\n'
  } > "$snapshot_next/manifest"
  printf '%s\n' "$accounts_hash" > "$snapshot_next/.complete"
  chown -R grandumi:grandumi "$snapshot_next"
  chmod -R u=rwX,go= "$snapshot_next"
  mv -- "$snapshot_next" "$snapshot"

  marker_next="$precommit_marker.next"
  {
    printf 'snapshot_dir=%s\n' "$snapshot"
    printf 'accounts_sha256=%s\n' "$accounts_hash"
    printf 'prepared_sha256=%s\n' "$prepared_hash"
    printf 'active_state=absent\n'
  } > "$marker_next"
  chown grandumi:grandumi "$marker_next"
  chmod 0640 "$marker_next"
  mv -f -- "$marker_next" "$precommit_marker"
}

require_precommit_snapshot() {
  [[ -f "$precommit_marker" ]] || die "共享账号权威提交前快照标记不存在"
  command -v sha256sum >/dev/null || die "缺少 sha256sum，无法复核共享账号提交前快照"
  [[ "$(wc -l < "$precommit_marker")" -eq 4 ]] \
    || die "共享账号权威提交前快照标记行数无效"

  local snapshot expected_accounts expected_prepared active_state
  snapshot="$(sed -n 's/^snapshot_dir=//p' "$precommit_marker")"
  expected_accounts="$(sed -n 's/^accounts_sha256=//p' "$precommit_marker")"
  expected_prepared="$(sed -n 's/^prepared_sha256=//p' "$precommit_marker")"
  active_state="$(sed -n 's/^active_state=//p' "$precommit_marker")"
  [[ "$snapshot" =~ ^/data/grandumi-shared/rollback/pre-authority-[0-9]{8}T[0-9]{6}Z-[0-9]+$ ]] \
    || die "共享账号权威提交前快照路径无效"
  [[ "$expected_accounts" =~ ^[0-9a-f]{64}$ \
      && "$expected_prepared" =~ ^[0-9a-f]{64}$ \
      && "$active_state" == absent ]] \
    || die "共享账号权威提交前快照标记内容无效"
  [[ -f "$snapshot/.complete" && -f "$snapshot/manifest" \
      && -f "$snapshot/accounts.db" && -f "$snapshot/prepared" \
      && "$(cat "$snapshot/active.state" 2>/dev/null)" == absent ]] \
    || die "共享账号权威提交前快照不完整"
  [[ ! -f "$active_marker" ]] || die "共享账号权威状态已改变，拒绝重复提交旧快照"
  require_prepared_database
  validate_database "$snapshot/accounts.db"
  [[ "$(sha256sum "$shared_db" | awk '{print $1}')" == "$expected_accounts" \
      && "$(sha256sum "$snapshot/accounts.db" | awk '{print $1}')" == "$expected_accounts" ]] \
    || die "accounts.db 已偏离共享账号权威提交前快照"
  [[ "$(sha256sum "$prepared_marker" | awk '{print $1}')" == "$expected_prepared" \
      && "$(sha256sum "$snapshot/prepared" | awk '{print $1}')" == "$expected_prepared" ]] \
    || die "prepared 标记已偏离共享账号权威提交前快照"
  grep -Fxq "$expected_accounts" "$snapshot/.complete" \
    || die "共享账号权威提交前快照完成标记无效"
  grep -Fxq 'status=complete' "$snapshot/manifest" \
    && grep -Fxq "accounts_sha256=$expected_accounts" "$snapshot/manifest" \
    && grep -Fxq "prepared_sha256=$expected_prepared" "$snapshot/manifest" \
    && grep -Fxq 'active_state=absent' "$snapshot/manifest" \
    || die "共享账号权威提交前快照清单无效"
}

formal_backend_is_active() {
  systemctl is-active --quiet grandumi-production-backend.service \
    || systemctl is-active --quiet grandumi-production-backend@a.service \
    || systemctl is-active --quiet grandumi-production-backend@b.service
}

test_backend_is_active() {
  systemctl is-active --quiet grandumi-test-backend.service
}

prepare_database() {
  [[ "$backend_dir" == /opt/grandumi/* ]] \
    || die "迁移只允许使用 /opt/grandumi 下的受控后端发布包"
  require_shared_marker "$backend_dir"
  command -v sqlite3 >/dev/null || die "缺少 sqlite3，无法验证共享账号库"
  command -v runuser >/dev/null || die "缺少 runuser，无法以服务账号执行迁移"
  [[ -x /opt/dotnet/dotnet ]] || die "缺少 /opt/dotnet/dotnet"
  [[ -s "$formal_players" ]] || die "正式服 players.db 不存在，拒绝创建共享账号库"

  install -d -o grandumi -g grandumi -m 0750 "$shared_dir" "$rollback_dir"
  exec 8>"$lock_file"
  flock -n 8 || die "另一个共享账号迁移任务正在执行"

  if [[ -f "$active_marker" ]]; then
    validate_active_marker
    validate_database "$shared_db"
    [[ -f "$prepared_marker" ]] || write_prepared_marker
    validate_prepared_marker
    return 0
  fi
  formal_backend_is_active \
    && die "首次共享账号迁移前必须停止所有正式后端写入"
  test_backend_is_active \
    && die "首次共享账号迁移前必须停止测试后端写入"
  rm -f -- "$prepared_marker" "$shared_dir/prepared.next" \
    "$precommit_marker" "$precommit_marker.next"

  local next="$shared_dir/accounts.db.next.$$"
  local timestamp archive formal_count shared_count
  cleanup_next() {
    rm -f -- "$next" "$next-wal" "$next-shm"
  }
  trap cleanup_next EXIT RETURN
  cleanup_next

  if [[ -s "$shared_db" ]]; then
    validate_database "$shared_db"
    timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
    archive="$rollback_dir/accounts-before-remigration-$timestamp-$$.db"
    sqlite3 -readonly "$shared_db" ".timeout 30000" ".backup '$archive'"
    [[ "$(sqlite3 "$archive" 'PRAGMA integrity_check;')" == ok ]] \
      || die "未激活共享库的保留副本校验失败"
    chown grandumi:grandumi "$archive"
    chmod 0600 "$archive"
  fi

  local arguments=(
    /opt/dotnet/dotnet "$backend_dir/GrandUMIServer.dll"
    --migrate-shared-accounts "$next" "$formal_players"
  )
  [[ -s "$test_players" ]] && arguments+=("$test_players")
  runuser -u grandumi -- "${arguments[@]}"

  # 迁移产物在原子替换前收敛为单文件；正常服务启动后会重新启用 WAL。
  sqlite3 "$next" 'PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;' >/dev/null
  validate_database "$next"
  formal_count="$(sqlite3 -readonly "$formal_players" 'SELECT count(*) FROM players;')"
  shared_count="$(sqlite3 -readonly "$next" 'SELECT count(*) FROM shared_accounts;')"
  [[ "$formal_count" =~ ^[0-9]+$ && "$shared_count" =~ ^[0-9]+$ \
      && "$shared_count" -ge "$formal_count" ]] \
    || die "共享账号数少于正式服源数量，拒绝切换"

  chown grandumi:grandumi "$next"
  chmod 0640 "$next"
  mv -f -- "$next" "$shared_db"
  rm -f -- "$shared_db-wal" "$shared_db-shm"
  write_prepared_marker
  write_precommit_snapshot
  trap - EXIT RETURN
  echo "共享账号库已准备（尚未激活测试服）：$shared_count 个账号"
}

commit_authority() {
  [[ "$backend_dir" == /opt/grandumi/* ]] \
    || die "共享账号权威提交只允许使用 /opt/grandumi 下的受控后端发布包"
  require_shared_marker "$backend_dir"
  command -v sqlite3 >/dev/null || die "缺少 sqlite3，无法验证共享账号库"
  install -d -o grandumi -g grandumi -m 0750 "$shared_dir"
  exec 8>"$lock_file"
  flock -n 8 || die "另一个共享账号迁移任务正在执行"
  require_prepared_database

  if [[ -f "$active_marker" ]]; then
    validate_active_marker
    return 0
  fi
  formal_backend_is_active \
    && die "首次提交共享账号权威前必须停止所有正式后端写入"
  test_backend_is_active \
    && die "首次提交共享账号权威前必须停止测试后端写入"
  require_precommit_snapshot
  write_active_marker
  echo "共享账号库已提交为不可回滚权威源；后续失败只能向前恢复新版服务" || true
}

activate_test() {
  command -v sqlite3 >/dev/null || die "缺少 sqlite3，无法验证共享账号库"
  require_prepared_database
  [[ -f "$active_marker" ]] \
    || die "共享账号权威尚未提交，拒绝由测试服启动流程隐式激活"
  validate_active_marker
  formal_backend_is_active || die "正式后端未运行，拒绝激活测试服共享账号"
  require_shared_marker "$test_backend"
  [[ -f /etc/systemd/system/grandumi-test-backend.service ]] \
    || die "测试服后端 systemd 单元未安装"

  # 必须 restart 而不是 start：若并发测试部署曾在权威标记提交前以本地回退模式启动，
  # start 会把旧进程留在本地库上，restart 才能强制重新解析 active 标记并切到共享库。
  if systemctl restart grandumi-test-backend.service \
      && systemctl is-active --quiet grandumi-test-backend.service \
      && curl -fsS --retry 20 --retry-delay 1 --retry-connrefused \
           http://127.0.0.1:8081/ready >/dev/null; then
    echo "测试服已激活共享账号库"
    return 0
  fi

  # 此阶段共享库已经是唯一权威源。激活标记不得再撤销，
  # 否则测试服会回退到旧本地库并形成双写分叉。保持测试服停止，等待修复后重试。
  systemctl stop grandumi-test-backend.service || true
  die "测试服共享账号激活失败；正式服保持新版与共享权威库，测试服已安全停止"
}

case "$mode" in
  prepare)
    [[ -n "$backend_dir" ]] || die "用法：grandumi-shared-account-migration prepare <backend-dir>"
    prepare_database
    ;;
  commit-authority)
    [[ -n "$backend_dir" ]] || die "用法：grandumi-shared-account-migration commit-authority <backend-dir>"
    commit_authority
    ;;
  activate-test)
    activate_test
    ;;
  verify-target)
    [[ -n "$backend_dir" ]] || die "用法：grandumi-shared-account-migration verify-target <backend-dir>"
    if [[ -f "$active_marker" ]]; then
      validate_active_marker
      require_shared_marker "$backend_dir"
      require_prepared_database
    fi
    ;;
  verify-test)
    require_shared_marker "$test_backend"
    if [[ -f "$active_marker" ]]; then
      validate_active_marker
      require_prepared_database
    fi
    [[ -f /etc/systemd/system/grandumi-test-backend.service ]] \
      || die "测试服后端 systemd 单元未安装"
    ;;
  *)
    die "用法：grandumi-shared-account-migration prepare <backend-dir> | commit-authority <backend-dir> | activate-test | verify-target <backend-dir> | verify-test"
    ;;
esac
