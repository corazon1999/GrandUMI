#!/usr/bin/env bash
set -Eeuo pipefail

data_dir=/data/grandumi
archive_root=/data/grandumi-archives
lock_file=/run/lock/grandumi-production-snapshot.lock
release="${1:-}"
required_databases=(players.db ranked.db leader-stats.db)

die() { echo "错误：$*" >&2; exit 1; }
[[ "$data_dir" == /data/grandumi ]] || die "正式数据目录安全检查失败"
[[ "$archive_root" == /data/grandumi-archives ]] || die "正式归档目录安全检查失败"
[[ "$release" =~ ^[0-9a-f]{40}$ ]] || die "一致性快照必须提供 40 位目标提交号"
command -v sqlite3 >/dev/null || die "缺少 sqlite3，无法创建 SQLite 一致性快照"
command -v sha256sum >/dev/null || die "缺少 sha256sum，无法生成快照清单"

for name in "${required_databases[@]}"; do
  [[ -s "$data_dir/$name" ]] || die "缺少正式关键数据库：$name"
done

install -d -m 0750 "$archive_root"
exec 9>"$lock_file"
flock -n 9 || die "另一个正式数据快照正在执行"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="$archive_root/pre-release-${release:0:12}-$timestamp-$$"
install -d -o grandumi -g grandumi -m 0700 "$target"

on_exit() {
  local status=$?
  trap - EXIT
  if (( status != 0 )); then
    printf 'status=failed\ntarget_commit=%s\ncreated_at_utc=%s\n' \
      "$release" "$timestamp" > "$target/FAILED"
    chmod 0600 "$target/FAILED" || true
  fi
  exit "$status"
}
trap on_exit EXIT

total_bytes=0
while IFS= read -r -d '' database; do
  total_bytes=$((total_bytes + $(stat -c %s "$database")))
done < <(find "$data_dir" -maxdepth 1 -type f -name '*.db' -print0)
available_bytes="$(df -PB1 "$archive_root" | awk 'NR==2 {print $4}')"
[[ "$available_bytes" =~ ^[0-9]+$ ]] || die "无法读取正式归档目录可用空间"
minimum_free_after=$((512 * 1024 * 1024))
(( available_bytes >= total_bytes + minimum_free_after )) \
  || die "正式归档空间不足：数据库合计 $total_bytes 字节，可用 $available_bytes 字节"

entries="$target/manifest.entries"
: > "$entries"
count=0
while IFS= read -r -d '' database; do
  name="$(basename "$database")"
  [[ "$name" =~ ^[A-Za-z0-9._-]+\.db$ ]] || die "数据库文件名不安全：$name"
  destination="$target/$name"

  # SQLite 在线备份 API 为每个数据库取得一致视图；不复制 WAL/SHM，也不写源数据库。
  sqlite3 -readonly "$database" ".timeout 30000" ".backup '$destination'"
  result="$(sqlite3 "$destination" 'PRAGMA integrity_check;')"
  [[ "$result" == ok ]] || die "数据库快照完整性检查失败：$name：$result"
  checksum="$(sha256sum "$destination" | awk '{print $1}')"
  size="$(stat -c %s "$destination")"
  printf '%s\t%s\t%s\n' "$name" "$size" "$checksum" >> "$entries"
  count=$((count + 1))
done < <(find "$data_dir" -maxdepth 1 -type f -name '*.db' -print0 | sort -z)

(( count >= ${#required_databases[@]} )) || die "正式 SQLite 快照数量异常：$count"
{
  printf 'status=complete\n'
  printf 'target_commit=%s\n' "$release"
  printf 'created_at_utc=%s\n' "$timestamp"
  printf 'database_count=%s\n' "$count"
  printf 'format=name\\tsize_bytes\\tsha256\n'
  cat "$entries"
} > "$target/manifest.txt"
rm -f "$entries"
printf '%s\n' "$release" > "$target/.complete"
chown -R grandumi:grandumi "$target"
chmod -R u=rwX,go= "$target"

trap - EXIT
printf '%s\n' "$target"
