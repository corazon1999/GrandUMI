#!/usr/bin/env bash
set -Eeuo pipefail

data_dir=/data/grandumi
backup_root="$data_dir/backups"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="$backup_root/$timestamp"

[[ "$data_dir" == /data/grandumi ]] || { echo "备份数据目录安全检查失败" >&2; exit 1; }
install -d -o grandumi -g grandumi -m 0700 "$backup_root" "$target"

count=0
while IFS= read -r -d '' database; do
  name="$(basename "$database")"
  sqlite3 "$database" ".timeout 10000" ".backup '$target/$name'"
  result="$(sqlite3 "$target/$name" 'PRAGMA integrity_check;')"
  [[ "$result" == "ok" ]] || { echo "数据库备份完整性检查失败：$name：$result" >&2; exit 1; }
  count=$((count + 1))
done < <(find "$data_dir" -maxdepth 1 -type f -name '*.db' -print0)

printf 'created_at_utc=%s\ndatabase_count=%s\n' "$timestamp" "$count" > "$target/manifest.txt"
chown -R grandumi:grandumi "$target"
chmod -R u=rwX,go= "$target"

# 只在固定备份根目录内清理七天前的目录；不跟随符号链接。
find "$backup_root" -mindepth 1 -maxdepth 1 -type d -mtime +7 -exec rm -rf -- {} +
echo "SQLite 在线备份完成：$target，数据库=$count"
