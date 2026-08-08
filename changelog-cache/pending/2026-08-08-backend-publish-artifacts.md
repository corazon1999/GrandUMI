# 修复后端发布产物被旧文件覆盖

- 日期：2026-08-08
- 分类：修复
- 影响范围：正式服与测试服后端发布、服务启动稳定性
- 状态：已完成

## 玩家可见说明

- 修复服务器更新后后端可能因依赖文件不完整而无法启动的问题，提升版本更新后的服务可用性。

## 技术说明

- 将 `服务端WebSocket/publish/` 明确加入 Git 忽略规则，并停止跟踪其中 5 个历史构建产物。
- 避免服务器切换提交时，由 Git 写入的旧版运行时配置和依赖清单因时间戳较新而覆盖本次发布产物。
- 正式服使用全新暂存目录重新生成完整后端发布包并完成原子切换，未改动玩家数据库。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --nologo`：677 项全部通过。
- 使用全新目录执行 `dotnet publish`，确认运行时配置包含 `Microsoft.AspNetCore.App`，依赖清单和发布目录包含 `Microsoft.Data.Sqlite`。
- 正式服后端恢复为 `active`，版本接口返回发布提交 `a91a318bd7a85fe59cee372e6a15890ea5ce72a5`。
