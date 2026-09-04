# ============================================================
# deploy-hk.ps1 — GrandUMI 正式服 A/B 紧急发布入口
# 用法：
#   .\deploy-hk.ps1 -Emergency
#   .\deploy-hk.ps1 -Emergency -All   # 兼容参数；A/B 流程始终完整构建前后端
#
# 只有 -Emergency 会跳过在线房间排空等待。目标提交仍必须满足：
# main/工作区/远端一致、测试服同提交完整验证、更新日志已归档、
# 当前正式版是目标祖先、共享账号权威健康，以及 A/B 切槽与快照门禁。
# 本文件必须保持 UTF-8 with BOM，兼容 Windows PowerShell 5.1。
# ============================================================
param(
  [string]$Commit = "",
  [switch]$All,
  [switch]$Emergency,
  [string]$Server = "root@103.146.230.37"
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
Set-Location $repo

function Die([string]$Message) {
  Write-Host $Message -ForegroundColor Red
  exit 1
}

function Assert-LastExitCode([string]$Message) {
  if ($LASTEXITCODE -ne 0) { Die $Message }
}

if (-not $Emergency) {
  Die "正式服紧急发布必须显式添加 -Emergency；日常改动请运行 .\deploy-test.ps1。"
}
if ($Server -ne "root@103.146.230.37") {
  Die "安全检查失败：正式服紧急发布只允许 root@103.146.230.37。"
}
if ($Commit) {
  Write-Host "提示：-Commit 自动暂存功能已停用；发布入口只接受已经提交且边界清晰的干净工作区。" -ForegroundColor Yellow
}
if ($All) {
  Write-Host "提示：当前 A/B 发布流程始终完整构建前后端，-All 仅为兼容旧命令保留。" -ForegroundColor Yellow
}

# Codex 自带的 Git 不一定在系统 PATH 中；优先用 PATH，其次寻找本机运行时。
$gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
$gitCandidates = @(
  @(
    $(if ($gitCommand) { $gitCommand.Source }),
    "$env:ProgramFiles\Git\cmd\git.exe",
    "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe"
  ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
)
if (-not $gitCandidates) {
  $codexGit = Get-ChildItem "$env:USERPROFILE\.cache\codex-runtimes" -Filter git.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*\native\git\cmd\git.exe' } |
    Select-Object -First 1
  if ($codexGit) { $gitCandidates = @($codexGit.FullName) }
}
if (-not $gitCandidates) { Die "未找到 git.exe，请先安装 Git for Windows。" }
$git = $gitCandidates[0]
$ssh = (Get-Command ssh.exe -ErrorAction Stop).Source

Write-Host "===== [1/5] 校验本地 main 与工作区 =====" -ForegroundColor Cyan
$branch = (& $git branch --show-current).Trim()
Assert-LastExitCode "无法读取当前 Git 分支。"
if ($branch -ne "main") { Die "正式服发布必须从 main 分支执行，当前为 $branch。" }
$dirty = & $git status --porcelain
Assert-LastExitCode "无法读取工作区状态。"
if ($dirty) {
  & $git status --short
  Die "工作区存在未提交改动；紧急发布入口不会自动暂存或提交。"
}

Write-Host "===== [2/5] 安全同步并精确推送 origin/main =====" -ForegroundColor Cyan
& $git fetch --prune origin main
Assert-LastExitCode "获取 origin/main 失败，未执行正式发布。"
$localHead = (& $git rev-parse HEAD).Trim()
Assert-LastExitCode "无法解析本地 HEAD。"
$originHead = (& $git rev-parse refs/remotes/origin/main).Trim()
Assert-LastExitCode "无法解析 origin/main。"

if ($localHead -ne $originHead) {
  & $git merge-base --is-ancestor $originHead $localHead
  $originIsAncestor = $LASTEXITCODE -eq 0
  & $git merge-base --is-ancestor $localHead $originHead
  $localIsAncestor = $LASTEXITCODE -eq 0
  if ($originIsAncestor) {
    Write-Host "本地 main 是 origin/main 的安全后继，将推送本地提交。"
  } elseif ($localIsAncestor) {
    & $git merge --ff-only refs/remotes/origin/main
    Assert-LastExitCode "本地 main 无法安全快进到 origin/main，已停止。"
    $localHead = (& $git rev-parse HEAD).Trim()
  } else {
    Die "本地 main 与 origin/main 已分叉，拒绝自动合并或覆盖。"
  }
}

& $git push origin main
Assert-LastExitCode "推送 origin/main 失败，未执行正式发布。"
& $git fetch --prune origin main
Assert-LastExitCode "推送后的远端复核失败，未执行正式发布。"
$localHead = (& $git rev-parse HEAD).Trim()
$originHead = (& $git rev-parse refs/remotes/origin/main).Trim()
if ($localHead -notmatch '^[0-9a-f]{40}$' -or $originHead -ne $localHead) {
  Die "推送后本地 HEAD 与 origin/main 不完全一致。"
}

$pending = @(& $git ls-tree -r --name-only $localHead -- changelog-cache/pending |
  Where-Object { $_ -match '\.md$' })
Assert-LastExitCode "无法检查目标提交的更新日志归档状态。"
if ($pending.Count -gt 0) {
  Write-Host ($pending -join "`n") -ForegroundColor Yellow
  Die "目标提交仍有待发布更新日志记录，拒绝正式发布。"
}

Write-Host "===== [3/5] 固定远端仓库到同一目标提交（不修改工作树） =====" -ForegroundColor Cyan
$gitUrl = "https://github.com/corazon1999/GrandUMI.git"
$remoteFetch = "git -C /opt/grandumi fetch --force --prune '$gitUrl' 'refs/heads/main:refs/remotes/origin/main'"
& $ssh -o BatchMode=yes $Server $remoteFetch
Assert-LastExitCode "正式服无法获取远端 main，未执行构建或切槽。"
$serverMain = (& $ssh -o BatchMode=yes $Server "git -C /opt/grandumi rev-parse refs/remotes/origin/main").Trim()
Assert-LastExitCode "无法读取正式服仓库的 origin/main。"
if ($serverMain -ne $localHead) {
  Die "正式服仓库读取到的 main 与本地目标不一致：服务器 $serverMain，本地 $localHead。"
}

Write-Host "===== [4/5] 执行版本化紧急 A/B 发布 =====" -ForegroundColor Cyan
$shortHead = $localHead.Substring(0, 12)
$nonce = [Guid]::NewGuid().ToString("N")
$remoteScript = "/run/grandumi-emergency-$shortHead-$nonce.sh"
$serverScriptPath = "ops/server/deploy-grandumi-production-emergency.sh"
$remoteDeploy = @"
set -Eeuo pipefail
script='$remoteScript'
trap 'rm -f -- "`$script"' EXIT
git -C /opt/grandumi show '${localHead}:$serverScriptPath' > "`$script"
chmod 0700 "`$script"
GRANDUMI_PRODUCTION_IP=103.146.230.37 bash "`$script" --emergency '$localHead'
"@
# Windows PowerShell 的 here-string 使用 CRLF；ssh 会原样交给 Linux shell，首行的
# `pipefail\r` 会在任何远端门禁运行前失败。只归一化命令载荷，不修改目标提交内的脚本。
$remoteDeploy = $remoteDeploy.Replace("`r", "")
& $ssh -o BatchMode=yes $Server $remoteDeploy
Assert-LastExitCode "正式服版本化紧急发布失败；请按服务器输出核对槽位、快照和共享账号状态。"

Write-Host "===== [5/5] 核验正式服版本、健康状态与直连顺序 =====" -ForegroundColor Cyan
$deployedHead = (& $ssh -o BatchMode=yes $Server "tr -d '\r\n' < /var/lib/grandumi-production-deployed").Trim()
Assert-LastExitCode "无法读取正式服已部署版本标记。"
if ($deployedHead -ne $localHead) {
  Die "正式服版本标记不一致：期望 $localHead，实际 $deployedHead。"
}

$homeCode = & curl.exe -sS --noproxy '*' -o NUL -w "%{http_code}" -L "https://ygo.grand-umi.com/"
Assert-LastExitCode "正式服首页公网请求失败。"
if ($homeCode -ne "200") { Die "正式服首页公网验证失败：HTTP $homeCode。" }

$readyRaw = & curl.exe -fsS --noproxy '*' "https://ygo.grand-umi.com/backend/ready"
Assert-LastExitCode "正式服 /backend/ready 公网验证失败。"
$versionRaw = & curl.exe -fsS --noproxy '*' "https://ygo.grand-umi.com/backend/version"
Assert-LastExitCode "正式服 /backend/version 公网验证失败。"
$directReadyRaw = & curl.exe -fsS --noproxy '*' "https://direct.grand-umi.com/backend/ready"
Assert-LastExitCode "正式服低延迟直连 /backend/ready 验证失败。"
$endpointsRaw = & curl.exe -fsS --noproxy '*' "https://ygo.grand-umi.com/network-endpoints.json"
Assert-LastExitCode "正式服 WebSocket 端点清单读取失败。"
try {
  $ready = $readyRaw | ConvertFrom-Json
  $version = $versionRaw | ConvertFrom-Json
  $directReady = $directReadyRaw | ConvertFrom-Json
  $endpoints = $endpointsRaw | ConvertFrom-Json
} catch {
  Die "正式服公网状态返回了无效 JSON：$($_.Exception.Message)"
}
if ($ready.status -ne "ready" -or $ready.storage.healthy -ne $true) {
  Die "正式服主域后端未处于健康就绪状态。"
}
if ($directReady.status -ne "ready" -or $directReady.storage.healthy -ne $true) {
  Die "正式服低延迟直连后端未处于健康就绪状态。"
}
if ($version.commit -ne $localHead) {
  Die "正式服公网版本不一致：期望 $localHead，实际 $($version.commit)。"
}
$enabledEndpoints = @($endpoints.endpoints | Where-Object { $_.enabled } | ForEach-Object { $_.url })
if ($enabledEndpoints.Count -ne 2 -or
    $enabledEndpoints[0] -ne "wss://direct.grand-umi.com/ws" -or
    $enabledEndpoints[1] -ne "wss://ygo.grand-umi.com/ws") {
  Die "正式服 WebSocket 端点顺序不正确：$($enabledEndpoints -join ', ')。"
}

Write-Host "正式服紧急发布并核验成功：$localHead" -ForegroundColor Green
Write-Host "首页 HTTP 200；主域与直连 ready；WebSocket 首选 wss://direct.grand-umi.com/ws。" -ForegroundColor Green
