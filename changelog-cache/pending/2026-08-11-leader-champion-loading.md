# 修复 Leader 最强使用者加载

- 日期：2026-08-11
- 分类：修复
- 影响范围：排行榜、Leader 最强使用者称号、测试服排行榜数据
- 状态：已完成

## 玩家可见说明

- 排行榜现在会为每个 Leader 正确加载近 30 日符合场次门槛的最强使用者，不再因统计索引错误而统一显示“最强使用者待诞生”。
- 测试服的 Leader 总场次与最强使用者改为读取同一份排行榜数据；确实没有玩家达到个人场次门槛时，才会继续显示“待诞生”。

## 技术说明

- 修正称号聚合结果中 Leader 编号与玩家统计键的字段映射顺序。
- 为称号存储增加独立的写入数据库和排行榜读取数据库，保持测试服与正式排行榜的数据口径一致，同时避免测试对局写入正式统计库。
- 排行榜日志增量回填后同步初始化称号事实表，保证历史有效对局可参与近 30 日称号计算。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --filter FullyQualifiedName~LeaderChampionStoreTests --nologo`：6 项通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --nologo`：821 项通过。
