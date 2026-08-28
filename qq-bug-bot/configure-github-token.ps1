param(
    [string]$Server = "root@103.146.230.37",
    [string]$RemoteDir = "/opt/qq-bug-bot",
    [string]$Repository = "corazon1999/GrandUMI"
)

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "GrandUMI GitHub Token 验证与配置"

if ($RemoteDir -notmatch '^/[A-Za-z0-9._/-]+$') {
    throw "远程目录格式不安全：$RemoteDir"
}
if ($Server -notmatch '^[A-Za-z0-9._-]+@[A-Za-z0-9.:-]+$') {
    throw "SSH 服务器格式不安全：$Server"
}

$secure = Read-Host "请粘贴新生成的 GitHub Token 后按回车" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

if ($token -notmatch '^(github_pat_|ghp_)[A-Za-z0-9_]+$' -or $token.Length -lt 40) {
    $token = $null
    throw "Token 格式检查失败，请确认复制的是生成后仅显示一次的完整字符串。"
}

$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "GrandUMI-Deploy"
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $repoUrl = "https://api.github.com/repos/$Repository"
    $issuesUrl = "$repoUrl/issues?per_page=1"
    $repoResponse = Invoke-WebRequest -UseBasicParsing -Uri $repoUrl -Headers $headers
    $issueResponse = Invoke-WebRequest -UseBasicParsing -Uri $issuesUrl -Headers $headers
}
catch {
    $status = $_.Exception.Response.StatusCode.value__
    if (-not $status) { $status = "无法连接" }
    $token = $null
    $headers = $null
    throw "GitHub 验证失败：HTTP $status。Token 未写入服务器。"
}

if ($repoResponse.StatusCode -ne 200 -or $issueResponse.StatusCode -ne 200) {
    $token = $null
    $headers = $null
    throw "GitHub 仓库或 Issues 读取验证未通过，Token 未写入服务器。"
}

Write-Host "GitHub 本机验证通过，正在安全写入服务器……" -ForegroundColor Cyan
$sshPath = (Get-Command ssh.exe -ErrorAction Stop).Source
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $sshPath
$startInfo.Arguments = "-o BatchMode=yes $Server $RemoteDir/install-github-token.sh"
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
[void]$process.Start()
$process.StandardInput.NewLine = "`n"
$process.StandardInput.WriteLine($token)
$process.StandardInput.Close()
$token = $null
$headers = $null
$secure.Dispose()

$sshOutput = $process.StandardOutput.ReadToEnd()
$sshError = $process.StandardError.ReadToEnd()
$process.WaitForExit()

if ($process.ExitCode -ne 0 -or $sshOutput -notmatch 'TOKEN_SAVED') {
    throw "GitHub 验证已通过，但写入服务器失败：$sshError"
}

Write-Host "GitHub 验证通过，新 Token 已安全保存。" -ForegroundColor Green
