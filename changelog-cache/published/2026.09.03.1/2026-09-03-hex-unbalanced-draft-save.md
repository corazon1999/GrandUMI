# 允许保存并发布非等量的海克斯品质配置

- 日期：2026-09-03
- 分类：修复
- 影响范围：管理员海克斯品质调配面板、海克斯配置草稿与发布校验
- 状态：已完成

## 玩家可见说明

- 调整海克斯品质时，可以把任意分配先保存为共享草稿，不再要求三个常规池数量相等。
- 只要银色、金色、棱彩三个常规池均至少保留 1 个海克斯，非等量草稿也可以发布生效；空池仍会被明确阻止。

## 技术说明

- 将完整目录的草稿校验与激活配置校验分离：草稿允许任意品质数量，发布请求入队前与受限执行器激活时统一校验三个常规池均非空。
- 已保存的非等量草稿可在服务重启后正常读取；空池草稿不能绕过服务端与执行器校验进入激活状态。

## 验证结果

- `node --test opcgpro-web/tests/admin-hex-catalog.test.mjs`：7/7 通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~AdminDeploymentCoordinatorTests`：8/8 通过。
- `git diff --check`：通过。
