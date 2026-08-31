# 训练对局历史工件独立进程重放

- 日期：2026-08-29
- 分类：优化
- 影响范围：AI 真人对练训练数据重放、测试服归档审计
- 状态：已完成

## 玩家可见说明

- 强化后续真人对练 AI 的训练数据校验：测试服会用生成该局日志时对应的历史后端版本重新执行整局，只有动作、状态稳定点、随机轨迹和终局全部一致的对局才会记录为重放验证通过。
- 单局分歧、超时或 worker 异常会完整隔离该局，不会输出部分可信样本；本次不改变线上对局规则，也不代表测试候选已经具备正式训练或生产发布资格。

## 技术说明

- 新版不可变归档冻结历史 `GrandUMIServer.dll` 的固定 worker 入口；controller 按完整 artifact 指纹启动一次性独立进程，不接受日志或 manifest 自由文本参与可执行文件、参数和工作目录选择，也不回退当前 `main`。
- 进程协议采用有界长度前缀与规范 UTF-8 JSON，严格执行 `hello → 单请求 → 单响应 → EOF`；请求、checkpoint contract、动作 lineage、响应与 replay digest 均重新计算稳定哈希，额外帧、截断、非法编码、未知字段、身份不符或响应篡改全部 fail closed。
- 为 probe、稳定等待、整局 wall-clock、请求／响应／stderr 大小和批量并发设置上限；超时及取消会终止整棵子进程树并有界等待，终止失败会中止批次。审计期间日志集合或字节变化同样拒绝生成跨快照报告。
- 覆盖审计仅在历史 DLL 握手、完整重放、逐 checkpoint／终局及返回 lineage 全部验证后写入 `replay_verified`，并单独区分分歧、超时和 worker 失败；系统性协议错误不会留下部分输出。
- 历史 worker 禁用排行榜／称号等外部画像查询，并在响应前复核归档未变化，避免重放过程向不可变归档工作目录写入数据库。旧 v1 归档继续只读可取回且保持 worker unavailable，生产 artifact registry 保持为空。
- 当前仅实现应用层进程隔离、环境清理、权限降级尝试和资源边界，尚无 OS 级网络／文件系统沙箱或 CPU／内存硬隔离，因此候选继续仅限测试服且不具备生产 registry 资格。

## 验证结果

- `dotnet build 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：成功，0 警告、0 错误。
- 进程协议、不可变归档、覆盖审计、artifact worker 与生产 checkpoint 定向测试：74/74 通过；其中协议异常矩阵覆盖额外帧、超长／截断、非法 UTF-8／JSON、stderr 噪声／超限、非零退出、请求超限、响应哈希篡改，以及超时／取消后的子进程树清理。
- Release 真实链路完成 `publish → capture → 历史 DLL verify/handshake → 独立进程完整重放 → checkpoint 分歧隔离`，重复审计报告字节与哈希稳定；同时验证错误请求哈希和 artifact 身份均被拒绝。
- 通过 `ops/windows/GrandUmiTemp.ps1` 使用并清理 E 盘专用验证目录后，服务端完整测试 1643/1643 通过，0 失败、0 跳过。
