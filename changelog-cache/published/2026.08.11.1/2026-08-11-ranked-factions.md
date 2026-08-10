# 排位阵营与称号体系

- 日期：2026-08-11
- 分类：新增
- 影响范围：排位匹配、大厅段位信息、赛后结算、排位排行榜
- 状态：已完成

## 玩家可见说明

- 开始首次排位前可从海贼、海军、世界政府中选择一个阵营；选择后永久锁定，不影响匹配、RP 或隐藏分。
- 段位称号会随阵营显示：海贼从见习海贼到船长，海军从海军三等兵到海军中将，世界政府从政府线人到神之骑士团。
- 1500 RP 起为新世界段位；各阵营榜首及前列玩家可获得海贼王、四皇、海军元帅、海军大将、世界之王或五老星称号。

## 技术说明

- 排位 SQLite 新增按账号哈希永久保存的阵营选择表，服务端拒绝未选阵营或尝试改选阵营的排位请求。
- 排行榜按阵营独立计算新世界特殊称号，同时保留全局 RP 排序；额外包含各阵营前六名以保证特殊称号可见。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-build --filter FullyQualifiedName~RankedStoreTests`：通过 10，失败 0。
- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests\ranked-match-clock.test.mjs`：通过 5，失败 0。
- `node node_modules\next\dist\bin\next build`：通过，含 TypeScript 检查。
- 全量服务端测试在本机因既有 `memory_pressure` 房间创建限制失败 8 项，其余 753 项通过；失败项均不涉及排位阵营逻辑。
