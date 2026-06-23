#!/usr/bin/env bash
# ============================================================
#  sync-assets-hk.sh — 增量同步卡图到香港线上 (8.210.155.25)
#  仅在「新增/替换了卡图」后需要跑(卡图不在 git 里,走这里单独传)。
#  在 Git Bash 里执行:   bash sync-assets-hk.sh
#  原理:用 git 里没有的三大资源目录做增量(只传上次同步后改动的文件)。
#       首次跑=全量;之后靠 .last-asset-sync 时间戳做增量。
#  注:新卡的「数据(JSON)」走 git → 用 deploy-hk.ps1 推送重建;
#      本脚本只负责「图片二进制」。两者都做才完整。
# ============================================================
set -uo pipefail
SRV="root@8.210.155.25"
REPO="/d/Self/GrandUMI"
cd "$REPO"
MARKER="$REPO/.last-asset-sync"

if [ -f "$MARKER" ]; then
  SINCE=$(cat "$MARKER")
  NEWER="--newer-mtime=@$SINCE"
  echo "增量模式:只传 $(date -d @"$SINCE" '+%F %T') 之后改动的卡图"
else
  SINCE=0
  NEWER=""
  echo "首次:全量同步"
fi
NOW=$(date +%s)

sync_one() {
  local src="$1" dst="$2" label="$3"
  local cnt
  if [ "$SINCE" -gt 0 ]; then
    cnt=$(find "$src" -type f -newermt "@$SINCE" 2>/dev/null | wc -l)
  else
    cnt=$(find "$src" -type f 2>/dev/null | wc -l)
  fi
  echo "[$label] 待传 $cnt 文件"
  [ "${cnt:-0}" -eq 0 ] && { echo "[$label] 无新文件,跳过"; return; }
  tar -C "$src" $NEWER -cf - . | ssh -o BatchMode=yes "$SRV" "mkdir -p $dst && tar -C $dst -xf -"
  echo "[$label] 完成"
}

sync_one "opcgpro-web/public/sprites"     "/opt/grandumi/opcgpro-web/public/sprites"     "sprites"
sync_one "opcgpro-web/public/cards-thumb" "/opt/grandumi/opcgpro-web/public/cards-thumb" "cards-thumb"
sync_one "CardImages"                     "/opt/grandumi/CardImages"                     "CardImages"

echo "$NOW" > "$MARKER"
echo "===== 卡图同步完成 ====="
echo "提示:若同时新增了卡牌数据,记得再跑  .\\deploy-hk.ps1  推送代码+重建前端。"
