$ErrorActionPreference = "Continue"
$log = "D:\Self\GrandUMI\deploy-online.log"
"=== GrandUMI 上线开始 ===" | Out-File $log -Encoding utf8
function W($m){ $t = Get-Date -Format "HH:mm:ss"; "$t  $m" | Tee-Object -FilePath $log -Append | Out-Null }

W "[1/6] 停止后端服务"
Stop-Service GrandUMI-Backend -Force
Start-Sleep -Seconds 1

W "[2/6] dotnet publish 后端"
& dotnet publish "D:\Self\GrandUMI\服务端WebSocket\GrandUMIServer.csproj" -c Release -o "D:\Self\GrandUMI\服务端WebSocket\publish" --nologo *>> $log
$pub = $LASTEXITCODE
W "publish 退出码 = $pub"

W "[3/6] 启动后端服务"
Start-Service GrandUMI-Backend

W "[4/6] 停止前端服务"
Stop-Service GrandUMI-Frontend -Force
Start-Sleep -Seconds 1

W "[5/6] npm run build 前端"
Push-Location "D:\Self\GrandUMI\opcgpro-web"
& npm.cmd run build *>> $log
$bld = $LASTEXITCODE
Pop-Location
W "build 退出码 = $bld"

W "[6/6] 启动前端服务"
Start-Service GrandUMI-Frontend
Start-Sleep -Seconds 2

(Get-Service GrandUMI-Backend,GrandUMI-Frontend,GrandUMI-Tunnel | Format-Table Name,Status | Out-String) | Tee-Object -FilePath $log -Append | Out-Null
W "=== 完成  publish=$pub  build=$bld ==="
