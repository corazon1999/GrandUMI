# Windows 一键实时导出游戏 QQ 白名单

需要手工导入最新白名单时，直接双击仓库根目录的 `一键导出QQ白名单.cmd`。入口固定读取 `GrandUMI测试群（297542853）`，不能通过命令行改成其他群；成功后会显示完整文件路径、群成员人数、实时拉取时间和 SHA-256，并在资源管理器中选中新生成的 `qq-whitelist-297542853-YYYYMMDD-HHmmss-fff-live.json`。窗口会保留，便于查看结果。

脚本要求本机已有可用的 `ssh.exe`、`node.exe`、E 盘和到 `root@103.146.230.37` 的 SSH 密钥登录。它通过 SSH 标准输入在 `/opt/qq-bug-bot` 的现有 `bug-bot` 容器内执行只读采样，不向群发送消息，也不在远端创建临时文件。OneBot 连接设置只在容器内部从现有 Docker secret 使用，不回传或打印访问令牌；本机不需要密码、Token 或白名单同步密钥。

每次尝试严格执行以下顺序，三个动作都使用 `no_cache=true`：

1. `get_group_info`
2. `get_group_member_list`
3. `get_group_info`

只有前置群信息、成员列表、后置群信息的群号、群名和人数完全一致，且全部成员均为目标群内唯一的 5–12 位纯数字 QQ 时才接受。拉取期间人数变化会有限重试；错误群、空名单、重复 QQ、无效 QQ、串群记录、SSH/容器/NapCat/OneBot 不可用均直接失败，绝不回退到旧导出。

远端 JSON 先写入由 `ops/windows/GrandUmiTemp.ps1` 分配的 `E:\GrandUMI-Temp` 中转文件，再由游戏实际使用的 `qqWhitelist.mjs` 解析器校验；复制到仓库根目录后会再次校验并比对 SHA-256。成功或失败都会清理中转文件，校验未完成时不交付最终文件。

需要替换 SSH 主机或远端安装目录时，可在 PowerShell 中显式调用；目标群仍不可修改：

```powershell
.\qq-bug-bot\export-live-qq-whitelist.ps1 `
  -SshTarget root@103.146.230.37 `
  -RemoteDir /opt/qq-bug-bot
```

相关文件：

- `../一键导出QQ白名单.cmd`：Windows 双击入口。
- `export-live-qq-whitelist.ps1`：SSH 拉取、E 盘中转、双重本地校验、最终交付与资源管理器定位。
- `export_live_qq_whitelist.py`：容器内只读 OneBot 三段实时采样与严格成员校验。
- `verify_qq_whitelist_export.mjs`：复用游戏解析逻辑校验导出并计算 SHA-256。
