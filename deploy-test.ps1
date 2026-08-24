# 将当前 main 提交部署到 GrandUMI 测试服。
param(
  [switch]$All,
  [string]$Server = "root@103.146.230.37"
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
Set-Location $repo
. (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")

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
  & $git status --short
  Stop-WithError "存在未提交改动。为避免夹带无关文件，请先完成或移走这些改动。"
}

& $git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { Stop-WithError "拉取 main 失败；必须先解决分支差异。" }
& $git push origin main
if ($LASTEXITCODE -ne 0) { Stop-WithError "推送 main 失败，未部署测试服。" }

$target = (& $git rev-parse HEAD).Trim()

$serverHead = (& $ssh -o BatchMode=yes $Server "git -C /opt/grandumi-test rev-parse HEAD 2>/dev/null || true").Trim()
$hasServerHead = $serverHead -match '^[0-9a-f]{40}$'

$short = $target.Substring(0, 12)
$deployTempDirectory = Get-GrandUmiTempDirectory -Category "Deploy"
$bundle = Join-Path $deployTempDirectory "grandumi-test-$short.bundle"
$remoteBundle = "/tmp/grandumi-test-$short.bundle"
try {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
  if ($hasServerHead) {
    & $git bundle create $bundle main "^$serverHead"
  }
  if (-not $hasServerHead -or $LASTEXITCODE -ne 0) {
    # 新服务器初次部署或远端提交不是当前 main 祖先时传输完整 main。
    & $git bundle create $bundle main
  }
  if ($LASTEXITCODE -ne 0) { Stop-WithError "创建测试服代码包失败。" }
  & $scp -o BatchMode=yes $bundle ($Server + ":" + $remoteBundle)
  if ($LASTEXITCODE -ne 0) { Stop-WithError "上传测试服代码包失败。" }
  if ($hasServerHead) {
    & $ssh -o BatchMode=yes $Server "git -C /opt/grandumi-test fetch '$remoteBundle' '+refs/heads/main:refs/remotes/origin/main' && rm -f '$remoteBundle'"
  } else {
    & $ssh -o BatchMode=yes $Server "mkdir -p /opt/grandumi-test && git -C /opt/grandumi-test init && git -C /opt/grandumi-test fetch '$remoteBundle' '+refs/heads/main:refs/remotes/origin/main' && git -C /opt/grandumi-test checkout --detach '$target' && rm -f '$remoteBundle'"
  }
  if ($LASTEXITCODE -ne 0) { Stop-WithError "测试服导入代码包失败。" }
} finally {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
}

$forceArg = if ($All -or -not $hasServerHead) { "all" } else { "" }
& $ssh -o BatchMode=yes $Server "bash /opt/grandumi-test/ops/server/deploy-test.sh '$target' '$forceArg'"
if ($LASTEXITCODE -ne 0) { Stop-WithError "测试服部署失败，请检查服务器日志。" }

$code = & curl.exe -s --noproxy '*' -o NUL -w "%{http_code}" -L "https://test.grand-umi.com/"
if ($code -ne "200") { Stop-WithError "测试服外网验证失败，HTTP $code。" }
Write-Host "测试服部署成功：$target" -ForegroundColor Green
