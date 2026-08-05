# 批准当前测试服版本；服务器将在下一个北京时间零点自动发布。
param(
  [string]$Server = "root@8.210.155.25"
)

$ErrorActionPreference = "Stop"
$ssh = (Get-Command ssh.exe -ErrorAction Stop).Source
$command = @'
set -eu
state=/var/lib/grandumi-release
test -s "$state/test-deployed"
commit=$(tr -d '\r\n' < "$state/test-deployed")
echo "$commit" | grep -Eq '^[0-9a-f]{40}$'
echo "$commit" > "$state/approved.next"
mv "$state/approved.next" "$state/approved"
echo "$commit"
'@
$approved = (& $ssh -o BatchMode=yes $Server $command).Trim()
if ($LASTEXITCODE -ne 0 -or $approved -notmatch '^[0-9a-f]{40}$') {
  Write-Host "批准失败，请确认测试服已经成功部署。" -ForegroundColor Red
  exit 1
}
Write-Host "已批准版本 $approved，将在北京时间下一个零点自动发布。" -ForegroundColor Green
