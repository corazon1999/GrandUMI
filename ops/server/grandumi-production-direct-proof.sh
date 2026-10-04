#!/usr/bin/env bash

# 此文件仅提供可复用函数，由固定到目标提交的正式发布 worktree 加载。
grandumi_verify_complete_direct_proof() {
  local source_root="$1"
  local repository="$2"
  local target="$3"
  local proof_path="$4"
  local proof_checksum="$5"
  local proof_root="${6:-/run}"
  local expected_uid="${7:-0}"
  local expected_mode="${8:-600}"
  local target_tree

  [[ "$target" =~ ^[0-9a-f]{40}$ ]] \
    || { echo "错误：Direct 证明缺少有效目标提交" >&2; return 1; }
  [[ "$proof_checksum" =~ ^[0-9a-f]{64}$ ]] \
    || { echo "错误：Direct 证明文件摘要格式无效" >&2; return 1; }
  [[ "$(dirname "$proof_path")" == "$proof_root" \
      && "$(basename "$proof_path")" =~ ^grandumi-direct-proof-${target:0:12}-[0-9a-f]{32}\.json$ ]] \
    || { echo "错误：Direct 证明不在受控路径" >&2; return 1; }
  [[ -f "$proof_path" && ! -L "$proof_path" ]] \
    || { echo "错误：Direct 证明文件缺失或不是普通文件" >&2; return 1; }
  [[ "$(stat -c '%u:%a' "$proof_path")" == "$expected_uid:$expected_mode" ]] \
    || { echo "错误：Direct 证明文件持有者或权限无效" >&2; return 1; }
  [[ -f "$source_root/tools/verification-proof.mjs" ]] \
    || { echo "错误：目标提交缺少 Direct 证明验证器" >&2; return 1; }

  target_tree="$(git -C "$repository" rev-parse "$target^{tree}")" \
    || { echo "错误：无法解析 Direct 目标 Git tree" >&2; return 1; }
  node "$source_root/tools/verification-proof.mjs" verify \
    --proof "$proof_path" \
    --commit "$target" \
    --tree "$target_tree" \
    --checksum "$proof_checksum" \
    --require-complete
}
