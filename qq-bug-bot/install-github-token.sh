#!/usr/bin/env sh
set -eu

deploy_dir="/opt/qq-bug-bot"
env_file="$deploy_dir/.env"
tmp_file="$deploy_dir/.env.new"

IFS= read -r token
# Windows PowerShell 的标准输入默认使用 CRLF；只移除行尾单个 CR。
cr=$(printf '\r')
token=${token%"$cr"}
case "$token" in
  github_pat_*|ghp_*) ;;
  *)
    echo "TOKEN_FORMAT_INVALID" >&2
    exit 2
    ;;
esac

if [ "${#token}" -lt 40 ]; then
  echo "TOKEN_TOO_SHORT" >&2
  exit 2
fi

umask 077
trap 'rm -f "$tmp_file"' EXIT INT TERM
printf 'GH_TOKEN=%s\nTZ=Asia/Shanghai\nNAPCAT_UID=1000\nNAPCAT_GID=1000\n' "$token" > "$tmp_file"
chmod 600 "$tmp_file"
mv "$tmp_file" "$env_file"
trap - EXIT INT TERM
unset token
echo "TOKEN_SAVED"
