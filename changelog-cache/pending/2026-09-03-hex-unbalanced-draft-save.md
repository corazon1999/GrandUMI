# 允许保存未平衡的海克斯品质草稿

- 日期：2026-09-03
- 分类：修复
- 影响范围：管理员海克斯品质调配面板、海克斯配置草稿与发布校验
- 状态：已完成

## 玩家可见说明

- 调整海克斯品质时，即使银色、金色、棱彩三个常规池暂未各达到 18 个，也可以先保存为共享草稿。
- 面板会明确提示未平衡草稿可以保存，但在三个常规池恢复为各 18 个前不能发布生效。

## 技术说明

- 将完整目录的草稿校验与激活配置校验分离：草稿允许品质数量暂时不平衡，发布请求入队前与受限执行器激活时仍严格校验 18/18/18。
- 已保存的不平衡草稿可在服务重启后正常读取，且不能绕过服务端校验进入发布队列。

## 验证结果

- `node --test opcgpro-web/tests/admin-hex-catalog.test.mjs`：7/7 通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~AdminDeploymentCoordinatorTests`：8/8 通过。
- `git diff --check`：通过。
