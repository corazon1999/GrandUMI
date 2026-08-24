#!/usr/bin/env bash
set -Eeuo pipefail

queue_dir=/var/lib/grandumi-admin-deploy/requests
status_dir=/var/lib/grandumi-admin-deploy/status
git_url=https://github.com/corazon1999/GrandUMI.git
lock_file=/run/lock/grandumi-admin-deploy.lock

write_status() {
  local environment="$1" state="$2" target="${3:-}" message="$4"
  local encoded temporary
  encoded="$(printf '%s' "$message" | base64 -w0)"
  temporary="$status_dir/.${environment}.status.$$"
  printf 'state=%s\ntarget=%s\nmessage=%s\nupdated=%s\n' \
    "$state" "$target" "$encoded" "$(date +%s)" > "$temporary"
  chmod 0644 "$temporary"
  mv -f "$temporary" "$status_dir/$environment.status"
}

production_snapshot() {
  local active port
  active="$(cat /var/lib/grandumi-ha/active-slot 2>/dev/null || echo a)"
  port=8080
  [[ "$active" == b ]] && port=8082
  curl -fsS "http://127.0.0.1:$port/ready" | python3 -c \
    'import json,sys; d=json.load(sys.stdin); print(str(bool(d.get("maintenance"))).lower(), int(d.get("rooms", -1)))'
}

deploy_test() {
  local repo=/opt/grandumi-test
  git -C "$repo" fetch --force --prune "$git_url" \
    refs/heads/main:refs/remotes/admin/main || return 1
  target="$(git -C "$repo" rev-parse refs/remotes/admin/main)" || return 1
  [[ "$target" =~ ^[0-9a-f]{40}$ ]] || { echo "远端 main 提交号无效" >&2; return 1; }
  git -C "$repo" show "$target:ops/server/deploy-test.sh" | bash -s -- "$target" all || return 1
}

deploy_production() {
  local repo=/opt/grandumi tested pending snapshot stage_script activate_script
  git -C "$repo" fetch --force --prune "$git_url" \
    refs/heads/main:refs/remotes/admin/main || return 1
  target="$(git -C "$repo" rev-parse refs/remotes/admin/main)" || return 1
  [[ "$target" =~ ^[0-9a-f]{40}$ ]] || { echo "远端 main 提交号无效" >&2; return 1; }

  tested="$(tr -d '\r\n' < /var/lib/grandumi-test-release/test-deployed 2>/dev/null || true)"
  [[ "$tested" == "$target" ]] || {
    echo "正式发布被拒绝：最新版本尚未部署到测试服（测试服 ${tested:-无记录}，目标 $target）" >&2
    return 1
  }
  pending="$(git -C "$repo" ls-tree -r --name-only "$target" -- changelog-cache/pending | grep -E '\.md$' || true)"
  [[ -z "$pending" ]] || {
    echo "正式发布被拒绝：changelog-cache/pending 仍有待发布记录，请先汇总并归档" >&2
    return 1
  }
  snapshot="$(production_snapshot)" || return 1
  [[ "$snapshot" == "true 0" ]] || {
    echo "正式发布被拒绝：必须处于维护模式且进行中房间为 0（当前 $snapshot）" >&2
    return 1
  }

  stage_script="$status_dir/stage-$target.sh"
  activate_script="$status_dir/activate-$target.sh"
  git -C "$repo" show "$target:ops/server/stage-grandumi-production.sh" > "$stage_script" || return 1
  git -C "$repo" show "$target:ops/server/activate-grandumi-production.sh" > "$activate_script" || {
    rm -f "$stage_script"
    return 1
  }
  chmod 0700 "$stage_script" "$activate_script" || {
    rm -f "$stage_script" "$activate_script"
    return 1
  }
  if ! bash "$stage_script" "$target"; then
    rm -f "$stage_script" "$activate_script"
    return 1
  fi
  snapshot="$(production_snapshot)" || {
    rm -f "$stage_script" "$activate_script"
    return 1
  }
  [[ "$snapshot" == "true 0" ]] || {
    rm -f "$stage_script" "$activate_script"
    echo "正式发布切槽前检查失败：维护状态或进行中房间发生变化" >&2
    return 1
  }
  if ! bash "$activate_script" "$target"; then
    rm -f "$stage_script" "$activate_script"
    return 1
  fi
  rm -f "$stage_script" "$activate_script"
}

process_request() {
  local request="$1" filename environment nonce request_environment request_nonce log_file message
  filename="$(basename "$request")"
  if [[ ! "$filename" =~ ^(test|production)-([0-9a-f]{32})\.request$ ]] \
      || [[ ! -f "$request" || -L "$request" ]]; then
    rm -f -- "$request"
    return 0
  fi
  environment="${BASH_REMATCH[1]}"
  nonce="${BASH_REMATCH[2]}"
  request_environment="$(sed -n 's/^environment=//p' "$request")"
  request_nonce="$(sed -n 's/^nonce=//p' "$request")"
  if [[ "$request_environment" != "$environment" || "$request_nonce" != "$nonce" ]]; then
    write_status "$environment" failed "" "发布请求校验失败，未执行任何操作。"
    rm -f -- "$request"
    return 0
  fi

  target=""
  write_status "$environment" running "" "正在获取远端 main 最新版本并执行安全检查。"
  log_file="$(mktemp "/run/grandumi-admin-${environment}.XXXXXX.log")"
  if "deploy_$environment" >"$log_file" 2>&1; then
    message="$(tail -n 1 "$log_file")"
    write_status "$environment" success "$target" "${message:-发布成功。}"
  else
    message="$(tail -n 3 "$log_file" | tr '\n' ' ' | sed 's/[[:space:]]\+/ /g')"
    write_status "$environment" failed "$target" "${message:-发布失败，请查看服务器日志。}"
  fi
  rm -f "$log_file" "$request"
}

install -d -m 0755 "$status_dir"
install -d -o grandumi -g grandumi -m 0750 "$queue_dir"
exec 9>"$lock_file"
flock -n 9 || exit 0
while IFS= read -r -d '' request; do
  process_request "$request"
done < <(find "$queue_dir" -maxdepth 1 -type f -name '*.request' -print0 | sort -z)
