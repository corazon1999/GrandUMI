# ============================================================
#  deploy-hk.ps1 — 一键热更香港线上 (ygo.grand-umi.com @ 8.210.155.25)
#  用法:
#    .\deploy-hk.ps1 -Emergency       # 紧急情况下直接发布正式服
#    .\deploy-test.ps1                # 日常改动应先发布测试服
#    .\deploy-hk.ps1 -All             # 强制前后端全量重建
#    .\deploy-hk.ps1 -Server "root@8.210.155.25" # 指定 SSH 服务器
#  流程: 提交 → pull合并协作者改动 → push → SSH增量传输 → 香港重建 → 验证200
#  注: 本文件必须存为 UTF-8 with BOM,否则 PS5.1 按GBK解码中文会语法报错。
# ============================================================
param(
  [string]$Commit = "",
  [switch]$All,
  [switch]$Emergency,
  [string]$Server = "grandumi-hk"
)
$ErrorActionPreference = "Stop"
$SRV  = $Server
$repo = $PSScriptRoot
Set-Location $repo
. (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")

function Die($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

if (-not $Emergency) {
  Die "已启用测试服发布流程。日常发布请运行 .\deploy-test.ps1；只有紧急上线正式服时才可加 -Emergency。"
}

# Codex 自带的 Git 不一定在系统 PATH 中；优先用 PATH，其次自动寻找本机可用版本。
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
$scp = (Get-Command scp.exe -ErrorAction Stop).Source

Write-Host "===== [1/4] 提交本地改动 =====" -ForegroundColor Cyan
$dirty = & $git status --porcelain
if ($dirty) {
  if ($Commit) {
    & $git add -A
    & $git commit -m $Commit
    if ($LASTEXITCODE -ne 0) { Die "git commit 失败,已中止" }
  } else {
    Write-Host "有未提交改动:" -ForegroundColor Yellow
    & $git status --short
    Die "请先提交,或用  .\deploy-hk.ps1 -Commit `"说明`"  自动提交后再热更。"
  }
}

Write-Host "===== [2/4] 同步远端(先合并协作者改动,避免push被拒) =====" -ForegroundColor Cyan
& $git pull --no-rebase --no-edit origin main
if ($LASTEXITCODE -ne 0) { Die "git pull 失败(可能有冲突),请手动解决后重试,本次未部署。" }

Write-Host "===== [3/4] 推送 GitHub =====" -ForegroundColor Cyan
& $git push origin main
if ($LASTEXITCODE -ne 0) { Die "git push 失败,已中止(未部署)。请重试。" }

Write-Host "===== [4/4] SSH增量传输 + 香港热更 + 验证 =====" -ForegroundColor Cyan
$localHead = (& $git rev-parse HEAD).Trim()
$serverHead = (& $ssh -o BatchMode=yes $SRV "git -C /opt/grandumi rev-parse HEAD").Trim()
if ($LASTEXITCODE -ne 0 -or $serverHead -notmatch '^[0-9a-f]{40}$') {
  Die "无法读取香港服务器版本，请检查 SSH 配置。"
}

if ($serverHead -ne $localHead) {
  & $git merge-base --is-ancestor $serverHead $localHead
  if ($LASTEXITCODE -ne 0) {
    Die "香港服务器版本不是当前 main 的祖先，已中止以避免覆盖，请人工检查。"
  }

  $shortHead = $localHead.Substring(0, 12)
  $deployTempDirectory = Get-GrandUmiTempDirectory -Category "Deploy"
  $bundle = Join-Path $deployTempDirectory "grandumi-$shortHead.bundle"
  $remoteBundle = "/tmp/grandumi-$shortHead.bundle"
  try {
    if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
    & $git bundle create $bundle main "^$serverHead"
    if ($LASTEXITCODE -ne 0) { Die "创建 Git 增量包失败。" }
    & $scp -o BatchMode=yes $bundle "${SRV}:$remoteBundle"
    if ($LASTEXITCODE -ne 0) { Die "上传 Git 增量包失败。" }
    & $ssh -o BatchMode=yes $SRV "git -C /opt/grandumi fetch '$remoteBundle' refs/heads/main:refs/remotes/origin/main"
    if ($LASTEXITCODE -ne 0) { Die "香港服务器导入 Git 增量包失败。" }
  } finally {
    if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
  }
}

$arg = if ($All) { "all" } else { "" }
$productionBuildEnvironment = "NEXT_PUBLIC_WS_URL='wss://ygo.grand-umi.com/ws' NEXT_PUBLIC_ASSET_ORIGIN='https://assets.grand-umi.com' NEXT_PUBLIC_GRANDUMI_COMMIT='$localHead' CARD_BACK_API_URL='http://127.0.0.1:8080'"
& $ssh -o BatchMode=yes $SRV "$productionBuildEnvironment bash /opt/grandumi/deploy.sh $arg"
if ($LASTEXITCODE -ne 0) { Die "香港 deploy.sh 执行报错,请查香港日志。" }

$deployedHead = (& $ssh -o BatchMode=yes $SRV "git -C /opt/grandumi rev-parse HEAD").Trim()
if ($deployedHead -ne $localHead) { Die "香港版本校验失败：期望 $localHead，实际 $deployedHead" }

# 用 curl.exe --noproxy 绕过本机代理(127.0.0.1:9098),否则代理会把直连香港的请求误判为失败
$code = & curl.exe -s --noproxy '*' -o NUL -w "%{http_code}" -L "https://ygo.grand-umi.com/"
if ($code -eq "200") {
  Write-Host "线上首页 HTTP 200 ✓ 部署成功" -ForegroundColor Green
} else {
  Write-Host "线上验证: HTTP $code (非200,检查香港服务)" -ForegroundColor Red
}
