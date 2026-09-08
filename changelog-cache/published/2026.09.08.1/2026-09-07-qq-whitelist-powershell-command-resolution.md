# 修复白名单导出在多 Node 环境下无法启动

- 日期：2026-09-07
- 分类：修复
- 影响范围：Windows 一键实时 QQ 白名单导出及部署前验证
- 状态：已完成

## 玩家可见说明

- 修复电脑中存在多个 Node.js 安装时，实时 QQ 白名单导出可能在启动阶段报错的问题。

## 技术说明

- Windows PowerShell 5.1 的 `Get-Command` 在 PATH 中存在多个同名程序时可能返回数组，无法直接绑定到要求单个字符串的进程路径参数。脚本现在确定性选择命令解析顺序中的第一项，与直接执行该命令的系统选择一致。
- 同一规则覆盖传输自检与正式导出的 Node.js 路径解析；固定目标群、SSH 目标限制、标准输入传输、源码 SHA-256 校验、白名单本地复验和失败清理语义均保持不变。

## 验证结果

- 原失败用例 `OneClickEntryStaticTests.test传输自检证明WindowsPowerShell子进程收到的源码逐字节一致`：通过。
- `py -m unittest qq-bug-bot.tests.test_live_qq_whitelist_export`：17 项全部通过。
- 在干净部署克隆中运行 `py -3 -m unittest discover -s qq-bug-bot/tests -p "test_*.py"`：234 项全部通过。
