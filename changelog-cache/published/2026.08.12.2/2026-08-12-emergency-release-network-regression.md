# 2026-08-12 紧急发布网络配置回退修复

- 日期：2026-08-12
- 分类：修复
- 影响范围：正式服网页加载、更新日志刷新、WebSocket 连接稳定性

## 玩家可见说明

修复紧急发布后网页脚本未走独立静态资源线路、旧首页可能长期缓存的问题；正式服对局连接会优先选择低延迟香港直连线路，恢复网页资源与对局连接的分流，并让新版本和更新日志及时生效。

## 技术说明

为 `deploy-hk.ps1` 的旧紧急部署入口补齐正式服构建环境变量，确保 `NEXT_PUBLIC_ASSET_ORIGIN` 始终指向 `https://assets.grand-umi.com`；同时将首页服务端入口改为动态响应，避免版本相关 HTML 被共享缓存长期复用。正式服 WebSocket 调整为香港直连优先、Cloudflare 兜底。新增回归测试覆盖两种正式发布入口、首页缓存策略与连接端点顺序。

## 验证结果

- `node --test tests/cdn-asset-routing.test.mjs`：7/7 通过。
- `node --test tests/ws-endpoint-fallback.test.mjs`：3/3 通过。
- `npm run build`：通过，`/home` 标记为动态路由。
- PowerShell 语法解析：`deploy-hk.ps1` 通过。
- 正式服持续心跳对照：Cloudflare 主线路 P95 1372.2ms；香港直连线路 P95 14.0ms，因此将直连调整为首选。
