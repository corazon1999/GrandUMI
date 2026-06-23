$ErrorActionPreference = "Continue"
$log = "D:\Self\GrandUMI\_deploy-backend.log"
function W($m){ $t = Get-Date -Format "HH:mm:ss"; "$t  $m" | Tee-Object -FilePath $log -Append | Out-Null }
"=== GrandUMI backend deploy start ===" | Out-File $log -Encoding utf8
W "[1/4] Stop service GrandUMI-Backend"
Stop-Service GrandUMI-Backend -Force
Start-Sleep -Seconds 2
W "[2/4] dotnet publish (Release)"
& dotnet publish "D:\Self\GrandUMI\服务端WebSocket\GrandUMIServer.csproj" -c Release -o "D:\Self\GrandUMI\服务端WebSocket\publish" --nologo *>> $log
$pub = $LASTEXITCODE
W "publish exitcode = $pub"
W "[3/4] Start service"
Start-Service GrandUMI-Backend
Start-Sleep -Seconds 2
W "[4/4] service status"
(Get-Service GrandUMI-Backend,GrandUMI-Frontend,GrandUMI-Tunnel | Format-Table Name,Status | Out-String) | Tee-Object -FilePath $log -Append | Out-Null
W "=== done  publish=$pub ==="
