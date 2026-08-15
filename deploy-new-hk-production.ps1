param(
  [string]$Server = "root@103.146.230.37",
  [string]$Commit = ""
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
Set-Location $repo

function Stop-WithError([string]$Message) { Write-Host $Message -ForegroundColor Red; exit 1 }
if ($Server -ne "root@103.146.230.37") { Stop-WithError "安全检查失败：新正式服部署只允许 root@103.146.230.37。" }
if ((git branch --show-current).Trim() -ne "main") { Stop-WithError "新正式服部署必须从 main 分支执行。" }
if (git status --porcelain) { Stop-WithError "工作区存在未提交改动，已停止新正式服部署。" }
$target = if ($Commit) { (git rev-parse $Commit).Trim() } else { (git rev-parse HEAD).Trim() }
if ($target -notmatch '^[0-9a-f]{40}$') { Stop-WithError "无法解析部署提交。" }

$ssh = (Get-Command ssh.exe -ErrorAction Stop).Source
$scp = (Get-Command scp.exe -ErrorAction Stop).Source
& $ssh -o BatchMode=yes $Server "mkdir -p /opt/grandumi && if [ ! -d /opt/grandumi/.git ]; then git init -b main /opt/grandumi; fi"
if ($LASTEXITCODE -ne 0) { Stop-WithError "无法初始化新正式服仓库。" }

$tempDir = & {
  . (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")
  Get-GrandUmiTempDirectory -Category "Deploy"
}
$short = $target.Substring(0, 12)
$bundle = Join-Path $tempDir "grandumi-production-$short.bundle"
$remoteBundle = "/tmp/grandumi-production-$short.bundle"
try {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
  git bundle create $bundle main
  if ($LASTEXITCODE -ne 0) { Stop-WithError "创建新正式服代码包失败。" }
  & $scp -o BatchMode=yes $bundle ($Server + ":" + $remoteBundle)
  if ($LASTEXITCODE -ne 0) { Stop-WithError "上传新正式服代码包失败。" }
  & $ssh -o BatchMode=yes $Server "git -C /opt/grandumi fetch '$remoteBundle' 'refs/heads/main:refs/remotes/origin/main' && rm -f '$remoteBundle'"
  if ($LASTEXITCODE -ne 0) { Stop-WithError "新正式服导入代码包失败。" }
} finally {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
}

$remoteStageRoot = "/opt/grandumi-stage-" + $target.Substring(0, 12)
$remoteStageCommand = "set -Eeuo pipefail; trap 'git -C /opt/grandumi worktree remove --force $remoteStageRoot >/dev/null 2>&1 || true' EXIT; git -C /opt/grandumi worktree add --detach $remoteStageRoot '$target' >/dev/null; GRANDUMI_PRODUCTION_IP=103.146.230.37 bash $remoteStageRoot/ops/server/bootstrap-grandumi-production.sh; GRANDUMI_PRODUCTION_IP=103.146.230.37 bash $remoteStageRoot/ops/server/stage-grandumi-production.sh '$target'"
& $ssh -o BatchMode=yes $Server $remoteStageCommand
if ($LASTEXITCODE -ne 0) { Stop-WithError "新正式服预构建失败。" }

& $ssh -o BatchMode=yes $Server "grep -Fxq '$target' /var/lib/grandumi-production-staged"
if ($LASTEXITCODE -ne 0) { Stop-WithError "新正式服预构建版本校验失败。" }
Write-Host "新正式服版本预构建成功（尚未切流）：$target" -ForegroundColor Green
