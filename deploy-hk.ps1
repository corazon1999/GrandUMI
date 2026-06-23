# ============================================================
#  deploy-hk.ps1 — 一键热更香港线上 (grand-umi.com @ 8.210.155.25)
#  用法:
#    .\deploy-hk.ps1                  # 推已提交的代码并触发香港重建
#    .\deploy-hk.ps1 -Commit "修复xx" # 先把当前改动提交(消息),再推+热更
#    .\deploy-hk.ps1 -All             # 强制前后端全量重建(不看diff)
#  说明:走 GitHub 中转(push origin main)→ ssh 触发香港 deploy.sh 按改动只重建对应侧。
# ============================================================
param(
  [string]$Commit = "",
  [switch]$All
)
$ErrorActionPreference = "Stop"
$SRV  = "root@8.210.155.25"
$repo = "D:\Self\GrandUMI"
Set-Location $repo

Write-Host "===== [1/3] 检查并推送代码 =====" -ForegroundColor Cyan
$dirty = git status --porcelain
if ($dirty) {
  if ($Commit) {
    Write-Host "提交本地改动: $Commit"
    git add -A
    git commit -m $Commit
  } else {
    Write-Host "有未提交改动:" -ForegroundColor Yellow
    git status --short
    Write-Host "请先提交,或用  .\deploy-hk.ps1 -Commit `"提交说明`"  自动提交后再热更。" -ForegroundColor Yellow
    exit 1
  }
}
git push origin main

Write-Host "===== [2/3] 触发香港机热更 =====" -ForegroundColor Cyan
$arg = if ($All) { "all" } else { "" }
ssh $SRV "bash /opt/grandumi/deploy.sh $arg"

Write-Host "===== [3/3] 线上验证 =====" -ForegroundColor Cyan
# 用 curl.exe --noproxy 绕过本机代理(127.0.0.1:9098),否则代理会把直连香港的请求误判为失败
$code = & curl.exe -s --noproxy '*' -o NUL -w "%{http_code}" -L "https://grand-umi.com/"
if ($code -eq "200") {
  Write-Host "线上首页 HTTP 200 ✓" -ForegroundColor Green
} else {
  Write-Host "线上验证: HTTP $code (非200,检查香港服务)" -ForegroundColor Red
}
