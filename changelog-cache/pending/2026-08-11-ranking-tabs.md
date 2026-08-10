# 排行榜双榜切换

- 日期：2026-08-11
- 分类：优化
- 影响范围：大厅排行榜页面与导航
- 状态：已完成

## 玩家可见说明

- 原“Leader 胜率榜”现更名为“排行榜”，可在同一页面切换查看 Leader 榜和排位榜。
- 排位榜会展示本赛季已完成定级玩家的名次、阵营、段位、RP 与战绩；手机竖屏使用紧凑列表，便于浏览。

## 技术说明

- 复用登录时同步的排位榜数据，在现有排行榜页面内添加榜单类型切换；Leader 榜原有的时间范围、搜索、排序与对阵一图流保持不变。

## 验证结果

- `node --test tests/ranking-tabs.test.mjs tests/home-sidebar.test.mjs tests/leader-leaderboard-sort.test.mjs`：7 项通过。
- Next.js 生产构建通过（包含 TypeScript 检查）。
