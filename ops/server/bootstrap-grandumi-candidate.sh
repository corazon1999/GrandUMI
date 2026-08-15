#!/usr/bin/env bash
set -Eeuo pipefail

candidate_ip="${GRANDUMI_CANDIDATE_IP:-103.146.230.37}"
[[ "$candidate_ip" == "103.146.230.37" ]] || { echo "拒绝在未登记主机上初始化：$candidate_ip" >&2; exit 1; }

export DEBIAN_FRONTEND=noninteractive
apt-get -o DPkg::Lock::Timeout=300 update
apt-get -o DPkg::Lock::Timeout=300 install -y --no-install-recommends ca-certificates curl git nginx rsync xz-utils iproute2 kmod sqlite3 jq xfsprogs

if [[ ! -x /opt/dotnet/dotnet ]]; then
  installer="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  bash "$installer" --channel 10.0 --install-dir /opt/dotnet
  rm -f "$installer"
fi
ln -sfn /opt/dotnet/dotnet /usr/local/bin/dotnet

if [[ ! -x /opt/node/bin/node ]]; then
  node_index="$(curl -fsSL https://nodejs.org/dist/latest-v22.x/SHASUMS256.txt)"
  node_archive="$(awk '$2 ~ /^node-v[0-9.]+-linux-x64.tar.xz$/ {print $2; exit}' <<<"$node_index")"
  [[ -n "$node_archive" ]] || { echo "无法解析 Node.js 22 下载版本" >&2; exit 1; }
  node_version="${node_archive%-linux-x64.tar.xz}"
  node_temp="$(mktemp -d)"
  curl -fsSL "https://nodejs.org/dist/latest-v22.x/$node_archive" -o "$node_temp/$node_archive"
  grep " $node_archive$" <<<"$node_index" | (cd "$node_temp" && sha256sum -c -)
  tar -xJf "$node_temp/$node_archive" -C "$node_temp"
  rm -rf /opt/node
  mv "$node_temp/$node_version-linux-x64" /opt/node
  rm -rf "$node_temp"
fi
ln -sfn /opt/node/bin/node /usr/local/bin/node
ln -sfn /opt/node/bin/npm /usr/local/bin/npm
ln -sfn /opt/node/bin/npx /usr/local/bin/npx

id grandumi >/dev/null 2>&1 || useradd --system --home /nonexistent --shell /usr/sbin/nologin grandumi
install -d -o grandumi -g grandumi -m 0750 /data/grandumi
install -d -m 0755 /opt/grandumi-candidate
if ! swapon --show=NAME --noheadings | grep -Fxq /data/grandumi.swap; then
  if [[ ! -f /data/grandumi.swap ]]; then
    fallocate -l 2G /data/grandumi.swap
    chmod 0600 /data/grandumi.swap
    mkswap /data/grandumi.swap >/dev/null
  fi
  swapon /data/grandumi.swap
fi
grep -Fq '/data/grandumi.swap none swap sw 0 0' /etc/fstab \
  || printf '/data/grandumi.swap none swap sw 0 0\n' >> /etc/fstab

install -m 0644 /opt/grandumi-candidate/ops/server/60-grandumi-network.conf /etc/sysctl.d/60-grandumi-network.conf
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-network-modules.conf /etc/modules-load.d/grandumi-network.conf
install -m 0755 /opt/grandumi-candidate/ops/server/apply-grandumi-network.sh /usr/local/sbin/apply-grandumi-network
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-network-tuning.service /etc/systemd/system/grandumi-network-tuning.service
install -m 0755 /opt/grandumi-candidate/ops/server/grandumi-network-monitor.sh /usr/local/sbin/grandumi-network-monitor
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-network-monitor.service /etc/systemd/system/grandumi-network-monitor.service
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-network-monitor.timer /etc/systemd/system/grandumi-network-monitor.timer
install -m 0755 /opt/grandumi-candidate/ops/server/grandumi-candidate-backup.sh /usr/local/sbin/grandumi-candidate-backup
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-candidate-backup.service /etc/systemd/system/grandumi-candidate-backup.service
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-candidate-backup.timer /etc/systemd/system/grandumi-candidate-backup.timer
mkdir -p /etc/systemd/system/grandumi-network-tuning.service.d
printf '[Service]\nEnvironment=GRANDUMI_EGRESS_RATE=60mbit\n' > /etc/systemd/system/grandumi-network-tuning.service.d/candidate.conf
sysctl --system >/dev/null

install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-candidate-backend.service /etc/systemd/system/grandumi-candidate-backend.service
install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-candidate-frontend.service /etc/systemd/system/grandumi-candidate-frontend.service
if [[ -f /etc/letsencrypt/live/candidate.grand-umi.com/fullchain.pem ]]; then
  install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-candidate-tls.nginx /etc/nginx/sites-available/grandumi-candidate
else
  install -m 0644 /opt/grandumi-candidate/ops/server/grandumi-candidate.nginx /etc/nginx/sites-available/grandumi-candidate
fi
ln -sfn /etc/nginx/sites-available/grandumi-candidate /etc/nginx/sites-enabled/grandumi-candidate
rm -f /etc/nginx/sites-enabled/default
sed -ri 's/worker_connections\s+[0-9]+;/worker_connections 8192;/' /etc/nginx/nginx.conf
nginx -t
systemctl daemon-reload
systemctl enable nginx grandumi-network-tuning.service grandumi-network-monitor.timer grandumi-candidate-backup.timer grandumi-candidate-backend.service grandumi-candidate-frontend.service
systemctl restart grandumi-network-tuning.service
systemctl restart grandumi-network-monitor.timer grandumi-candidate-backup.timer
systemctl restart nginx

echo "候选服基础环境初始化完成：IP=$candidate_ip，dotnet=$(dotnet --version)，node=$(node --version)"
