# QQ 白名单改为每日零点更新

- 日期：2026-09-03
- 分类：优化
- 影响范围：QQ 群机器人、游戏 QQ 白名单自动同步
- 状态：已完成

## 玩家可见说明

- 游戏 QQ 白名单现在改为每天凌晨 0 点自动更新，不再每小时更新。
- 群友询问白名单申请或更新时间时，机器人会统一告知每天凌晨 0 点自动更新。

## 技术说明

- 调度器按 `Asia/Singapore` 墙钟每次重算下一个自然日 `00:00:00`，并拒绝执行旧版留下的非零点计划槽。
- 重启时只在零点允许延迟窗口内恢复已持久化任务；当天非零点启动不创建、不补跑当天零点任务，跨日后前向过期旧任务。
- 保留 SQLite 的 `scheduled_hour` 字段、HTTP 的 `scheduledHour` 字段和原幂等键格式，无需数据迁移或服务端协议升级。

## 验证结果

- `py -m unittest qq-bug-bot/tests/test_qq_whitelist_sync.py qq-bug-bot/tests/test_chat_agent.py qq-bug-bot/tests/test_bot_shutdown.py qq-bug-bot/tests/test_deploy_files.py`：90 项通过。
- 除受共享工作区 `.b6-work` 读取权限影响的 `test_agent_worker.py` 外，`qq-bug-bot/tests` 其余全部测试：193 项通过。
- 定向覆盖每日零点计算、跨日切换、旧版非零点任务拒绝、过期不补跑、非零点重启、零点窗口内恢复与重放幂等。
