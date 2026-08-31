# s-蛇欢迎新成员

- 日期：2026-08-29
- 分类：新增
- 影响范围：QQ群助理、新成员入群通知
- 状态：已完成

## 玩家可见说明

- 新成员加入指定群后，只由 s-蛇 @ 新成员并发送欢迎语；s-鹰和 s-鲨不会参与欢迎，重复通知也不会造成重复刷屏。

## 技术说明

- 为每条助理连接增加缺省关闭的欢迎开关和显式目标群列表，仅允许 s-蛇处理真实 `group_increase` 通知。
- 欢迎消息使用 OneBot 结构化 `at` 与 `text` 消息段，并在服务端确认发送成功后按助理和事件键进行有界内存去重；发送失败时保留重放重试能力。
- 欢迎通知在 `admin_only` 普通消息权限分支前处理；主助理无论欢迎成功或失败都会继续原有新人验证，副助理不会消费欢迎通知。
- 机器人服务部署时会安全迁移现有连接配置：仅为已经存在的 s-蛇连接启用测试群欢迎，显式关闭 s-鹰和 s-鲨欢迎，并保留访问令牌和其他未知字段；不会创建缺失账号。

## 验证结果

- `py -B -m unittest discover -s tests -p test_new_member_welcome.py -v`：8 项通过，包含主助理欢迎成功或失败均继续新人验证的回归检查。
- `py -m unittest discover -s tests -p test_chat_agent.py -v`：43 项通过。
- `py -m unittest discover -s tests -p test_group_add_auto_approval.py -v`：19 项通过。
- `py -m unittest discover -s tests -p test_member_verification.py -v`：30 项通过。
- `py -m unittest discover -s tests -p test_bot*.py -v`：6 项通过。
- `py -B -m unittest discover -s tests -p test_deploy_files.py -v`：15 项通过，包含生产配置迁移结构验证。
- 两份配置样例均通过 PowerShell `ConvertFrom-Json` 解析，任务文件通过 `git diff --check`。
