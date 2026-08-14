param(
    [string]$RuntimeRoot = "D:\Self\GrandUMI-agent-runtime",
    [string]$RepositoryRoot = "D:\Self\GrandUMI-agent-runtime\repo",
    [string]$TaskName = "GrandUMI-Chat-Agent"
)

$ErrorActionPreference = "Stop"

$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$worker = Join-Path $repo "qq-bug-bot\chat_agent_worker.py"
$config = Join-Path $runtime "agent-worker.json"
$logs = Join-Path $runtime "logs"
. (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")
$mediaRoot = Get-GrandUmiTempDirectory -Category "QQBotMedia"

if (-not (Test-Path -LiteralPath $worker)) { throw "找不到聊天工作器：$worker" }
if (-not (Test-Path -LiteralPath $config)) { throw "找不到 Agent 配置：$config" }

$pythonLauncher = (Get-Command py.exe -ErrorAction Stop).Source
$python = (& $pythonLauncher -c "import sys; print(sys.executable)").Trim()
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $python)) {
    throw "无法解析 Python 解释器。"
}
$pythonPath = [IO.Path]::GetFullPath($python)
$pythonw = Join-Path (Split-Path -Parent $pythonPath) "pythonw.exe"
if (-not (Test-Path -LiteralPath $pythonw)) { $pythonw = $pythonPath }
[void](Get-Command codex -ErrorAction Stop)
[void](Get-Command ssh.exe -ErrorAction Stop)
New-Item -ItemType Directory -Path $logs -Force | Out-Null

Write-Host "正在执行 QQ 聊天 Agent 自检……" -ForegroundColor Cyan
& $pythonPath $worker --config $config --media-root $mediaRoot --self-check
if ($LASTEXITCODE -ne 0) { throw "聊天工作器自检失败，未注册计划任务。" }

$action = New-ScheduledTaskAction `
    -Execute $pythonw `
    -Argument ('"' + $worker + '" --config "' + $config + '" --media-root "' + $mediaRoot + '"') `
    -WorkingDirectory (Split-Path -Parent $worker)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -RestartCount 100 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 3650) `
    -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 20; $i++) {
        if ((Get-ScheduledTask -TaskName $TaskName).State -ne "Running") { break }
        Start-Sleep -Milliseconds 250
    }
    if ((Get-ScheduledTask -TaskName $TaskName).State -eq "Running") {
        throw "旧聊天 Agent 未能停止，拒绝启动重复实例。"
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Register-ScheduledTask `
    -TaskName $TaskName `
    -Description "GrandUMI QQ 群女帝汉库克人格只读聊天 Agent" `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal | Out-Null

Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 3
$state = (Get-ScheduledTask -TaskName $TaskName).State
if ($state -ne "Running") { throw "聊天 Agent 启动失败，当前状态：$state" }
Write-Host "QQ 聊天 Agent 已隐藏常驻。日志：$(Join-Path $logs 'chat-agent-worker.log')" -ForegroundColor Green
