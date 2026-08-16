#!/usr/bin/env bash
set -Eeuo pipefail

domain=direct.grand-umi.com
live_chain="/etc/letsencrypt/live/$domain/fullchain.pem"
compat_chain=/etc/letsencrypt/compat/isrg-root-x2-cross-signed.pem

[[ -f "$live_chain" ]] || {
  echo "缺少直连证书链：$live_chain" >&2
  exit 1
}
[[ -f "$compat_chain" ]] || {
  echo "缺少 Windows/Node 兼容链：$compat_chain" >&2
  exit 1
}

chain_target="$(readlink -f "$live_chain")"
compat_bytes="$(wc -c < "$compat_chain")"
if ! tail -c "$compat_bytes" "$chain_target" | cmp -s - "$compat_chain"; then
  cat "$compat_chain" >> "$chain_target"
fi

openssl x509 -in "$live_chain" -noout -checkhost "$domain" >/dev/null
nginx -t
systemctl reload nginx
echo "正式服低延迟直连证书兼容链已校验并加载。"
