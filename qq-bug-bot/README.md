# GrandUMI QQ 群 bug 反馈机器人

监听 QQ 群里以 `#bug ` 开头的消息,把反馈**存到本地 SQLite** 并**自动提交到 GitHub Issues**,然后在群里 @ 上报人回执。

```
QQ群用户:  #bug OP16-080 的减费光环没生效
        ↓
机器人:    @某某 ✅ 已收到你的反馈 #5,感谢!
                  已同步到 Issue #23
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

## 三、运行

依赖只有 `websockets`,GitHub 走本机已登录的 `gh` CLI(无需 token)。

```powershell
cd D:\Self\GrandUMI\qq-bug-bot
py -m pip install -r requirements.txt
py bot.py
```

看到「已连接 NapCat,等待群消息…」即成功。在群里发一条 `#bug 测试` 验证。

> GitHub Issue 是通过本机 `gh issue create` 创建的,所以机器人必须跑在已 `gh auth login` 的这台机器上。

## 四、查看反馈数据

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
| `config.json` | 你的实际配置(已 gitignore) |
| `feedback.db` | 运行时生成的反馈数据库(已 gitignore) |
