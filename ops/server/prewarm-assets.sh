#!/usr/bin/env bash
set -Eeuo pipefail

mode="${1:-release}"
root="${GRANDUMI_ASSET_ROOT:-/opt/grandumi/opcgpro-web}"
origin="${GRANDUMI_ASSET_ORIGIN:-https://assets.grand-umi.com}"
concurrency="${GRANDUMI_PREWARM_CONCURRENCY:-1}"
rate="${GRANDUMI_PREWARM_RATE:-128K}"
work_dir="$(mktemp -d)"
paths_file="$work_dir/paths"
failures_file="$work_dir/failures"
trap 'rm -rf "$work_dir"' EXIT

[[ "$mode" == "release" || "$mode" == "catalog" || "$mode" == "all" ]] || {
  echo "错误：预热模式必须是 release、catalog 或 all。" >&2
  exit 1
}

append_files() {
  local directory="$1"
  local prefix="$2"
  [[ -d "$directory" ]] || return 0
  find "$directory" -type f -printf "$prefix/%P\n"
}

append_files "$root/.next/static" "/_next/static" >> "$paths_file"

if [[ "$mode" == "catalog" || "$mode" == "all" ]]; then
  append_files "$root/public/cards-thumb" "/cards-thumb" >> "$paths_file"
  append_files "$root/public/sprites-thumb" "/sprites-thumb" >> "$paths_file"
  for id in $(seq 1 500); do
    echo "/card-back-images/$id" >> "$paths_file"
  done
fi

if [[ "$mode" == "all" ]]; then
  append_files "$root/public/cards-webp" "/cards-webp" >> "$paths_file"
fi

sort -u -o "$paths_file" "$paths_file"
: > "$failures_file"
total="$(wc -l < "$paths_file")"
echo "开始预热静态资源：模式=$mode，数量=$total，并发=$concurrency，单请求限速=$rate"

export origin failures_file rate
xargs -r -d '\n' -P "$concurrency" -n 1 bash -c '
  path="$1"
  code="$(curl -sS --retry 2 --retry-all-errors --connect-timeout 10 --max-time 120 \
    --limit-rate "$rate" -o /dev/null -w "%{http_code}" "$origin$path" || true)"
  case "$code" in
    2??|3??) ;;
    404)
      [[ "$path" == /card-back-images/* ]] || echo "$code $path" >> "$failures_file"
      ;;
    *) echo "${code:-000} $path" >> "$failures_file" ;;
  esac
' _ < "$paths_file"

failures="$(wc -l < "$failures_file" 2>/dev/null || echo 0)"
if [[ "$failures" -ne 0 ]]; then
  echo "错误：有 $failures 个静态资源预热失败。" >&2
  sed -n '1,30p' "$failures_file" >&2
  exit 1
fi

echo "静态资源预热完成：$total 个路径。"
