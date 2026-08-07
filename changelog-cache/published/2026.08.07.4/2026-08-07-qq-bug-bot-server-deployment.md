# QQ Bug 反馈机器人迁移正式服

- 日期：2026-08-07
- 分类：优化
- 影响范围：QQ 群 Bug 反馈、GitHub Issue 同步、反馈日报
- 状态：已完成

## 玩家可见说明

- QQ 群 Bug 反馈机器人已迁移到正式服务器持续运行，反馈可稳定保存并同步到 GitHub，电脑关机后也不会中断服务。
- 原有反馈记录完整保留，群白名单和回执行为保持不变。

## 技术说明

- 使用 Docker Compose 隔离部署 NapCat 与 Python 机器人，OneBot WebSocket 仅在容器内网开放，NapCat WebUI 仅绑定服务器回环地址。
- SQLite、NapCat 登录状态与配置均已持久化；机器人支持通过环境变量指定配置、数据库和日报目录。
- GitHub 调用改为后台线程执行，并使用服务器受限文件中的细粒度 Token，避免阻塞 QQ WebSocket 或把凭据写入镜像和版本库。
- 固定已验证的 `websockets` 版本，并增加可复用的 GitHub Token 本机验证与 SSH 安全配置脚本。

## 验证结果

- 本地通过 Python 语法、服务器配置 JSON、SQLite 初始化、环境变量路径及日报导出验证。
- 正式服完成 Compose 配置检查、机器人镜像构建、NapCat 登录、OneBot 鉴权连接和 GitHub 仓库/Issues API 验证。
- QQ 群端到端测试成功：反馈 #261 已落库并创建 GitHub Issue #194，群内收到回执；测试记录随后标记为非 Bug，Issue 以 `not planned` 关闭。
- 部署完成后 NapCat 与机器人容器持续运行，Caddy、正式服和测试服前后端服务均保持正常。
