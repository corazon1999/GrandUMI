#!/usr/bin/env bash
# ============================================================
#  sync-assets-hk.sh — 增量同步卡图到香港正式服 (103.146.230.37)
#  仅在「新增/替换了卡图」后需要跑(卡图不在 git 里,走这里单独传)。
#  在 Git Bash 里执行:   bash sync-assets-hk.sh
#  原理:按“相对路径 + 文件大小”核对本机与远端，只传缺失或大小不同的文件。
#       不依赖时间戳，因此远端文件意外丢失后再次运行也能自动补齐。
#  注:新卡的「数据(JSON)」走 git → 用 deploy-hk.ps1 推送重建;
#      本脚本只负责「图片二进制」。两者都做才完整。
# ============================================================
set -Eeuo pipefail
SRV="root@103.146.230.37"
REPO="/d/Self/GrandUMI"
cd "$REPO"

# 先确认本地清单引用的派生图完整，避免把本地半成品同步到正式服。
node "opcgpro-web/scripts/check-card-image-manifest.mjs" \
  "opcgpro-web/public/data/imageManifest.json" \
  "opcgpro-web/public"

sync_one() {
  local src="$1" dst="$2" label="$3"
  local remote_manifest pending_lines pending_paths cnt
  remote_manifest=$(ssh -o BatchMode=yes "$SRV" \
    "mkdir -p '$dst'; cd '$dst'; find . -type f -printf '%P\\t%s\\n'" | LC_ALL=C sort)
  pending_lines=$(comm -23 \
    <(find "$src" -type f -printf '%P\t%s\n' | LC_ALL=C sort) \
    <(printf '%s\n' "$remote_manifest"))
  cnt=$(printf '%s\n' "$pending_lines" | sed '/^$/d' | wc -l)
  echo "[$label] 待传 $cnt 文件"
  [ "${cnt:-0}" -eq 0 ] && { echo "[$label] 无新文件,跳过"; return; }
  pending_paths=$(printf '%s\n' "$pending_lines" | cut -f1)
  tar -C "$src" -cf - -T <(printf '%s\n' "$pending_paths") \
    | ssh -o BatchMode=yes "$SRV" "tar -C '$dst' -xf -"
  echo "[$label] 完成"
}

sync_one "opcgpro-web/public/sprites"     "/opt/grandumi/opcgpro-web/public/sprites" "sprites"
sync_one "opcgpro-web/public/cards-thumb" "/www/cards-thumb"                        "cards-thumb"
sync_one "opcgpro-web/public/cards-webp"  "/www/cards-webp"                         "cards-webp"
sync_one "CardImages"                     "/opt/grandumi/CardImages"                 "CardImages"

# /www 是活动 A/B 槽实际读取的共享目录；同时回填仓库外资源源目录，确保下次正式发布
# 执行 stage-grandumi-production.sh 时仍能从持久源重新建立完整共享资源。
ssh -o BatchMode=yes "$SRV" '
  set -Eeuo pipefail
  for asset_dir in cards-thumb cards-webp; do
    source_dir="/opt/grandumi/opcgpro-web/public/$asset_dir"
    mkdir -p "$source_dir"
    rsync -a "/www/$asset_dir/" "$source_dir/"
  done
'

# 用当前本地清单逐项核对正式服共享目录。清单路径通过标准输入传递，不依赖服务器
# 是否已经拉取包含本校验器的最新提交。
node "opcgpro-web/scripts/check-card-image-manifest.mjs" \
  "opcgpro-web/public/data/imageManifest.json" \
  "opcgpro-web/public" \
  --list \
  | ssh -o BatchMode=yes "$SRV" '
      set -Eeuo pipefail
      missing=0
      while IFS= read -r relative_path; do
        case "$relative_path" in
          cards-thumb/*|cards-webp/*) ;;
          *) echo "拒绝校验异常卡图路径：$relative_path" >&2; exit 2 ;;
        esac
        if [ ! -s "/www/$relative_path" ]; then
          echo "缺少正式服卡图：/www/$relative_path" >&2
          missing=$((missing + 1))
        fi
      done
      if [ "$missing" -ne 0 ]; then
        echo "正式服卡图清单校验失败：缺少 $missing 个文件。" >&2
        exit 1
      fi
      echo "正式服卡图清单校验通过。"
    '

echo "===== 卡图同步完成 ====="
echo "提示:若同时新增了卡牌数据,记得再跑  .\\deploy-hk.ps1  推送代码+重建前端。"
