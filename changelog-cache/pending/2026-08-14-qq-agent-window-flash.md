# QQ Agent 黑色窗口闪烁修复

- 日期：2026-08-14
- 分类：修复
- 影响范围：QQ 聊天 Agent、QQ Bug Agent、Windows 计划任务

## 玩家可见说明

- 修复 QQ 机器人 Agent 运行时每隔数秒弹出黑色窗口、干扰电脑正常使用的问题。
- 聊天与 Bug 自动处理继续在后台常驻，不再要求保留可见控制台窗口。

## 技术说明

- Windows 下统一为 SSH、Codex、Git、PowerShell 等 Agent 子进程设置 `CREATE_NO_WINDOW`。
- 将 `GrandUMI-Bug-Agent` 计划任务由 `python.exe` 改为 `pythonw.exe`；安装阶段仍使用控制台 Python 执行一次可见自检，正式常驻进程完全隐藏。
- 部署修复前先停止了每 5 秒轮询的聊天任务，立即终止窗口闪烁。

## 验证结果

- `py -m unittest discover -s qq-bug-bot/tests -p 'test_*.py' -v`：38 项测试全部通过。
- 两个计划任务安装脚本均通过 PowerShell AST 语法检查。
- Windows 回归测试确认子进程创建标志包含 `CREATE_NO_WINDOW`。
