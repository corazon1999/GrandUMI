# 修复正式服紧急 A/B 发布入口

- 日期：2026-09-02
- 分类：修复
- 影响范围：正式服紧急发布、A/B 槽位切换、共享账号与发布安全门禁
- 状态：已完成

## 玩家可见说明

- 修复正式服紧急更新入口失效的问题；紧急更新会使用可回滚的 A/B 槽位流程，并在发布前后完整核验版本与服务状态。
- 明确授权紧急更新时可以不等待在线房间清空，但仍会保留测试验证、数据快照、共享账号和版本一致性保护。

## 技术说明

- Windows 入口不再调用服务器上已经淘汰的 `/opt/grandumi/deploy.sh`，改为从目标提交提取版本化 `ops/server/deploy-grandumi-production-emergency.sh`。
- 远端使用 detached worktree 执行 `bootstrap → stage → activate`，不 checkout、reset 或覆盖 `/opt/grandumi` 的历史产物与现有脏工作树。
- 构建前与切流前都会复核 GitHub `main`、服务器目标 ref、测试服 `test-deployed` 与完整验证证明、更新日志归档、正式版本祖先关系、A/B 槽位、恢复写队列、共享账号权威和管理面板发布竞争状态。
- 切槽继续由现有一致性快照与自动回滚流程承担；重复请求会在目标版本与双槽均已收敛时幂等成功，状态互相矛盾时拒绝继续。

## 验证结果

- `verify.ps1 -InfrastructureOnly`：2 个基础设施套件通过，其中部署证明与发布门禁共 9 项通过。
- `node --test tools/deploy-verification-gate.test.mjs`：7/7 通过。
- `node --test opcgpro-web/tests/new-production-deploy.test.mjs`：18/18 通过。
- `node --test opcgpro-web/tests/cdn-asset-routing.test.mjs opcgpro-web/tests/ws-endpoint-fallback.test.mjs`：15/15 通过。
- PowerShell AST 解析 `deploy-hk.ps1` 无语法错误，文件仍为 UTF-8 with BOM；远端 Bash `-n` 检查通过。
- 在 103.146.230.37 上以 `--preflight` 只读验证当前目标 `e5839cbe321c9d8ae0fb6b21137521273780ad68` 通过；使用过期目标 `df6e403d23deb01cd616cae2452571d6596a4d30` 时按预期因不是最新 `main` 被拒绝，全程未构建、切槽或部署。
