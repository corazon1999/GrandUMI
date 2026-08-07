# Windows 后端版本化发布

生成不可变发布目录：

```powershell
.\ops\windows\publish-versioned.ps1
```

脚本会在 `服务端WebSocket/releases/<时间-提交号>/` 生成完整发布包和 `release.json`，不会改动当前服务。

将 NSSM 服务切换到已构建版本：

```powershell
.\ops\windows\switch-grandumi-backend.ps1 -Version 20260808-120000-abcdef123456
```

切换后会轮询 `/ready`；启动或就绪检查失败时自动恢复原 Application、AppDirectory 和参数。需要人工回到上一个记录版本时执行：

```powershell
.\ops\windows\switch-grandumi-backend.ps1 -Rollback
```

正式环境仍应先完成测试服验收和发布批准，不要直接把“构建成功”等同于“允许上线”。
