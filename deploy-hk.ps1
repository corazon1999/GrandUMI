# ============================================================
#  deploy-hk.ps1 — 一键热更香港线上 (grand-umi.com @ 8.210.155.25)
#  用法:
#    .\deploy-hk.ps1                  # 推已提交的代码并触发香港重建
#    .\deploy-hk.ps1 -Commit "说明"   # 先提交当前改动再推+热更
#    .\deploy-hk.ps1 -All             # 强制前后端全量重建
#  流程: 提交 → pull合并协作者改动 → push(失败即中止) → ssh触发香港deploy.sh → 验证200
#  注: 本文件必须存为 UTF-8 with BOM,否则 PS5.1 按GBK解码中文会语法报错。
# ============================================================
param(
  [string]$Commit = "",
  [switch]$All
)
$ErrorActionPreference = "Stop"
$SRV  = "root@8.210.155.25"
$repo = "D:\Self\GrandUMI"
Set-Location $repo

function Die($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

Write-Host "===== [1/4] 提交本地改动 =====" -ForegroundColor Cyan
$dirty = git status --porcelain
if ($dirty) {
  if ($Commit) {
    git add -A
    git commit -m $Commit
    if ($LASTEXITCODE -ne 0) { Die "git commit 失败,已中止" }
  } else {
    Write-Host "有未提交改动:" -ForegroundColor Yellow
    git status --short
    Die "请先提交,或用  .\deploy-hk.ps1 -Commit `"说明`"  自动提交后再热更。"
  }
}

Write-Host "===== [2/4] 同步远端(先合并协作者改动,避免push被拒) =====" -ForegroundColor Cyan
git pull --no-rebase --no-edit origin main
if ($LASTEXITCODE -ne 0) { Die "git pull 失败(可能有冲突),请手动解决后重试,本次未部署。" }

Write-Host "===== [3/4] 推送 GitHub =====" -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) { Die "git push 失败,已中止(未部署)。请重试。" }

Write-Host "===== [4/4] 触发香港热更 + 验证 =====" -ForegroundColor Cyan
$arg = if ($All) { "all" } else { "" }
ssh $SRV "bash /opt/grandumi/deploy.sh $arg"
if ($LASTEXITCODE -ne 0) { Die "香港 deploy.sh 执行报错,请查香港日志。" }

# 用 curl.exe --noproxy 绕过本机代理(127.0.0.1:9098),否则代理会把直连香港的请求误判为失败
$code = & curl.exe -s --noproxy '*' -o NUL -w "%{http_code}" -L "https://grand-umi.com/"
if ($code -eq "200") {
  Write-Host "线上首页 HTTP 200 ✓ 部署成功" -ForegroundColor Green
} else {
  Write-Host "线上验证: HTTP $code (非200,检查香港服务)" -ForegroundColor Red
}
