# 卡牌效果发动特效

- 日期：2026-08-08
- 分类：新增
- 影响范围：对战牌桌、观战与回放中的卡牌效果提示
- 状态：已完成

## 玩家可见说明

- 卡牌发动效果时会播放醒目的金色高亮并标注触发时机；事件卡、生命触发卡或已经离场的来源卡会短暂展示卡图，连续发动的多张卡会依次提示。
- 效果提示不会阻挡牌桌操作，并会遵循系统的“减少动态效果”设置。

## 技术说明

- 服务端在实际声明了对应触发时机的卡牌进入效果解析后，按顺序暂存来源卡、控制方和触发类型，并随下一份视角化状态快照下发。
- 含效果发动事件的状态快照被标记为不可合并，避免慢连接下的一次性动画事件被后续普通状态覆盖。
- 客户端使用独立本地队列顺序播放：场上来源按实例 ID 定位高亮，无法定位的公开来源使用卡图切入，并统一播放现有效果音效。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName~EffectActivationSnapshotTests|FullyQualifiedName~LatencyOptimizationTests.含效果发动事件的状态快照_不可被后续普通快照合并"`：4 项通过。
- `npm exec tsc -- --noEmit --incremental false`：通过。
- `npm exec next build`：Next.js 16.2.6 生产构建通过。
- 全量后端测试在并行写入的 OP17 卡牌数据出现后产生 8 项既有 OP17 数据断言失败；本任务定向测试不受影响，未修改或暂存该并行数据。
