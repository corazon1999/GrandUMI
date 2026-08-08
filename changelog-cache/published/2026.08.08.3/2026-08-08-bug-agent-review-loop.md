# 防止 Bug Agent 重复处理同一反馈

- 日期：2026-08-08
- 分类：优化
- 影响范围：QQ Bug 反馈自动分诊、修复与独立复核流程
- 状态：已完成

## 玩家可见说明

- 反馈的自动修复未通过独立复核时，会先按复核意见原地修订；仍无法通过时会明确转人工，不再重复询问并从头处理同一条反馈。

## 技术说明

- 独立复核失败后保留当前 Git worktree，最多执行一次带复核上下文的有界修订；达到上限后写入 `manual` 终态。
- 管理员已经回答但任务仍无法确定或修复时，不再创建新的确认问题。
- Windows 前端构建门禁显式调用 `npm.cmd`，避免 PowerShell 执行策略拦截 `npm.ps1`；本次新增的 `*.test.mjs` 由可信工作器自动加入必跑测试。

## 验证结果

- `py -m unittest discover -s qq-bug-bot/tests -p "test_*.py"`：28 项测试全部通过。
- `py -m py_compile qq-bug-bot/agent_worker.py qq-bug-bot/agent_protocol.py`：通过。
- `git diff --check`：通过。
