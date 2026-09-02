param(
    [string]$RuntimeRoot = "D:\Self\GrandUMI-agent-runtime",
    [string]$RepositoryRoot = "D:\Self\GrandUMI-agent-runtime\repo",
    [string]$AdminWorkspace = "D:\Self\GrandUMI",
    [string]$TaskName = "GrandUMI-Admin-Agent"
)

$ErrorActionPreference = "Stop"

$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$adminRoot = [IO.Path]::GetFullPath($AdminWorkspace)
$worker = Join-Path $repo "qq-bug-bot\chat_agent_worker.py"
$config = Join-Path $runtime "agent-worker.json"
$logs = Join-Path $runtime "logs"
. (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")
$mediaRoot = Get-GrandUmiTempDirectory -Category "QQBotMedia"
$workspaceLockRoot = Get-GrandUmiTempDirectory -Category "Locks"

if (-not (Test-Path -LiteralPath $worker)) { throw "找不到管理员工作器：$worker" }
if (-not (Test-Path -LiteralPath $config)) { throw "找不到 Agent 配置：$config" }
if (-not (Test-Path -LiteralPath $adminRoot -PathType Container)) {
    throw "管理员 Agent 工作区不存在：$adminRoot"
}
if (-not (Test-Path -LiteralPath (Join-Path $adminRoot "AGENTS.md"))) {
    throw "管理员 Agent 工作区缺少 AGENTS.md：$adminRoot"
}

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

Write-Host "正在执行 QQ 管理员 Agent 自检……" -ForegroundColor Cyan
& $pythonPath $worker `
    --config $config `
    --media-root $mediaRoot `
    --mode admin `
    --admin-workspace $adminRoot `
    --workspace-lock-root $workspaceLockRoot `
    --self-check
if ($LASTEXITCODE -ne 0) { throw "管理员 Agent 自检失败，未注册计划任务。" }

$arguments = (
    '"' + $worker + '" --config "' + $config +
    '" --media-root "' + $mediaRoot +
    '" --mode admin --admin-workspace "' + $adminRoot +
    '" --workspace-lock-root "' + $workspaceLockRoot + '"'
)
$action = New-ScheduledTaskAction `
    -Execute $pythonw `
    -Argument $arguments `
    -WorkingDirectory (Split-Path -Parent $worker)
$triggers = @(
    New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    New-ScheduledTaskTrigger `
        -Once `
        -At (Get-Date).AddMinutes(5) `
        -RepetitionInterval (New-TimeSpan -Minutes 5) `
        -RepetitionDuration (New-TimeSpan -Days 3650)
)
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -RestartCount 100 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 3650) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
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
        throw "旧管理员 Agent 未能停止，拒绝启动重复实例。"
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Register-ScheduledTask `
    -TaskName $TaskName `
    -Description "GrandUMI QQ 管理员专用全权限 Agent" `
    -Action $action `
    -Trigger $triggers `
    -Settings $settings `
    -Principal $principal | Out-Null

Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 3
$state = (Get-ScheduledTask -TaskName $TaskName).State
if ($state -ne "Running") { throw "管理员 Agent 启动失败，当前状态：$state" }
Write-Host "QQ 管理员 Agent 已隐藏常驻。日志：$(Join-Path $logs 'admin-agent-worker.log')" -ForegroundColor Green
