# 训练对局工件隔离重放门禁

- 日期：2026-08-29
- 分类：优化
- 影响范围：AI 真人对练训练数据重放基础设施
- 状态：已完成

## 玩家可见说明

- 为后续真人对练 AI 的训练数据增加了更严格的对局重放校验基础：动作被拒绝、重放超时或状态、随机结果、终局不一致时会整局隔离，避免把部分可信的对局误用于训练。
- 本次只完善内部训练基础设施，不改变当前线上对局规则，也不代表历史对局已经可用于正式训练。

## 技术说明

- 新增可序列化、可替换为独立进程代理的 artifact replay worker 边界，按不可变 descriptor 完整指纹精确路由，不回退到当前 `main`；进程内实现同时锁定对应规则集 ID。
- dispatcher 会复算成功／失败响应的规范哈希；成功响应逐项绑定请求中的完整 lineage，失败响应校验稳定分类及 source/action 定位范围，阻止传输损坏或错误 worker 用正确外层字段夹带篡改结果。
- 将 `PreparedReplayMatch.MaterializeActionEntries()` 接入 `MatchReplay` 的严格旁路入口，在开局、每条 accepted／系统动作后的稳定点和终局调用 artifact 自带的 `IReplayCheckpointProvider`，分别核对完整状态、公开状态、累计随机轨迹和终局语义。
- 新增显式 `grandumi.replay_checkpoint.v1` 期望契约、完整 lineage 与规范 SHA-256；缺失或部分契约、动作拒绝、效果链异常、稳定等待超时、整局 worker 超时、取消及任一 digest 分歧都只返回整局 quarantine，不输出部分结果。
- 生产 artifact registry 保持为空；当前仅以合成／当前版本 fixture provider 证明执行路径，生产 digest provider、历史二进制归档与独立进程 executable 仍是 Gate B 的 No-Go 项。

## 验证结果

- `dotnet build 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：成功，0 警告、0 错误。
- artifact worker 定向测试：32/32 通过，覆盖成功重放、动作拒绝、稳定等待与整局超时、取消、full/public/random/terminal 分歧、工件不匹配、全有或全无、稳定哈希，以及成功／失败响应哈希、12 项 verified lineage 与失败定位范围篡改。
- Training + Replay 定向测试：56/56 通过。
- Replay／Recovery／StartingPlayer／Mulligan／DrawAgreement 相关回归：75/75 通过。
- 通过 `ops/windows/GrandUmiTemp.ps1` 设置 E 盘测试根后运行完整服务端测试：1564/1564 通过，0 跳过。
