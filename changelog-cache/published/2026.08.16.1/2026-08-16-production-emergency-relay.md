# 正式服低延迟应急中转

- 日期：2026-08-16
- 分类：优化
- 影响范围：正式服 WebSocket 直连入口

## 玩家可见说明

在新正式服直连证书尚无法配置期间，恢复低延迟对局入口，显著减少操作等待时间；所有对局仍由同一正式服节点处理。

## 技术说明

旧香港节点仅作为 `direct.grand-umi.com` 的 TLS 与 WebSocket 临时中转，通过固定新正式服 IP 和 `grand-umi.com` TLS SNI 转发全部请求。旧节点本地正式后端不接收中转流量，避免数据库和房间状态分裂。启用脚本在替换 Caddy 站点前保留原配置，并在验证或重载失败时自动回滚。

## 验证结果

- Cloudflare 权威 DNS 与 Google DNS 均解析到旧香港中转节点 `8.210.155.25`。
- `https://direct.grand-umi.com/backend/version` 返回新正式服节点 `hk-production-b`。
- 10 次 WebSocket 协议握手全部成功，稳定耗时约 82–89ms，平均约 99ms。
- 旧节点本地正式后端保持 0 连接、0 房间，确认未发生玩家分流。
- Caddy 全量配置验证、无中断重载和外网 TLS 健康检查通过。
