# 协作工作流

本仓库的日常 git 协作指南。所有命令默认在仓库根目录（如 `D:\Self\GrandUMI\`）执行。

---

## 最常用的 4 条命令

```powershell
git status                          # 看哪些文件改了
git add .                           # 暂存全部改动（也可指定文件名）
git commit -m "feat: 描述你做了什么"
git push                            # 推到 GitHub
```

> 90% 的日常操作就这 4 条。其他命令是出问题时才用。

---

## 场景 1：自己改了代码 → 推到 GitHub

```powershell
git status
git add .
git commit -m "feat: 新增登录界面"
git push
```

---

## 场景 2：拉取对方的新提交

```powershell
git pull
```

`git pull` = `git fetch`（下载远端改动）+ `git merge`（合并到本地）。

**建议每天开工前先 `git pull` 一次**，避免攒一堆本地改动后才发现要合并。

---

## 场景 3：`git push` 报错说"远端有新提交"

说明对方抢先 push 了，需要先拉再推：

```powershell
git pull --rebase                   # 把你的本地 commit 暂时移开，先吃掉对方的，再把你的接到后面
git push
```

`--rebase` 让历史是直线，比默认 merge 干净。

---

## 场景 4：冲突（两人改了同一文件的同一行）

`git pull` 或 `git pull --rebase` 后如果有冲突，git 会提示哪些文件冲突：

```powershell
git status                          # 看哪些文件冲突（带 CONFLICT 标记）
```

用编辑器打开冲突文件，里面会有类似的标记：

```
<<<<<<< HEAD
你的本地内容
=======
对方推上来的内容
>>>>>>> origin/main
```

**手动选择保留哪段**（或合并两段），**删掉 `<<<<<<<` / `=======` / `>>>>>>>` 这三行标记**，保存。

然后：

```powershell
git add <冲突文件>                  # 标记冲突已解决
git rebase --continue               # 如果是 rebase 模式
# 或
git commit                          # 如果是 merge 模式（无 --rebase 参数）
git push
```

---

## Commit Message 规范

| 前缀 | 用途 | 示例 |
|---|---|---|
| `feat:` | 新功能 | `feat: 卡组编辑器新增费用曲线图` |
| `fix:` | 修 bug | `fix: 修复出牌时手牌不刷新` |
| `refactor:` | 重构（不改行为） | `refactor: 抽离 NetManager 心跳逻辑` |
| `docs:` | 文档 | `docs: 更新启动流程说明` |
| `chore:` | 杂项（依赖、配置） | `chore: 升级 next 到 15.2` |
| `style:` | 格式调整（空格、命名等） | `style: 统一服务端文件缩进` |
| `test:` | 测试相关 | `test: 添加战斗流程单元测试` |

---

## 撤销操作速查

| 想做什么 | 命令 |
|---|---|
| 撤销工作区改动（还没 `git add`） | `git checkout -- <文件>` |
| 撤销暂存（已 add 还没 commit） | `git reset <文件>` |
| 撤销最后一个 commit（还没 push，保留改动） | `git reset --soft HEAD~1` |
| 撤销最后一个 commit（还没 push，丢弃改动） | `git reset --hard HEAD~1`（⚠️ 不可恢复） |
| 撤销已 push 的 commit（新建反向 commit） | `git revert HEAD && git push` |
| 看历史 | `git log --oneline -20` |
| 看某次 commit 改了啥 | `git show <commit-hash>` |

---

## 不要做的事

1. ❌ **不要 `git push --force`** —— 会覆盖对方的提交，灾难性，几乎没有合理场景
2. ❌ **不要 commit 密钥、密码、`.env`** —— 一旦推上去就是公开记录（即使删了 git 历史还在）
3. ❌ **不要把 `node_modules/`、`bin/`、`obj/`、`Library/` 等生成物 commit 进去** —— `.gitignore` 已经处理，但 `git add -A` 后请 `git status` 确认
4. ❌ **不要直接修改本仓库不该改的资源**：`CardImages/`、`opcgpro-web/public/sprites/`、`opcgpro-web/public/cards/`、`OPCGPro/` 这些都在 `.gitignore` 里，本机改动不会同步给对方
5. ❌ **不要随意改 `.gitignore` / `.gitattributes`** —— 改动前先在群里同步一下

---

## 推荐养成的习惯

1. ✅ **每天开工前先 `git pull`**
2. ✅ **commit 要勤、要小** —— 一个小功能 / 修一个 bug 就 commit 一次，比攒一周一次好
3. ✅ **push 前 `git status` 确认一下** —— 避免推上临时调试代码
4. ✅ **改了什么 commit 什么** —— `git add <具体文件>` 比 `git add .` 更安全
5. ✅ **commit message 写"为什么"而不是"做了什么"** —— diff 会告诉别人做了什么，message 应该解释为什么这么做

---

## 大型资源同步

仓库不包含 Unity 客户端、卡牌图片等大型资源（见 [README.md](README.md#仓库未包含的资源)）。这些通过 **网盘 / U盘** 在两人之间同步，不走 git。

约定：每次 Unity 客户端有大版本更新时，发起方在群里同步链接。

---

## 仓库地址 & 协作者

- 仓库：https://github.com/corazon1999/GrandUMI （私有）
- 所有人 owner：**corazon1999**
- 协作者 write：**watermelon1519**
