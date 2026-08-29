param(
    [string]$Server = "root@103.146.230.37",
    [string]$RemoteDir = "/opt/qq-bug-bot",
    [switch]$EnableAgent
)

$ErrorActionPreference = "Stop"
if ($Server -notmatch '^[A-Za-z0-9._-]+@[A-Za-z0-9.:-]+$') {
    throw "SSH 服务器格式不安全：$Server"
}
if ($RemoteDir -notmatch '^/[A-Za-z0-9._/-]+$') {
    throw "远程目录格式不安全：$RemoteDir"
}

$botDir = $PSScriptRoot
$repo = Split-Path -Parent $botDir
. (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")
$commit = (& git -C $repo rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{12}$') {
    throw "无法读取当前提交。"
}
$files = @(
    ".dockerignore",
    ".env.example",
    "Dockerfile",
    "docker-compose.yml",
    "napcat-init.sh",
    "requirements.txt",
    "bot.py",
    "storage.py",
    "abuse_moderation.py",
    "qq_whitelist_sync.py",
    "github_issue.py",
    "agent_bridge.py",
    "media_pipeline.py",
    "export_by_date.py",
    "mark.py",
    "dedup.py",
    "config.server.example.json"
)
foreach ($name in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $botDir $name))) {
        throw "缺少部署文件：$name"
    }
}

$deployTempDirectory = Get-GrandUmiTempDirectory -Category "Deploy"
$bundle = Join-Path $deployTempDirectory "grandumi-bug-bot-$commit.tar.gz"
$remoteBundle = "/tmp/grandumi-bug-bot-$commit.tar.gz"
$remoteScript = "/tmp/grandumi-deploy-bug-bot-$commit.sh"
$tar = (Get-Command tar.exe -ErrorAction Stop).Source
$scp = (Get-Command scp.exe -ErrorAction Stop).Source
$ssh = (Get-Command ssh.exe -ErrorAction Stop).Source

try {
    if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
    & $tar -czf $bundle -C $botDir @files
    if ($LASTEXITCODE -ne 0) { throw "创建机器人部署包失败。" }
    & $scp -o BatchMode=yes $bundle ($Server + ":" + $remoteBundle)
    if ($LASTEXITCODE -ne 0) { throw "上传机器人部署包失败。" }
    & $scp -o BatchMode=yes (Join-Path $botDir "deploy-bot-server.sh") ($Server + ":" + $remoteScript)
    if ($LASTEXITCODE -ne 0) { throw "上传机器人部署脚本失败。" }
    $enableValue = if ($EnableAgent) { "true" } else { "false" }
    & $ssh -o BatchMode=yes $Server "sh '$remoteScript' '$remoteBundle' '$RemoteDir' '$enableValue'"
    if ($LASTEXITCODE -ne 0) { throw "机器人服务器部署失败，已尝试自动回滚。" }
}
finally {
    if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
}

Write-Host "QQ Bug 机器人部署成功：$commit" -ForegroundColor Green
