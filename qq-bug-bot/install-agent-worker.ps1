param(
    [string]$RuntimeRoot = "D:\Self\GrandUMI-agent-runtime",
    [string]$RepositoryRoot = "D:\Self\GrandUMI-agent-runtime\repo",
    [string]$SharedNodeModulesPath = "D:\Self\GrandUMI\opcgpro-web\node_modules",
    [string]$TaskName = "GrandUMI-Bug-Agent"
)

$ErrorActionPreference = "Stop"

$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$worker = Join-Path $repo "qq-bug-bot\agent_worker.py"
$example = Join-Path $repo "qq-bug-bot\agent-worker.example.json"
$config = Join-Path $runtime "agent-worker.json"
$jobs = Join-Path $runtime "jobs"
$logs = Join-Path $runtime "logs"
$sharedNodeModules = [IO.Path]::GetFullPath($SharedNodeModulesPath)

if (-not (Test-Path -LiteralPath $worker)) { throw "找不到工作器：$worker" }
if (-not (Test-Path -LiteralPath (Join-Path $repo ".git"))) {
    throw "RepositoryRoot 不是独立 Git 仓库：$repo"
}

$python = (Get-Command py.exe -ErrorAction Stop).Source
[void](Get-Command codex -ErrorAction Stop)
[void](Get-Command git.exe -ErrorAction Stop)
[void](Get-Command ssh.exe -ErrorAction Stop)

New-Item -ItemType Directory -Path $runtime, $jobs, $logs -Force | Out-Null
$cfg = Get-Content -LiteralPath $example -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg.repository_root = $repo
$cfg.jobs_root = $jobs
$cfg.logs_root = $logs
$cfg.shared_node_modules_path = $sharedNodeModules
$cfg | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $config -Encoding UTF8

if (-not (Test-Path -LiteralPath (Join-Path $sharedNodeModules ".bin\next.cmd"))) {
    throw "共享前端依赖不可用：$sharedNodeModules"
}

Write-Host "正在执行本机 Agent 工作器自检……" -ForegroundColor Cyan
& $python $worker --config $config --self-check
if ($LASTEXITCODE -ne 0) { throw "工作器自检失败，未注册计划任务。" }

$action = New-ScheduledTaskAction `
    -Execute $python `
    -Argument ('"' + $worker + '" --config "' + $config + '"') `
    -WorkingDirectory (Split-Path -Parent $worker)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -RestartCount 20 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 3650)
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Register-ScheduledTask `
    -TaskName $TaskName `
    -Description "GrandUMI QQ Bug 自动分析、修复与测试服部署工作器" `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal | Out-Null

Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 3
$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "Agent 工作器已安装并启动。最近结果：$($info.LastTaskResult)" -ForegroundColor Green
Write-Host "配置：$config"
Write-Host "日志：$(Join-Path $logs 'agent-worker.log')"
