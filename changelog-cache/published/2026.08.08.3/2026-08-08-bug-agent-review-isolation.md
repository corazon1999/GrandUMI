# 隔离 Bug Agent 独立复核工作区

- 日期：2026-08-08
- 分类：优化
- 影响范围：QQ Bug 反馈独立复核与自动修订流程
- 状态：已完成

## 玩家可见说明

- 自动修复进入独立复核后，复核过程不再可能覆盖已完成的修复；即使复核发生异常修改，也会保留原修复并进入有界修订。

## 技术说明

- 每次独立复核都从精确基线创建 detached Git worktree，将待审二进制 diff 复制到隔离副本后再运行复核和固定测试。
- 复核完成后强制清理隔离副本；如果复核 Agent 修改了副本，将其转换为可修订的复核失败，丢弃副本改动并保护原工作区。
- 隔离逻辑同时支持首轮未提交 diff 和变基后的已提交 diff。

## 验证结果

- `py -m unittest discover -s qq-bug-bot/tests -p "test_*.py"`：30 项测试全部通过。
- `py -m py_compile qq-bug-bot/agent_worker.py qq-bug-bot/agent_protocol.py`：通过。
- `git diff --check`：通过。
