# GrandUMI 正式服主域名切换到 ygo.grand-umi.com

- 编制日期：2026-08-26
- 目标主入口：`https://ygo.grand-umi.com`
- 保持不变：`direct.grand-umi.com`、`assets.grand-umi.com`、正式服源站 `103.146.230.37`
- 旧入口：切换后 `grand-umi.com` 的 HTTP 与 HTTPS 普通请求均返回 403

## 1. 不变量与当前阻塞

1. `direct.grand-umi.com` 始终是正式 WebSocket 首选，`wss://ygo.grand-umi.com/ws` 只作 Cloudflare 兜底。
2. `assets.grand-umi.com` 不改 DNS、不改证书、不改路由。
3. 域名切换期间只允许一套正式数据源；不得启动旧正式服或另一套可写后端。
4. `bootstrap-grandumi-production.sh` 在没有持久模式文件时固定使用 `legacy`，普通预构建不会让旧域提前返回 403。
5. `switch-grandumi-primary-domain.sh` 只在所有正式后端 unit 均为 inactive、8080/8082 均无监听时运行；否则立即拒绝。
6. 2026-08-26 公网只读核验：`ygo.grand-umi.com` 为 NXDOMAIN；Cloudflare 控制台连接器、管理 CLI和可用浏览器会话均不存在，因此 DNS 必须由管理员手工配置。
7. 应急直连中转与主域切换共用互斥锁，并按 `/etc/grandumi/primary-domain-mode` 选择 legacy 或 ygo 上游；覆盖 Caddy 前必须通过固定源站 IP 的严格 TLS 和 `/backend/ready` 200 验证。

## 2. Cloudflare 最短手工步骤

在 `grand-umi.com` 区域中执行：

1. 打开 DNS 记录，新增 `A` 记录：名称 `ygo`，IPv4 `103.146.230.37`，代理状态“已代理”，TTL“自动”。
2. 不修改根记录 `grand-umi.com`，不修改 `direct` 和 `assets`。
3. 核对 SSL/TLS 模式已经是 **Full (strict)**；若当前不是，不要临场直接更改整个区域，先查清现有源站证书策略。
4. 核对没有把 `grand-umi.com` 重定向到新域的 Redirect Rule/Page Rule；需求是旧域返回拒绝访问，不是跳转。
5. 等待公共 DNS 能查到 `ygo.grand-umi.com` 后，再执行证书准备脚本。

不得索取、复制或粘贴 Cloudflare Cookie、密码、API Token。若 DNS 无法在 06:00 前完成，停止切换并继续保留 legacy 模式。

## 3. 阶段一：证书准备，仍不切流

先按正式发布流程把包含本文件对应脚本的已验证提交预置到正式服，但不得运行域名切换命令。`bootstrap` 会继续加载 legacy 主站。

在新正式服执行：

```bash
sudo /usr/local/sbin/prepare-grandumi-ygo-tls
```

该命令会：

- 为 `ygo` 加载独立隔离站点；签证前使用旧证书占住 SNI，签证后使用 ygo 证书；两种状态都只返回 503。
- 用随机 HTTP-01 文件核对公开 DNS/Cloudflare 确实回到本机，然后才调用 Certbot。
- 校验证书主机名并启用自动续期。

成功标准：脚本明确输出“证书已就绪，预切入口保持 HTTP 503”；此时旧主域、直连域、资产域仍保持原服务。失败时不要绕过 HTTP-01 或证书校验，先修正 DNS/Cloudflare。

## 4. 阶段二：06:00 停机后的显式切换

先完成维护排空和持久化快照，再停止所有正式后端实例。若 06:00 的“强制关机”是整机断电，重新开机后必须先阻止后端自动启动，再执行以下检查。

```bash
systemctl stop \
  grandumi-production-backend.service \
  grandumi-production-backend@a.service \
  grandumi-production-backend@b.service

systemctl is-active \
  grandumi-production-backend.service \
  grandumi-production-backend@a.service \
  grandumi-production-backend@b.service

ss -Hlnpt | grep -E ':(8080|8082)([[:space:]]|$)' || true
```

三个 unit 都必须不是 `active`，端口检查必须无输出。然后执行：

```bash
sudo /usr/local/sbin/switch-grandumi-primary-domain cutover
```

脚本使用独占锁并依次完成：备份当前 Nginx/模式/双槽线路清单，写入双槽 ygo 线路，持久化 ygo 模式，替换 Nginx 配置，严格校验证书，再验证旧域 403、新域已进入正式站点、直连域 TLS 可用。任一步失败会恢复执行前状态，恢复证据保存在 `/var/lib/grandumi-domain-cutover/<时间戳>/`。

脚本成功后，再启动当前活动槽服务：

```bash
active_slot="$(cat /var/lib/grandumi-ha/active-slot)"
systemctl start "grandumi-production-backend@$active_slot.service"
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused \
  "http://127.0.0.1:$([[ "$active_slot" == a ]] && echo 8080 || echo 8082)/ready"
systemctl start "grandumi-production-frontend@$active_slot.service"
```

## 5. 切换后验收

以下检查必须全部通过，任一失败都不得宣布完成：

```bash
curl -fsS https://ygo.grand-umi.com/backend/ready
curl -fsS https://ygo.grand-umi.com/backend/version
curl -fsS https://direct.grand-umi.com/backend/ready
curl -fsS https://assets.grand-umi.com/sprites-thumb/CardBack.webp -o /dev/null

test "$(curl -sS -o /dev/null -w '%{http_code}' https://grand-umi.com/)" = 403
test "$(curl -sS -o /dev/null -w '%{http_code}' http://grand-umi.com/)" = 403
```

浏览器还必须核对：

- `https://ygo.grand-umi.com` 首页、登录、刷新、无痕首次访问正常。
- 首选 WebSocket 为 `wss://direct.grand-umi.com/ws`；模拟直连失败后才使用 `wss://ygo.grand-umi.com/ws`。
- Next.js JS/CSS 从 ygo 同源加载；卡图仍从 `assets.grand-umi.com` 加载。
- 电脑端及 `390×844`、`360×780` 竖屏没有因换域出现登录或主要操作不可用。
- 若 Cloudflare 仍返回旧内容，只清理旧主域与新主域相关缓存；不要修改或清空 `direct/assets` 配置。

## 6. 回退

域名回退不涉及服务器或数据库回退，仍使用同一正式服和同一数据目录。先再次排空并停止所有正式后端，然后运行：

```bash
sudo /usr/local/sbin/switch-grandumi-primary-domain rollback
```

成功后旧主域恢复正式站点，新主域恢复 503 隔离；再启动活动槽并验证旧主域、直连域和资产域。最后可在 Cloudflare 删除或暂停 `ygo` A 记录。不要启动旧服务器，不要复制或替换玩家数据库。

若切换进程被超时、取消或终止：保持后端停止，先查看命令输出与 `/var/lib/grandumi-domain-cutover/`。普通失败会自动恢复；遭遇不可捕获的进程终止时，按原动作重跑即可，模式文件会让后续 bootstrap 收敛到同一目标。

## 7. 已完成的验证证据

- 隔离 worktree 的 Node 自动化回归：43/43 通过。
- `.NET` 构建：0 个警告、0 个错误。
- Git Bash `bash -n`：7/7 个相关 Shell 脚本通过。
- 应急中转模式选择调整后，其启用脚本再次单独通过 `bash -n`。
- 正式 Linux 主机上的隔离 `nginx -t`：3/3 份配置通过。验证只使用临时只读配置，没有安装、重载或替换正式 Nginx 配置。
- PowerShell 部署入口解析和运行时 JSON 解析通过。

以上证据证明仓库配置和脚本具备进入测试服验证的条件，不代表 Cloudflare DNS 已配置，也不代表正式域名已经切换。
