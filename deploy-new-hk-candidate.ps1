param(
  [string]$Server = "root@103.146.230.37",
  [string]$Commit = ""
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
Set-Location $repo
. (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")

function Stop-WithError([string]$Message) { Write-Host $Message -ForegroundColor Red; exit 1 }
if ($Server -ne "root@103.146.230.37") { Stop-WithError "安全检查失败：候选部署只允许 root@103.146.230.37。" }
if ($Server -match '8\.210\.155\.25') { Stop-WithError "安全检查失败：不得对旧正式服执行候选部署。" }

$git = (Get-Command git.exe -ErrorAction Stop).Source
$ssh = (Get-Command ssh.exe -ErrorAction Stop).Source
$scp = (Get-Command scp.exe -ErrorAction Stop).Source
if ((& $git branch --show-current).Trim() -ne "main") { Stop-WithError "候选部署必须从 main 分支执行。" }
if (& $git status --porcelain) { Stop-WithError "工作区存在未提交改动，已停止候选部署。" }
$target = if ($Commit) { (& $git rev-parse $Commit).Trim() } else { (& $git rev-parse HEAD).Trim() }
if ($target -notmatch '^[0-9a-f]{40}$') { Stop-WithError "无法解析部署提交。" }

& $ssh -o BatchMode=yes $Server "mkdir -p /opt/grandumi-candidate && if [ ! -d /opt/grandumi-candidate/.git ]; then git init -b main /opt/grandumi-candidate; fi"
if ($LASTEXITCODE -ne 0) { Stop-WithError "无法初始化候选服仓库。" }

$tempDir = Get-GrandUmiTempDirectory -Category "Deploy"
$short = $target.Substring(0, 12)
$bundle = Join-Path $tempDir "grandumi-candidate-$short.bundle"
$remoteBundle = "/tmp/grandumi-candidate-$short.bundle"
try {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
  $serverHead = (& $ssh -o BatchMode=yes $Server "git -C /opt/grandumi-candidate rev-parse refs/remotes/origin/main 2>/dev/null || true").Trim()
  if ($serverHead -eq $target) {
    Write-Host "候选服代码已是目标提交，跳过代码包上传。" -ForegroundColor Yellow
  } elseif ($serverHead -match '^[0-9a-f]{40}$') {
    & $git bundle create $bundle main "^$serverHead"
  } else {
    & $git bundle create $bundle main
  }
  if ($LASTEXITCODE -ne 0) { Stop-WithError "创建候选服代码包失败。" }
  if (Test-Path -LiteralPath $bundle) {
    & $scp -o BatchMode=yes $bundle ($Server + ":" + $remoteBundle)
    if ($LASTEXITCODE -ne 0) { Stop-WithError "上传候选服代码包失败。" }
    & $ssh -o BatchMode=yes $Server "git -C /opt/grandumi-candidate fetch '$remoteBundle' 'refs/heads/main:refs/remotes/origin/main' && rm -f '$remoteBundle' && git -C /opt/grandumi-candidate checkout --detach '$target'"
    if ($LASTEXITCODE -ne 0) { Stop-WithError "候选服导入代码包失败。" }
  }
} finally {
  if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
}

& $ssh -o BatchMode=yes $Server "GRANDUMI_CANDIDATE_IP=103.146.230.37 bash /opt/grandumi-candidate/ops/server/bootstrap-grandumi-candidate.sh"
if ($LASTEXITCODE -ne 0) { Stop-WithError "候选服基础环境初始化失败。" }
& $ssh -o BatchMode=yes $Server "GRANDUMI_CANDIDATE_IP=103.146.230.37 bash /opt/grandumi-candidate/ops/server/deploy-grandumi-candidate.sh '$target'"
if ($LASTEXITCODE -ne 0) { Stop-WithError "候选服应用部署失败。" }
& $ssh -o BatchMode=yes $Server "bash /opt/grandumi-candidate/ops/server/enable-grandumi-candidate-tls.sh"
if ($LASTEXITCODE -ne 0) { Stop-WithError "候选域名 HTTPS/WSS 配置失败。" }

$homeCode = & curl.exe -s -L --noproxy '*' -o NUL -w "%{http_code}" "https://candidate.grand-umi.com/"
$readyCode = & curl.exe -s --noproxy '*' -o NUL -w "%{http_code}" "https://candidate.grand-umi.com/backend/ready"
if ($homeCode -ne "200" -or $readyCode -ne "200") { Stop-WithError "候选服外网验证失败：首页=$homeCode，就绪=$readyCode。" }
Write-Host "新香港候选服部署成功：$target" -ForegroundColor Green
