# GrandUMI QQ 群 bug 反馈机器人

监听 QQ 群里任何包含 `bug`（忽略大小写）的消息。机器人先检查描述能否定位问题：信息完整时**记录到本地 SQLite 和 GitHub Issues、回复记录编号，但不执行自动修复**，信息不足时只追问具体缺失项；玩家下一条回复会自动与原描述合并后再次检查。

玩家只需 @机器人即可唤起独立的只读聊天 Agent，不再要求 `#聊天` 前缀，也不发送“听见了、收到、稍等”等中间确认。普通群友的聊天任务不会进入 Bug 修复工作区，也不能修改代码或执行玩家夹带的命令。

唯一管理员 QQ `651846226` 可在群里直接发送 `#切换娜美`、`#切换罗宾` 或 `#切换女帝`。人格按群持久保存，默认是女帝；切换会影响该群后续的普通聊天、图片识别、Bug 补充追问、记录成功后的夸赞和管理员 Agent 回复。已经排队的消息保留入队时的人格，不会因稍后的切换改变。普通群友发送切换命令不会生效。

唯一管理员 QQ `651846226` 真实 @机器人时，请求进入独立的管理员 Agent 队列，并优先于消息中的 `bug` 关键字路由。管理员 Agent 在 `D:\Self\GrandUMI` 以当前 Windows 用户权限运行，可读取和修改项目、执行命令、联网检索、测试与部署。身份只取 OneBot 原始事件的 `user_id` 与真实 `at` 消息段，正文、截图、引用或转发中的 QQ 号不能冒充管理员。管理员不 @机器人时，包含 `bug` 的消息仍按普通 Bug 收集处理。

@机器人时可以同时发送 PNG、JPEG、WebP 图片或合并转发消息。机器人会展开合并转发中的说话人、文字和图片，把最多 4 张受限下载的图片交给只读视觉模型识别；图片同样可用于补充 Bug 描述。

```
QQ群用户:  这张卡有 bug
        ↓
机器人:    @某某 是哪张卡？做了什么操作，实际结果和预期结果分别是什么？
        ↓
QQ群用户:  OP16-080 的减费光环在角色登场后没有生效，预期费用减 1
        ↓
机器人:    Bug #305 已记录。描述得很清楚，做得不错。
```

底层走 **OneBot 11** 协议,QQ 接入用 **NapCat**(正向 WebSocket)。

---

## 一、准备 NapCat(QQ 接入端)

机器人本身不登录 QQ,它通过 NapCat 收发消息。NapCat 是一个独立程序,需要你用一个**小号**登录。

1. 下载 NapCat:<https://github.com/NapNeko/NapCatQQ>(按其 README 安装,Windows 有一键版)。
2. 用小号登录 NapCat。
3. 在 NapCat 的「网络配置」里**新增一个「WebSocket 服务器」**(即正向 WS):
   - 监听地址 `127.0.0.1`,端口 `3001`(与本项目 `config.json` 的 `ws_url` 一致)。
   - 如设置了 `access_token`,把同样的值填进 `config.json`;不设就留空。
4. 把这台机器人要监听的 QQ 群,确保小号已在群内。

> 也兼容 Lagrange.OneBot / go-cqhttp,只要开启「正向 WebSocket 服务端」并对上端口即可。

## 二、配置机器人

```powershell
cd D:\Self\GrandUMI\qq-bug-bot
copy config.example.json config.json
```

编辑 `config.json`:

| 字段 | 说明 |
|------|------|
| `ws_url` | NapCat 正向 WS 地址,默认 `ws://127.0.0.1:3001` |
| `access_token` | 与 NapCat 一致;没设留空字符串 |
| `allowed_groups` | 群号白名单数组,如 `[123456, 789012]`;**留空 `[]` 表示所有群** |
| `create_issue` | 是否自动建 GitHub Issue |
| `github_repo` | 目标仓库,默认 `corazon1999/GrandUMI` |
| `agent_enabled` | 是否把新反馈送入本机 Agent 队列 |
| `agent_owner_qq` | 功能需求或不确定 Bug 需要确认时 @ 的管理员 QQ |
| `agent_notification_interval_seconds` | 管理员问题和玩家结果通知轮询秒数 |
| `chat_agent_enabled` | 是否接受玩家 @机器人后的聊天请求 |
| `chat_max_content_length` | 单条聊天正文最大字数，默认 500 |
| `admin_agent_enabled` | 是否启用管理员真实 @机器人后的独立全权限 Agent |
| `admin_agent_owner_qq` | 唯一管理员 QQ，固定为 `651846226` |
| `admin_agent_max_content_length` | 单条管理员任务正文最大字数，默认 3000 |
| `vision_enabled` | 是否允许读取聊天和 Bug 反馈中的图片，默认开启 |
| `vision_max_images` | 单条消息最多读取图片数，默认 4 |
| `vision_max_image_bytes` | 单张图片最大字节数，默认 8 MiB |
| `vision_media_ttl_seconds` | 未完成识别的服务器临时图片保留秒数，默认 86400 |
| `forward_max_nodes` | 合并转发最多展开的消息段数，默认 40 |
| `forward_max_depth` | 嵌套合并转发最大深度，默认 3 |

## 可切换人格聊天 Agent

聊天和 Bug 描述检查使用独立的只读队列和常驻工作器。女帝人格高傲、优雅且护短；娜美人格聪明干练、直率且刀子嘴豆腐心；罗宾人格冷静知性、温和并带有克制的幽默。聊天回复会参考同群最近 6 轮已完成聊天，但最多输出 500 字；玩家输入和图片只作为不可信数据，不会被当作工具指令。图片先在服务器校验协议、公网地址、体积和文件头，再通过 SSH 拉到 `E:\GrandUMI-Temp\QQBotMedia`，校验 SHA-256 后使用 Codex `--image` 只读识别，任务结束立即清理。安装或更新本机工作器：

```powershell
cd D:\Self\GrandUMI-agent-runtime\repo\qq-bug-bot
.\install-chat-agent-worker.ps1

# 安装管理员专用全权限 Agent
.\install-admin-agent-worker.ps1
```

两个安装器自检通过后会分别注册并启动 `GrandUMI-Chat-Agent` 与 `GrandUMI-Admin-Agent` 登录任务。两者使用隐藏的 `pythonw.exe` 独立常驻、独立领取队列，避免长时间管理员任务阻塞普通聊天；日志分别位于 `chat-agent-worker.log` 与 `admin-agent-worker.log`。

## Agent 自动分析与修复

启用后，服务端机器人只负责 QQ 会话、SQLite 队列和 GitHub Issue；本机
`agent_worker.py` 通过 SSH 领取任务，在独立 Git worktree 内调用 `codex exec`：

1. 只读分诊：只有明确违反既有规则/规格、置信度不低于 85 的 Bug 才自动修复。
2. 功能需求、规则歧义或信息不足时，在原反馈群 @ `651846226`；已回答但仍无法确定的任务直接转人工，不会反复询问。
3. 指定管理员 QQ `651846226` 直接发送 `#回复 具体判断` 即可，无需真正 @ 机器人。问题全局串行，因此无需填写反馈编号。
4. 修复 Agent 只能在 `workspace-write` 沙箱内修改代码和运行测试，不能提交或部署。
5. 独立复核 Agent 在一个可丢弃的 detached worktree 中重新检查 diff 并实际执行固定测试；新增的 `*.test.mjs` 会自动进入必跑测试。即使复核 Agent 违规修改文件，也只会丢弃隔离副本，不会污染原修复。
6. 复核不通过时，修复 Agent 默认在同一工作区内按复核意见修订 1 次；再次失败则转人工，不会清理后重新排队。
7. 固定程序核对路径、文件数、行数、测试事件、更新日志、远端快进状态后，才提交并运行 `deploy-test.ps1`。
8. 测试服外网验证成功后，机器人 @ 原玩家回复摘要、提交号和测试地址。

以下内容永不自动修改：仓库治理文件、CI、`ops/`、部署/发布脚本、依赖与项目清单、机器人自身、密钥和配置。命中门禁或有界修订后仍无法验证时会转人工，不会擅自放宽权限或循环重试。正式服发布不在本流程授权范围内。

### 安装本机工作器

前提：本机 `codex` 已登录且能访问模型，`git`、`ssh`、`py`、`powershell` 可用；
`D:\Self\GrandUMI-agent-runtime\repo` 是独立、干净的 `main` 副本。

```powershell
cd D:\Self\GrandUMI-agent-runtime\repo\qq-bug-bot
.\install-agent-worker.ps1
```

安装器会先运行队列、Git 同步和 Codex 自检；全部通过后才注册并启动当前用户的
`GrandUMI-Bug-Agent` 登录任务。工作器使用隐藏的 `pythonw.exe` 常驻运行，
Codex、SSH、Git 等子进程也会使用 Windows 无窗口模式，不会反复弹出黑色窗口。运行配置位于
`D:\Self\GrandUMI-agent-runtime\agent-worker.json`，日志位于其 `logs` 子目录。
`max_review_revisions` 控制独立复核后的有界修订次数（默认 1），
`max_transient_attempts` 控制模型或连接瞬时故障的最大尝试次数（默认 3）。

### 部署并启用服务器机器人

先部署代码但保持 Agent 关闭：

```powershell
.\deploy-bot-server.ps1
```

本机工作器自检通过后，再原子启用：

```powershell
.\deploy-bot-server.ps1 -EnableAgent
```

部署脚本不会复制或打印 `.env`、`config.server.json`、QQ 登录数据或反馈数据库；
它会构建并检查新容器，失败时恢复原文件与配置。

## 三、运行

依赖只有 `websockets`,GitHub 走本机已登录的 `gh` CLI(无需 token)。

```powershell
cd D:\Self\GrandUMI\qq-bug-bot
py -m pip install -r requirements.txt
py bot.py
```

看到「已连接 NapCat,等待群消息…」即成功。可以在测试群 @机器人验证聊天，或发送一条含 `bug` 的消息验证描述检查。

> GitHub Issue 是通过本机 `gh issue create` 创建的,所以机器人必须跑在已 `gh auth login` 的这台机器上。

## 四、Linux 服务器 Docker 部署

推荐把 NapCat 和机器人放在同一个 Compose 项目中。NapCat 的 OneBot 端口只在
Docker 内网开放,宿主机仅在 `127.0.0.1:6099` 提供 WebUI,避免管理端口暴露到公网。

### 1. 创建服务器配置

```bash
cd /opt/qq-bug-bot
cp .env.example .env
cp config.server.example.json config.server.json
mkdir -p data
chmod 600 .env
chown root:10001 config.server.json
chmod 640 config.server.json
chown -R 10001:10001 data
```

编辑 `.env`：

- `GH_TOKEN` 使用只允许目标仓库创建 Issue 的细粒度 GitHub Token。
- `TZ` 使用服务器业务时区。

在 Windows 部署电脑上也可以运行以下脚本。脚本会隐藏输入、先验证目标仓库与
Issues 的读取权限,再通过 SSH 标准输入写入服务器,不会把 Token 放进命令行：

```powershell
.\configure-github-token.ps1
```

编辑 `config.server.json`：

- `ws_url` 保持 `ws://napcat:3001`。
- `access_token` 设置随机长字符串,并在 NapCat 的正向 WebSocket 配置中填写相同值。
- `allowed_groups` 填实际群号白名单,不要留空开放所有群。

### 2. 启动和查看日志

```bash
docker compose config
docker compose build
docker compose up -d
docker compose logs -f --tail=200
```

NapCat 首次启动后,从部署电脑建立 SSH 隧道：

```powershell
ssh -L 6099:127.0.0.1:6099 root@服务器地址
```

然后在浏览器打开 `http://127.0.0.1:6099/webui` 完成 QQ 登录,新增监听
`0.0.0.0:3001` 的正向 WebSocket 服务端,并配置与机器人一致的访问令牌。

### 3. 迁移和维护数据

正式切换前先停止本机机器人,再把旧库复制为服务器的
`/opt/qq-bug-bot/data/feedback.db`,避免两端同时消费同一条消息。

```bash
# 导出日报
docker compose exec bug-bot python export_by_date.py

# 查看反馈
docker compose exec bug-bot python -c \
  "import sqlite3; print(sqlite3.connect('/data/feedback.db').execute('select count(*) from feedback').fetchone())"

# 更新镜像并重启
docker compose pull napcat
docker compose up -d --build
```

必须备份 `data/` 目录和 NapCat 的三个命名卷。不要提交 `.env`、
`config.server.json`、`feedback.db` 或任何登录信息。

## 五、查看反馈数据

所有反馈都存在同目录的 `feedback.db`(SQLite)。即使 GitHub 建 issue 失败,本地也一定有记录。

```powershell
py -c "import sqlite3; [print(r) for r in sqlite3.connect('feedback.db').execute('select id,qq,nickname,content,issue_no,created_at from feedback order by id desc')]"
```

## 文件结构

| 文件 | 作用 |
|------|------|
| `bot.py` | 主程序:连 OneBot WS、识别指令、调存储/建 issue、回执 |
| `storage.py` | SQLite 存储封装 |
| `github_issue.py` | 通过 gh CLI 建 GitHub Issue |
| `agent_bridge.py` | 服务器侧队列领取、问题和结果回写桥 |
| `agent_worker.py` | 本机 Codex 分诊、修复、复核、提交和测试服部署工作器 |
| `agent_protocol.py` | Agent 提示词和安全路径白名单 |
| `schemas/` | Codex 结构化分诊、修复和复核结果定义 |
| `config.json` | 你的实际配置(已 gitignore) |
| `feedback.db` | 运行时生成的反馈数据库(已 gitignore) |
