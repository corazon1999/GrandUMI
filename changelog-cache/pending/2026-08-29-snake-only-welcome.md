# 新成员仅由 s-蛇欢迎

- 日期：2026-08-29
- 分类：优化
- 影响范围：QQ群助理、新成员入群通知
- 状态：已完成

## 玩家可见说明

- 新成员加入 GrandUMI 测试群后，只会收到 s-蛇的欢迎；s-鲨不再重复参与欢迎，s-鹰行为保持不变。

## 技术说明

- 新人欢迎处理器仅接受主助理 `primary`，即使 s-鲨遗留配置仍为开启也不会发送欢迎。
- 配置样例和服务器部署迁移统一为：s-蛇对群 `297542853` 开启欢迎，s-鹰与 s-鲨关闭且目标群列表为空，避免后续部署重新启用 s-鲨欢迎。

## 验证结果

- `py -B -m unittest discover -s tests -p test_new_member_welcome.py -v`：8 项通过，覆盖仅 s-蛇欢迎、s-鲨遗留开启配置仍不发送、s-鹰不发送及主助理新人验证链路。
- `py -B -m unittest discover -s tests -p test_deploy_files.py -v`：15 项通过，覆盖两份示例配置和服务器持久配置迁移。
- `py -B -m unittest discover -s tests -p test_chat_agent.py -v`：43 项通过，确认 s-鹰与 s-鲨原有管理员助理行为保持不变。
- `py -B -m unittest discover -s tests -p 'test_bot*.py' -v`：6 项通过；两份 JSON 示例通过 PowerShell `ConvertFrom-Json` 解析，`git diff --check` 通过。
