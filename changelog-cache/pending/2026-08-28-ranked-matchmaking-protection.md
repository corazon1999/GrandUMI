# 排位匹配扩圈与定级保护

- 日期：2026-08-28
- 分类：优化
- 影响范围：标准排位、狂野排位匹配
- 状态：已完成

## 玩家可见说明

- 排位匹配现在会按双方共同等待的时间逐步扩大分差范围，不再因为一名玩家等待较久，就让刚入队的玩家立刻遇到跨度过大的对手。
- 定级中的玩家在匹配高悬赏成熟玩家时会获得额外保护；低在线时双方共同等待满 90 秒后仍可在有限分差内完成匹配。

## 技术说明

- 扩圈依据由双方等待时间的最大值改为最小值，继续采用 100 / 175 / 275 / 400 的前四档隐藏分范围，并将 90 秒后的范围固定为 500，移除无限扩圈。
- 入队时原子读取并固化当前赛季隐藏分、定级局数、可见 RP 与阵营；定级未满 5 局的玩家对阵已完成定级且可见 RP 不低于 1500 的玩家时，双方共同等待未满 90 秒禁止匹配。
- 双方共同等待满 90 秒后，上述定级保护允许在隐藏分差不超过 500 时兜底匹配。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~RankedMatchmakingPolicyTests|FullyQualifiedName~MatchmakingIdentityTests|FullyQualifiedName~RankedStoreTests" --no-restore`：67 项全部通过。
- 通过 `ops/windows/GrandUmiTemp.ps1` 设置 E 盘测试临时目录后运行 `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：1551 项全部通过。
