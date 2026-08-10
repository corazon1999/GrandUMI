# 排位榜信息补全

- 日期：2026-08-11
- 分类：优化
- 影响范围：排行榜中的排位榜
- 状态：已完成

## 玩家可见说明

- 排位榜现在明确展示玩家昵称、段位、PT、阵营和最擅长的 Leader，方便快速了解上榜玩家的对局风格。

## 技术说明

- 排位榜协议新增最擅长 Leader 字段；服务端以账号哈希关联有效真人对局统计，按使用次数、胜率和 Leader 编号稳定选取结果。统计源暂不可用时保留其余排位榜数据。

## 验证结果

- `node --test tests/ranking-tabs.test.mjs tests/home-sidebar.test.mjs tests/leader-leaderboard-sort.test.mjs`：7 项通过。
- Next.js 生产构建通过（包含 TypeScript 检查）。
- 本机未安装 .NET SDK，新增 Leader 统计单测将由测试服服务端构建验证。
