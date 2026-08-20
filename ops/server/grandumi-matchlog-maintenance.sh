#!/usr/bin/env bash
set -Eeuo pipefail

matchlog_root="${GRANDUMI_MATCH_LOG_DIR:-/data/grandumi/MatchLogs}"
archive_root="${GRANDUMI_MATCH_LOG_ARCHIVE_DIR:-/data/grandumi/MatchLogsArchive}"
retention_days="${GRANDUMI_MATCH_LOG_RETENTION_DAYS:-14}"
today="$(date -u +%F)"
lock_file="${GRANDUMI_MATCH_LOG_LOCK_FILE:-/data/grandumi/.matchlog-maintenance.lock}"

[[ "$retention_days" =~ ^[0-9]+$ ]] || {
  echo "对局日志保留天数必须是非负整数：$retention_days" >&2
  exit 1
}
[[ -d "$matchlog_root" ]] || exit 0
install -d -m 0750 "$archive_root"

exec 9>"$lock_file"
flock -n 9 || exit 0

# 先清理已过保留期的压缩包，为当天归档预留空间。
find "$archive_root" -maxdepth 1 -type f -name '????-??-??.tar.gz' \
  -mtime "+$retention_days" -delete

for directory in "$matchlog_root"/????-??-??; do
  [[ -d "$directory" ]] || continue
  date_name="$(basename "$directory")"
  [[ "$date_name" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || continue
  [[ "$date_name" < "$today" ]] || continue

  resolved="$(realpath -e -- "$directory")"
  expected="$matchlog_root/$date_name"
  [[ "$resolved" == "$expected" ]] || {
    echo "对局日志目录越界，拒绝处理：$resolved" >&2
    exit 1
  }

  archive="$archive_root/$date_name.tar.gz"
  temporary="$archive.part.$$"
  rm -f -- "$temporary"
  if ! tar -C "$matchlog_root" -czf "$temporary" "$date_name"; then
    rm -f -- "$temporary"
    exit 1
  fi
  gzip -t "$temporary"
  mv -f -- "$temporary" "$archive"
  rm -rf -- "$resolved"
  echo "已归档对局日志：$date_name -> $archive"
done
