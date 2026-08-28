# 正式预构建 DNS Answer 区门禁修复

- 日期：2026-08-28
- 分类：修复
- 影响范围：新香港正式服 Windows 预构建入口的直连域名安全检查
- 状态：已完成

## 玩家可见说明

- 修复正式发布预构建在 DNS 响应同时带有附加区地址时被误阻止的问题；发布仍只接受 `direct.grand-umi.com` 在权威回答区唯一指向登记的新香港服务器，并继续执行 TLS 与健康检查。

## 技术说明

- `deploy-new-hk-production.ps1` 的 IPv4 解析结果现在同时限定 DNS `Answer` 区、精确查询域名、A 记录和唯一 IP，不再把 Additional 区中的 DNS 服务商胶水记录当作直连域名地址。
- 安全门禁没有增加跳过参数或解析覆盖入口；目标 IP、TLS/SNI 与后端健康检查约束保持不变。

## 验证结果

- PowerShell 脚本语法检查通过；构造 `Answer` 与 `Additional` 混合记录验证只保留 `103.146.230.37`，实时查询也只得到该 Answer 地址。
- 正式发布、主域切换、CDN 与 WebSocket 线路定向回归：39 项全部通过。
- 前端 113 个 `tests/*.test.mjs` 文件、`npx tsc --noEmit` 与 Next.js 生产构建全部通过。
