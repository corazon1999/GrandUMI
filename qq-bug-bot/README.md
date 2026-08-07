# GrandUMI QQ 群 bug 反馈机器人

监听 QQ 群里以 `#bug ` 开头的消息，把反馈**存到本地 SQLite**、**自动提交到 GitHub Issues**，并可调用所有者电脑上的 Codex Agent 自动分析和修复明确 Bug。

```
QQ群用户:  #bug OP16-080 的减费光环没生效
        ↓
机器人:    @某某 ✅ 已收到你的反馈 #5,感谢!
                  已同步到 Issue #23
                  已进入 Agent 自动分析队列
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
| `command_prefix` | 触发前缀,默认 `#bug `(注意末尾空格) |
| `allowed_groups` | 群号白名单数组,如 `[123456, 789012]`;**留空 `[]` 表示所有群** |
| `create_issue` | 是否自动建 GitHub Issue |
| `github_repo` | 目标仓库,默认 `corazon1999/GrandUMI` |
| `reply_enabled` | 是否在群里回执 |
| `min_content_length` | 反馈正文最少字数,过短不入库 |
| `agent_enabled` | 是否把新反馈送入本机 Agent 队列 |
| `agent_owner_qq` | 功能需求或不确定 Bug 需要确认时 @ 的管理员 QQ |
| `agent_notification_interval_seconds` | 管理员问题和玩家结果通知轮询秒数 |

## Agent 自动分析与修复

启用后，服务端机器人只负责 QQ 会话、SQLite 队列和 GitHub Issue；本机
`agent_worker.py` 通过 SSH 领取任务，在独立 Git worktree 内调用 `codex exec`：

1. 只读分诊：只有明确违反既有规则/规格、置信度不低于 85 的 Bug 才自动修复。
2. 功能需求、规则歧义、无法复现或安全门禁失败时，在原反馈群 @ `651846226`。
3. 管理员必须 @ 当前机器人并发送 `#回复 具体判断`。问题全局串行，因此无需填写反馈编号。
4. 修复 Agent 只能在 `workspace-write` 沙箱内修改代码和运行测试，不能提交或部署。
5. 独立复核 Agent 重新检查 diff 并实际执行固定测试。
6. 固定程序核对路径、文件数、行数、测试事件、更新日志、远端快进状态后，才提交并运行 `deploy-test.ps1`。
7. 测试服外网验证成功后，机器人 @ 原玩家回复摘要、提交号和测试地址。

以下内容永不自动修改：仓库治理文件、CI、`ops/`、部署/发布脚本、依赖与项目清单、机器人自身、密钥和配置。命中门禁后会询问管理员，不会擅自放宽权限。正式服发布不在本流程授权范围内。

### 安装本机工作器

前提：本机 `codex` 已登录且能访问模型，`git`、`ssh`、`py`、`powershell` 可用；
`D:\Self\GrandUMI-agent-runtime\repo` 是独立、干净的 `main` 副本。

```powershell
cd D:\Self\GrandUMI-agent-runtime\repo\qq-bug-bot
.\install-agent-worker.ps1
```

安装器会先运行队列、Git 同步和 Codex 自检；全部通过后才注册并启动当前用户的
`GrandUMI-Bug-Agent` 登录任务。工作器使用 `pythonw.exe` 在后台运行，不需要保留
命令行窗口。运行配置位于
`D:\Self\GrandUMI-agent-runtime\agent-worker.json`，日志位于其 `logs` 子目录。

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

看到「已连接 NapCat,等待群消息…」即成功。在群里发一条 `#bug 测试` 验证。

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
