$ErrorActionPreference = "Continue"
$log  = "D:\Self\GrandUMI\deploy-frontend.log"
$web  = "D:\Self\GrandUMI\opcgpro-web"
$next = "$web\.next"
$bak  = "$web\.next.bak"
"=== GrandUMI 仅前端上线（带回滚保护）===" | Out-File $log -Encoding utf8
function W($m){ $t = Get-Date -Format "HH:mm:ss"; "$t  $m" | Tee-Object -FilePath $log -Append | Out-Null }

# 1. 备份当前 .next（同盘 rename，瞬间完成），保证 build 失败可回滚
if (Test-Path $bak) { Remove-Item $bak -Recurse -Force }
if (Test-Path $next) { W "[1/5] 备份 .next -> .next.bak"; Move-Item $next $bak -Force }
else { W "[1/5] 无现有 .next，跳过备份" }

# 2. 停前端（后端不动，对局不受影响）
W "[2/5] 停止前端服务"
Stop-Service GrandUMI-Frontend -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 3. build
W "[3/5] npm run build"
Push-Location $web
& npm.cmd run build *>> $log
$bld = $LASTEXITCODE
Pop-Location
W "build 退出码 = $bld"

# 4. 校验：退出码0 且 BUILD_ID 存在；否则回滚旧 .next
$ok = ($bld -eq 0) -and (Test-Path "$next\BUILD_ID")
if ($ok) {
  W "[4/5] build 成功，删除备份"
  if (Test-Path $bak) { Remove-Item $bak -Recurse -Force }
} else {
  W "[4/5] build 失败！回滚到备份 .next（线上回到旧版本，不会挂）"
  if (Test-Path $next) { Remove-Item $next -Recurse -Force }
  if (Test-Path $bak) { Move-Item $bak $next -Force }
}

# 5. 启前端（无论成败都有完整 .next 可启动）
W "[5/5] 启动前端服务"
Start-Service GrandUMI-Frontend
Start-Sleep -Seconds 5

(Get-Service GrandUMI-Backend,GrandUMI-Frontend,GrandUMI-Tunnel | Format-Table Name,Status | Out-String) | Tee-Object -FilePath $log -Append | Out-Null
if ($ok) { W "=== 完成 OK  build=$bld ===" } else { W "=== build 失败，已回滚旧版本  build=$bld ===" }
