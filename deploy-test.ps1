# 将当前 main 提交部署到 GrandUMI 测试服。
param(
  [string]$Commit = "",
  [switch]$All,
  [string]$Server = "root@8.210.155.25"
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
Set-Location $repo

function Stop-WithError([string]$Message) {
  Write-Host $Message -ForegroundColor Red
  exit 1
}

$gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
if (-not $gitCommand) { Stop-WithError "未找到 git.exe。" }
$git = $gitCommand.Source
$ssh = (Get-Command ssh.exe -ErrorAction Stop).Source
$scp = (Get-Command scp.exe -ErrorAction Stop).Source

$branch = (& $git branch --show-current).Trim()
if ($branch -ne "main") { Stop-WithError "测试服发布必须从 main 分支执行，当前分支为 $branch。" }

$dirty = & $git status --porcelain
if ($dirty) {
  if (-not $Commit) {
    & $git status --short
    Stop-WithError "存在未提交改动。请先自行提交，或明确使用 -Commit 参数。"
  }
  & $git add -A
  & $git commit -m $Commit
  if ($LASTEXITCODE -ne 0) { Stop-WithError "提交失败，已停止部署。" }
}

& $git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { Stop-WithError "拉取 main 失败；必须先解决分支差异。" }
& $git push origin main
if ($LASTEXITCODE -ne 0) { Stop-WithError "推送 main 失败，未部署测试服。" }

$target = (& $git rev-parse HEAD).Trim()
$serverHead = (& $ssh -o BatchMode=yes $Server "git -C /opt/grandumi-test rev-parse HEAD").Trim()
if ($LASTEXITCODE -ne 0 -or $serverHead -notmatch '^[0-9a-f]{40}$') {
  Stop-WithError "无法读取测试服版本。"
}

$short = $target.Substring(0, 12)
$bundle = Join-Path ([IO.Path]::GetTempPath()) "grandumi-test-$short.bundle"
$remoteBundle = "/tmp/grandumi-test-$short.bundle"
try {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
  & $git bundle create $bundle main "^$serverHead"
  if ($LASTEXITCODE -ne 0) {
    # 初次部署或非祖先时传输完整 main。
    & $git bundle create $bundle main
  }
  if ($LASTEXITCODE -ne 0) { Stop-WithError "创建测试服代码包失败。" }
  & $scp -o BatchMode=yes $bundle ($Server + ":" + $remoteBundle)
  if ($LASTEXITCODE -ne 0) { Stop-WithError "上传测试服代码包失败。" }
  & $ssh -o BatchMode=yes $Server "git -C /opt/grandumi-test fetch '$remoteBundle' '+refs/heads/main:refs/remotes/origin/main' && rm -f '$remoteBundle'"
  if ($LASTEXITCODE -ne 0) { Stop-WithError "测试服导入代码包失败。" }
} finally {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
}

$forceArg = if ($All) { "all" } else { "" }
& $ssh -o BatchMode=yes $Server "bash /opt/grandumi-test/deploy.sh '$target' '$forceArg'"
if ($LASTEXITCODE -ne 0) { Stop-WithError "测试服部署失败，请检查服务器日志。" }

$code = & curl.exe -s --noproxy '*' -o NUL -w "%{http_code}" -L "https://test.grand-umi.com/"
if ($code -ne "200") { Stop-WithError "测试服外网验证失败，HTTP $code。" }
Write-Host "测试服部署成功：$target" -ForegroundColor Green
