# 修复紧急发布构建环境契约测试

- 日期：2026-09-02
- 分类：修复
- 影响范围：前端完整测试、正式服紧急 A/B 发布、客户端诊断版本号
- 状态：已完成

## 玩家可见说明

- 修复正式服紧急发布验证仍按旧入口检查的问题，确保 A/B 发布构建继续把准确的版本号写入客户端诊断。
- 发布前自动检查现在会沿真实发布链验证版本注入，避免正确的新发布入口被旧契约误判为失败。

## 技术说明

- `feedback-evidence.test.mjs` 不再要求 Windows 编排入口 `deploy-hk.ps1` 直接声明 `NEXT_PUBLIC_GRANDUMI_COMMIT`。
- 新契约分别验证 `deploy-hk.ps1` 调用版本化 `deploy-grandumi-production-emergency.sh`、远端紧急脚本调用 `stage-grandumi-production.sh`，以及 stage 使用目标提交设置 `NEXT_PUBLIC_GRANDUMI_COMMIT="$target"`。
- 测试服、候选服、正常正式提升等直接构建入口仍继续逐项断言提交号环境变量，未削弱客户端诊断版本门禁。

## 验证结果

- `node --test opcgpro-web/tests/feedback-evidence.test.mjs`：2/2 通过。
- 前端完整单元测试：484/484 通过，0 失败、0 跳过。
- `npm run build --prefix opcgpro-web`：Next.js 生产构建与 TypeScript 检查通过。
