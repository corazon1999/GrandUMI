# 测试服缺失卡图遵从源站禁止缓存

- 日期：2026-10-12
- 分类：修复
- 影响范围：测试服卡图的 Cloudflare 边缘缓存与浏览器缓存
- 状态：已完成（仅测试服）

## 玩家可见说明

- 测试服缺失卡图不再被缓存为长时间的错误结果，正常卡图继续缓存以加快加载。

## 技术说明

- 原有卡图规则忽略源站缓存控制头并设置 30 天边缘 TTL，导致 404 响应被缓存；浏览器响应也被覆盖为 4 小时。
- 在现有规则之后新增“GrandUMI 测试服卡图遵从源站缓存”，保留符合缓存条件，边缘 TTL 选择“使用缓存控制标头（如果存在），否则绕过缓存”，浏览器 TTL 选择“接受源服务器 TTL”。
- 匹配表达式如下，仅覆盖测试服三个卡图目录：

```text
(http.host eq "test.grand-umi.com" and (starts_with(http.request.uri.path, "/cards/") or starts_with(http.request.uri.path, "/cards-thumb/") or starts_with(http.request.uri.path, "/cards-webp/")))
```

- 规则通过 Cloudflare 管理页面部署，显示顺序 3、活动；未修改原有两条规则及全局缓存设置。
- 只精确清除了验收 URL `https://test.grand-umi.com/cards/lag-fix-missing-bb93b061b.png` 的旧 404 缓存，没有执行全站清除。
- 本记录仅确认测试服配置和验收结果，不代表正式服已应用本项修复。客户端已保存的旧响应不受 CDN 清除操作影响；此前卡图兜底修复使用新查询版本避开旧缓存。

## 验证结果

- 三个目录中新命名的缺失图片各连续请求两次，均为 HTTP 404，保留 `private, no-store, no-cache, max-age=0, must-revalidate`，Cloudflare 状态为 `BYPASS`。
- 已缓存的验收 404 在精确清除后连续请求两次，均为 `BYPASS`，不再返回 `HIT`。
- OP17-099 的旧 PNG、高清 WebP 和缩略 WebP 链接均返回 HTTP 200、`image/webp`；正常资源保留 `public, max-age=31536000, immutable`，重复请求为 `HIT`。
- 测试服首页跟随重定向后 HTTP 200，`/backend/ready` 返回 `ready`，未处于维护或过载状态。
