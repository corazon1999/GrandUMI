# 增加 WebSocket 直连备用线路

- 日期：2026-08-11
- 分类：优化
- 影响范围：正式服登录、断线重连与 WebSocket 网络连接
- 状态：已完成

## 玩家可见说明

- 正式服主连接线路不可用或握手超时时，客户端会自动切换到备用直连线路，减少因单条线路异常导致的登录和重连失败。

## 技术说明

- 统一首次连接与登录重试的 WebSocket 端点选择逻辑，并支持按顺序轮换多个端点。
- 为连接握手增加 5 秒超时保护，超时后关闭旧连接并切换下一条线路。
- 新增 `direct.grand-umi.com` 的反向代理配置，作为正式服 WebSocket 与前端直连入口。

## 验证结果

- `node --test tests\ws-endpoint-fallback.test.mjs`：通过 3，失败 0。
- `npm run build`：通过，Next.js 生产构建与 TypeScript 检查成功。
