# 正式服主域名切换准备

- 日期：2026-08-26
- 分类：优化
- 影响范围：正式服网页入口、WebSocket 备用线路、静态图片回退与发布运维
- 状态：已完成

## 玩家可见说明

- 正式服主入口更新为 `ygo.grand-umi.com`，对局仍优先使用原有低延迟直连线路，卡图资源域名保持不变。
- 域名切换后旧入口会明确拒绝访问，避免玩家误入已停用地址。

## 技术说明

- 正式构建和运行时线路清单改用 `ygo.grand-umi.com` 作为 Cloudflare WebSocket 兜底，保留 `direct.grand-umi.com` 首选及旧页面的短期连接兼容。
- 新增证书预备隔离站点：签发前后均只返回 503，不会因为预先添加 DNS 而提前开放新入口。
- 新增显式停机切换与回退脚本；脚本要求所有正式后端实例和端口均已停止，使用互斥锁，原子更新 A/B 两槽线路与持久模式，并在 Nginx、TLS 或状态码验证失败时恢复执行前配置。
- `bootstrap` 默认保持 legacy 模式，只有切换脚本成功写入模式后才会在后续预构建中继续加载新主域配置，避免普通预构建提前令旧域返回 403。
- 应急直连中转不再硬编码新主域；启用时与主域切换共用互斥锁，按持久模式安全选择 legacy 或 ygo 上游，并在覆盖 Caddy 前严格验证所选源站 TLS 与 `/backend/ready`。

## 验证结果

- `node --test tests/ws-endpoint-fallback.test.mjs tests/card-image-network-fallback.test.mjs tests/new-production-deploy.test.mjs tests/cdn-asset-routing.test.mjs tests/admin-deployment.test.mjs`：38 项全部通过。
- `node --test tests/game-layout.test.mjs`：5 项全部通过，覆盖手机竖屏及旋转横屏画布的既有布局不变量。
- 主代理在隔离 worktree 完成全部 43 项 Node 回归，结果 43/43 通过。
- `dotnet build`：0 个警告、0 个错误。
- Git Bash `bash -n`：7 个相关 Shell 脚本全部通过。
- 应急中转模式选择修改后，单独再次执行 `bash -n ops/server/enable-grandumi-emergency-direct-relay.sh`，结果通过。
- 正式 Linux 主机使用临时只读配置执行隔离 `nginx -t`：3 份 Nginx 配置全部通过；未安装、重载或替换任何正式配置。
- PowerShell 解析器验证 `deploy-hk.ps1` 无语法错误；`network-endpoints.json` 通过 JSON 解析。
- 公网只读审计确认 `ygo.grand-umi.com` 当前尚无 DNS 记录，`direct.grand-umi.com` 与 `assets.grand-umi.com` 保持原状态；未执行 Cloudflare、正式服、Git 或部署写入。
