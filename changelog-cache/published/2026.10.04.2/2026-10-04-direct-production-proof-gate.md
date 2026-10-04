# 增加 Direct 正式发布完整证明门禁

- 日期：2026-10-04
- 分类：优化
- 影响范围：正式服 A/B 发布入口、完整验证证明、共享账号权威检查
- 状态：已完成

## 玩家可见说明

- 在明确授权跳过测试服时，正式服更新可改用本地完整验证证明直接发布；测试服状态不会因此被读取、修改或重启。
- Direct 发布继续执行维护排空、旧对局日志、共享账号权威、快照、A/B 切槽和失败回退检查。

## 技术说明

- 新增显式 `-Drained -Direct -ProofPath` 入口。本地证明必须绑定目标 commit、Git tree、策略摘要、文件 SHA-256 和 payload 摘要，并包含九种不同的完整验证类别；部分证明、重复套件、错提交、错 tree、错策略及篡改内容均失败关闭。
- Windows 入口在推送前验证证明，再上传至 root 持有的受控 `/run` 路径；服务器在构建前和切槽前使用目标提交内的验证器复核同一证明。
- Direct 激活只允许共享账号 `active` 已生效且现有正式 A/B 槽健康的环境。旧后端停写后再次检查权威状态，只运行 `verify-target`，不执行首次迁移、测试服校验或测试服激活。
- 默认发布流程继续要求测试服同提交部署和验证证明；正式服主机、main、更新日志归档、祖先关系、并发锁、三阶段排空、快照与自动回退门禁保持有效。

## 验证结果

- `node --test tools/verification-proof.test.mjs tools/deploy-verification-gate.test.mjs`：18/18 通过，包含真实 Git fixture、真实证明验证器、九类命令唯一性、篡改拒绝及停写后 active 丢失回归。
- 四个相关 Bash 脚本通过 `bash -n`；`deploy-hk.ps1` 通过 PowerShell AST 解析并确认保留 UTF-8 BOM。
- `git diff --check` 通过。
