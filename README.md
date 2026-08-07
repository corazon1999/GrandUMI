# GrandUMI

> 基于《海贼王》OPCG（One Piece Card Game）的多人实时网络卡牌对战游戏。
> 产品名 **YGOGrandUMI**，由 D の一味 开发。

## 仓库结构

```
GrandUMI/
├── OPCGPro/              # Unity 客户端（Unity 2021.3.26f1c1 LTS）—— 不在本仓库
├── opcgpro-web/          # Next.js Web 端
├── 服务端WebSocket/      # C# .NET 10 WebSocket 服务（端口 8080）
├── 卡牌数据/             # 卡集 JSON 数据（OP01–OP15, ST01–ST29, EB01–EB04, P）
├── 文档/                 # 设计文档（架构 / 启动流程 / 游戏规则 / 重构方案 等）
├── *.mjs / *.js / *.py   # 卡牌爬取与下载脚本
└── README.md
```

## 仓库未包含的资源

为控制仓库体积，以下大型资源**不纳入 git**，需通过其他途径获取：

| 路径 | 大小 | 获取方式 |
|---|---|---|
| `OPCGPro/`（Unity 客户端） | ~2.3 GB | 由作者通过网盘 / U盘 提供 |
| `CardImages/` | ~496 MB | 网盘提供，或自行运行 `node download_cards.mjs` 下载 |
| `opcgpro-web/public/sprites/` | ~747 MB | 网盘提供，或自行运行 `node download_cards.mjs` 后转换 |
| `opcgpro-web/public/cards/` | (NTFS junction) | 用 `mklink /J opcgpro-web\public\cards <CardImages 绝对路径>` 重建 |

## 协作者上手流程

### 1. 接受协作邀请

登录 GitHub 后访问 https://github.com/corazon1999/GrandUMI/invitations 点 Accept，或在收件邮箱里点邀请链接。

### 2. Clone 仓库

```powershell
git clone https://github.com/corazon1999/GrandUMI.git
cd GrandUMI
```

### 3. 补全缺失资源

向作者获取 `OPCGPro/`、`CardImages/`、`opcgpro-web/public/sprites/` 三份资源，分别解压到对应位置。

### 4. 重建 NTFS Junction（Web 端卡图）

`opcgpro-web/public/cards` 在仓库里被忽略，需要在你本机重新建立 junction 指向 CardImages：

```cmd
:: 用 cmd 执行（PowerShell 不支持 mklink /J 内置）
:: 假设你把 CardImages 放在 D:\GrandUMI\CardImages
mklink /J opcgpro-web\public\cards D:\GrandUMI\CardImages
```

### 5. 安装依赖

**Web 端：**

```powershell
cd opcgpro-web
npm install
```

**服务端：**

```powershell
cd 服务端WebSocket
dotnet restore
```

### 6. 启动项目

**先启动服务端**（监听 8080）：

```powershell
cd 服务端WebSocket
dotnet run
```

看到下面输出即就绪：
```
╔══════════════════════════════════════╗
║    GrandUMI WebSocket 服务器          ║
║    ws://localhost:8080/ws/            ║
╚══════════════════════════════════════╝
```

**再启动 Web 端**：

```powershell
cd opcgpro-web
npm run dev
```

浏览器访问 http://localhost:3000

**Unity 客户端**：用 Unity Hub 打开 `OPCGPro/`，Unity 版本必须是 **2021.3.26f1c1 LTS**。

> 详细启动说明见 [`文档/启动流程.md`](文档/启动流程.md)。

## 技术栈

| 模块 | 技术 |
|---|---|
| Unity 客户端 | Unity 2021.3.26f1c1 LTS · C# 8.0 · DOTween · TextMesh Pro · protobuf-net · AES 加密 |
| Web 端 | Next.js（详见 `opcgpro-web/AGENTS.md` —— 注意不是标准 Next.js） |
| 服务端 | C# .NET 10 · WebSocket · 端口 8080 |
| 协议 | WebSocket + protobuf-net + AES 动态密钥，30s 心跳 |

## 日常协作

```powershell
# 拉取最新
git pull

# 提交改动
git add <files>
git commit -m "feat: 描述你做了什么"
git push
```

提交规范建议：
- `feat:` 新功能
- `fix:` 修 bug
- `refactor:` 重构
- `docs:` 文档
- `chore:` 杂项（依赖、配置）

## 测试服与正式服发布

所有已经实现并通过相关验证的新功能、新需求和 Bug 修复，完成后都应先在
`changelog-cache/pending/` 中留下记录，再提交当前任务涉及的文件并自动部署到测试服：

```powershell
.\deploy-test.ps1
```

日常自动部署测试服不要求提前清空 `pending/`。不得把无关工作区改动混入提交；部署脚本执行成功且
`https://test.grand-umi.com` 的 HTTP 验证通过后，才算已经提交到测试服。测试服部署不等于正式发布批准。

准备正式发布时，再集中处理更新日志缓存：

1. 检查 `changelog-cache/pending/` 下的全部修复记录。
2. 将所有玩家可见说明按“新增 / 修复 / 优化”汇总到 `opcgpro-web/src/data/changelog.ts` 的新版本条目。
3. 将已汇总记录归档到 `changelog-cache/published/<版本号>/`，确认待发布缓存没有遗漏。
4. 确认正式发布候选提交已经通过测试服验证，再批准正式发布。

缓存记录和发布检查清单详见 [`changelog-cache/README.md`](changelog-cache/README.md)。`pending/` 仍有本次应发布的记录时，不得批准正式发布。

确认当前测试服版本可以正式发布后，单独执行批准命令：

```powershell
.\approve-release.ps1
```

服务器会在北京时间每天 `00:00` 自动发布已批准版本。如果正式服仍有 WebSocket
玩家连接，则会等待最多一小时；仍有玩家在线时跳过当晚发布，不会强制断线。

`deploy-hk.ps1` 只保留紧急发布用途，必须显式添加 `-Emergency`。服务器配置源文件
位于 `ops/server/`。

## 文档索引

- [架构文档](文档/架构文档.md) —— 分层 / 模块 / 卡牌系统 / 网络协议
- [启动流程](文档/启动流程.md) —— 详细的启动命令与端口配置
- [游戏规则文档](文档/游戏规则文档.md) / [游戏规则学习文档](文档/游戏规则学习文档.md)
- [重构方案](文档/重构方案.md)
- [账号系统备用方案](文档/账号系统备用方案.md)
- [OP 综合规则 V1.2.0](文档/OP%20综合规则%20V1.2.0.pdf)（官方规则书）

---

**仓库可见性**：私有 · **协作者**：corazon1999（owner）· watermelon1519（write）
