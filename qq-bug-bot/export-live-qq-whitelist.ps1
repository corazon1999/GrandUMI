[CmdletBinding()]
param(
    [string]$SshTarget = 'root@103.146.230.37',
    [string]$RemoteDir = '/opt/qq-bug-bot',
    [Parameter(DontShow = $true)]
    [switch]$TransportSelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-NativeArguments {
    param([string[]]$Values)

    return (($Values | ForEach-Object {
        if ($_ -notmatch '[\s"]') {
            $_
        }
        else {
            '"' + $_.Replace('"', '\"') + '"'
        }
    }) -join ' ')
}

function Get-SafeDiagnostic {
    param([AllowEmptyString()][string]$Text)

    $safe = ($Text -replace '(?i)(access_token=)[^&\s]+', '$1[已隐藏]')
    $safe = ($safe -replace '(?i)(authorization:\s*bearer\s+)[^\s]+', '$1[已隐藏]')
    $safe = ($safe -replace '[\x00-\x08\x0B\x0C\x0E-\x1F]', ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return '未提供错误详情'
    }
    if ($safe.Length -gt 600) {
        return $safe.Substring(0, 600) + '…'
    }
    return $safe
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function New-RemoteSourcePayload {
    param([Parameter(Mandatory = $true)][byte[]]$SourceBytes)

    if ($SourceBytes.Length -eq 0) {
        throw '远端导出器源码为空。'
    }

    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try {
        $null = $strictUtf8.GetString($SourceBytes)
    }
    catch {
        throw '远端导出器不是有效的 UTF-8 文件。'
    }

    $sourceSha256 = Get-Sha256Hex -Bytes $SourceBytes
    $payloadText = [Convert]::ToBase64String($SourceBytes)
    if ($payloadText -notmatch '^[A-Za-z0-9+/]+={0,2}$') {
        throw '远端导出器 Base64 载荷包含非 ASCII 字符。'
    }
    $payloadBytes = [Text.Encoding]::ASCII.GetBytes($payloadText)
    if ($payloadBytes.Length -ne $payloadText.Length) {
        throw '远端导出器 Base64 载荷无法无损转换为 ASCII。'
    }

    try {
        $roundTripBytes = [Convert]::FromBase64String(
            [Text.Encoding]::ASCII.GetString($payloadBytes)
        )
    }
    catch {
        throw '远端导出器 Base64 本地往返解码失败。'
    }
    $roundTripSha256 = Get-Sha256Hex -Bytes $roundTripBytes
    if ($roundTripBytes.Length -ne $SourceBytes.Length -or $roundTripSha256 -ne $sourceSha256) {
        throw '远端导出器 Base64 本地往返完整性校验失败。'
    }
    for ($index = 0; $index -lt $SourceBytes.Length; $index++) {
        if ($roundTripBytes[$index] -ne $SourceBytes[$index]) {
            throw '远端导出器 Base64 本地往返字节不一致。'
        }
    }

    return [pscustomobject]@{
        PayloadBytes = $payloadBytes
        PayloadLength = $payloadBytes.Length
        SourceByteLength = $SourceBytes.Length
        SourceSha256 = $sourceSha256
        RoundTripSha256 = $roundTripSha256
    }
}

function Invoke-NativeProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [AllowNull()][byte[]]$InputBytes,
        [Parameter(Mandatory = $true)][int]$TimeoutMilliseconds
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = ConvertTo-NativeArguments -Values $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $InputBytes
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    $startInfo.StandardOutputEncoding = $utf8WithoutBom
    $startInfo.StandardErrorEncoding = $utf8WithoutBom
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $standardInput = $null
    $originalConsoleInputEncoding = $null
    try {
        if ($startInfo.RedirectStandardInput) {
            # Windows PowerShell 5.1 在 Start() 内用 Console.InputEncoding 创建
            # StandardInput StreamWriter；必须在启动前临时切换为无 BOM UTF-8。
            $originalConsoleInputEncoding = [Console]::InputEncoding
            [Console]::InputEncoding = $utf8WithoutBom
        }
        try {
            $started = $process.Start()
        }
        finally {
            if ($null -ne $originalConsoleInputEncoding) {
                [Console]::InputEncoding = $originalConsoleInputEncoding
            }
        }
    }
    catch {
        $process.Dispose()
        throw "无法启动所需程序：$FilePath"
    }
    if (-not $started) {
        $process.Dispose()
        throw "无法启动所需程序：$FilePath"
    }
    try {
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        if ($startInfo.RedirectStandardInput) {
            $standardInput = $process.StandardInput
            $inputWriteTask = $standardInput.BaseStream.WriteAsync(
                $InputBytes,
                0,
                $InputBytes.Length
            )
            $remainingMilliseconds = $TimeoutMilliseconds - [int]$stopwatch.ElapsedMilliseconds
            if ($remainingMilliseconds -le 0 -or -not $inputWriteTask.Wait($remainingMilliseconds)) {
                throw "外部命令执行超时（$([Math]::Round($TimeoutMilliseconds / 1000)) 秒）"
            }
            $null = $inputWriteTask.GetAwaiter().GetResult()
            $standardInput.BaseStream.Flush()
            $standardInput.Close()
        }
        $remainingMilliseconds = $TimeoutMilliseconds - [int]$stopwatch.ElapsedMilliseconds
        if ($remainingMilliseconds -le 0 -or -not $process.WaitForExit($remainingMilliseconds)) {
            throw "外部命令执行超时（$([Math]::Round($TimeoutMilliseconds / 1000)) 秒）"
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput
            StandardError = $standardError
        }
    }
    catch {
        $originalError = $_
        try {
            if ($null -ne $standardInput) {
                $standardInput.Close()
            }
        }
        catch {
        }
        try {
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
        catch {
        }
        throw $originalError
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-LocalVerifier {
    param(
        [Parameter(Mandatory = $true)][string]$NodePath,
        [Parameter(Mandatory = $true)][string]$VerifierPath,
        [Parameter(Mandatory = $true)][string]$JsonPath
    )

    $verification = Invoke-NativeProcess `
        -FilePath $NodePath `
        -Arguments @($VerifierPath, $JsonPath) `
        -InputBytes $null `
        -TimeoutMilliseconds 30000
    if ($verification.ExitCode -ne 0) {
        throw (Get-SafeDiagnostic -Text $verification.StandardError)
    }
    try {
        return ($verification.StandardOutput.Trim() | ConvertFrom-Json)
    }
    catch {
        throw '本地 Node 校验器没有返回有效结果。'
    }
}

function Invoke-TransportSelfTest {
    $scriptDirectory = Split-Path -Parent $PSCommandPath
    $remoteExporter = Join-Path $scriptDirectory 'export_live_qq_whitelist.py'
    if (-not [IO.File]::Exists($remoteExporter)) {
        throw "缺少导出组件：$remoteExporter"
    }
    try {
        $node = Get-Command node.exe -CommandType Application -ErrorAction Stop
    }
    catch {
        throw '找不到 Node.js；无法执行传输自检。'
    }

    $sourceBytes = [IO.File]::ReadAllBytes($remoteExporter)
    $transport = New-RemoteSourcePayload -SourceBytes $sourceBytes
    $probeCode = "const c=require('node:crypto'),b=[];process.stdin.on('data',x=>b.push(x)).on('end',()=>{const p=Buffer.concat(b),s=Buffer.from(p.toString('ascii'),'base64');process.stdout.write(JSON.stringify({payloadAscii:p.every(x=>x<128),decodedSha256:c.createHash('sha256').update(s).digest('hex'),decodedLength:s.length}))})"
    $probeResult = Invoke-NativeProcess `
        -FilePath $node.Source `
        -Arguments @('-e', $probeCode) `
        -InputBytes $transport.PayloadBytes `
        -TimeoutMilliseconds 30000
    if ($probeResult.ExitCode -ne 0) {
        throw (Get-SafeDiagnostic -Text $probeResult.StandardError)
    }
    try {
        $probe = $probeResult.StandardOutput | ConvertFrom-Json
    }
    catch {
        throw '传输自检子进程没有返回有效结果。'
    }
    if (
        $probe.payloadAscii -ne $true `
        -or [int]$probe.decodedLength -ne $transport.SourceByteLength `
        -or [string]$probe.decodedSha256 -ne $transport.SourceSha256
    ) {
        throw (
            '传输自检子进程收到的源码与本地 UTF-8 原字节不一致' +
            "（ASCII=$($probe.payloadAscii)，长度=$($probe.decodedLength)/$($transport.SourceByteLength)，" +
            "SHA-256=$($probe.decodedSha256)/$($transport.SourceSha256)）。"
        )
    }

    return [pscustomobject]@{
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        powershellMajor = $PSVersionTable.PSVersion.Major
        payloadAscii = [bool]$probe.payloadAscii
        payloadLength = $transport.PayloadLength
        sourceByteLength = $transport.SourceByteLength
        sourceSha256 = $transport.SourceSha256
        roundTripSha256 = $transport.RoundTripSha256
        childDecodedSha256 = [string]$probe.decodedSha256
    }
}

if ($TransportSelfTest) {
    try {
        Invoke-TransportSelfTest | ConvertTo-Json -Compress
        exit 0
    }
    catch {
        Write-Error (Get-SafeDiagnostic -Text $_.Exception.Message)
        exit 1
    }
}

$tempFile = $null
$finalPath = $null
$finalVerified = $false
$finalCreatedByThisRun = $false

try {
    if ($SshTarget -notmatch '^root@[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$' -or $SshTarget.Contains('..')) {
        throw 'SSH 目标格式无效；必须是 root@主机名或 root@IP。'
    }
    if ($RemoteDir -notmatch '^/[A-Za-z0-9._/-]+$' -or $RemoteDir.Contains('..') -or $RemoteDir.Contains('//')) {
        throw '远端目录格式无效；只允许不含空格与上级跳转的绝对路径。'
    }

    $scriptDirectory = Split-Path -Parent $PSCommandPath
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
    $tempHelper = Join-Path $repositoryRoot 'ops\windows\GrandUmiTemp.ps1'
    $remoteExporter = Join-Path $scriptDirectory 'export_live_qq_whitelist.py'
    $localVerifier = Join-Path $scriptDirectory 'verify_qq_whitelist_export.mjs'
    foreach ($requiredFile in @($tempHelper, $remoteExporter, $localVerifier)) {
        if (-not [IO.File]::Exists($requiredFile)) {
            throw "缺少导出组件：$requiredFile"
        }
    }

    . $tempHelper
    $tempDirectory = Get-GrandUmiTempDirectory -Category 'QqWhitelistExport'
    $tempDirectory = [IO.Path]::GetFullPath($tempDirectory)
    if (-not $tempDirectory.StartsWith('E:\GrandUMI-Temp\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "临时目录不在 E:\GrandUMI-Temp 下：$tempDirectory"
    }
    $drive = Get-PSDrive -Name E -ErrorAction Stop
    if ($drive.Free -lt 10MB) {
        throw 'E 盘剩余空间不足 10 MiB，拒绝开始导出。'
    }
    $tempFile = Join-Path $tempDirectory ("qq-whitelist-{0}.json" -f [Guid]::NewGuid().ToString('N'))

    try {
        $ssh = Get-Command ssh.exe -CommandType Application -ErrorAction Stop
    }
    catch {
        throw '找不到 Windows OpenSSH 客户端 ssh.exe。'
    }
    try {
        $node = Get-Command node.exe -CommandType Application -ErrorAction Stop
    }
    catch {
        throw '找不到 Node.js；无法使用游戏白名单解析器验证导出。'
    }
    $remoteSourceBytes = [IO.File]::ReadAllBytes($remoteExporter)
    $remoteTransport = New-RemoteSourcePayload -SourceBytes $remoteSourceBytes
    $remotePythonBootstrap = 'import base64,hashlib,sys; source=base64.b64decode(sys.stdin.buffer.read(),validate=True); actual=hashlib.sha256(source).hexdigest(); sys.exit("source hash mismatch") if actual != sys.argv[1] else exec(compile(source,"<grandumi-live-export>","exec"))'
    $remoteCommand = "cd -- '$RemoteDir' && docker compose exec -T bug-bot python -c '$remotePythonBootstrap' '$($remoteTransport.SourceSha256)'"
    $sshArguments = @(
        '-o', 'BatchMode=yes',
        '-o', 'StrictHostKeyChecking=yes',
        '-o', 'ConnectTimeout=10',
        '-o', 'ConnectionAttempts=1',
        '-o', 'ServerAliveInterval=5',
        '-o', 'ServerAliveCountMax=2',
        $SshTarget,
        $remoteCommand
    )

    Write-Host '正在通过现有服务器与 OneBot 实时拉取 GrandUMI测试群（297542853）…' -ForegroundColor Cyan
    $remoteResult = Invoke-NativeProcess `
        -FilePath $ssh.Source `
        -Arguments $sshArguments `
        -InputBytes $remoteTransport.PayloadBytes `
        -TimeoutMilliseconds 70000
    if ($remoteResult.ExitCode -ne 0) {
        throw ("实时拉取失败：{0}" -f (Get-SafeDiagnostic -Text $remoteResult.StandardError))
    }
    if ([string]::IsNullOrWhiteSpace($remoteResult.StandardOutput)) {
        throw '远端导出器没有返回白名单数据。'
    }

    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    $tempStream = New-Object System.IO.FileStream(
        $tempFile,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None
    )
    try {
        $bytes = $utf8WithoutBom.GetBytes($remoteResult.StandardOutput.Trim() + [Environment]::NewLine)
        $tempStream.Write($bytes, 0, $bytes.Length)
        $tempStream.Flush($true)
    }
    finally {
        $tempStream.Dispose()
    }

    $temporaryVerification = Invoke-LocalVerifier `
        -NodePath $node.Source `
        -VerifierPath $localVerifier `
        -JsonPath $tempFile
    try {
        $fetchedAt = [DateTimeOffset]::ParseExact(
            [string]$temporaryVerification.fetchedAt,
            "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
            [Globalization.CultureInfo]::InvariantCulture
        )
    }
    catch {
        throw '远端实时拉取时间无法解析。'
    }
    $snapshotAge = [DateTimeOffset]::Now - $fetchedAt
    if ($snapshotAge.TotalSeconds -lt -60 -or $snapshotAge.TotalMinutes -gt 5) {
        throw '远端返回的群成员快照不是最近 5 分钟内的实时数据。'
    }

    for ($nameAttempt = 1; $nameAttempt -le 100; $nameAttempt++) {
        $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss-fff')
        $fileName = "qq-whitelist-297542853-$timestamp-live.json"
        $finalPath = Join-Path $repositoryRoot $fileName
        try {
            [IO.File]::Copy($tempFile, $finalPath, $false)
            $finalCreatedByThisRun = $true
            break
        }
        catch [IO.IOException] {
            if (-not [IO.File]::Exists($finalPath)) {
                throw
            }
            Start-Sleep -Milliseconds 2
        }
    }
    if (-not $finalCreatedByThisRun) {
        throw '无法分配唯一的白名单导出文件名。'
    }

    try {
        $finalVerification = Invoke-LocalVerifier `
            -NodePath $node.Source `
            -VerifierPath $localVerifier `
            -JsonPath $finalPath
        if ($finalVerification.sha256 -ne $temporaryVerification.sha256) {
            throw '最终文件与已校验的 E 盘中转文件 SHA-256 不一致。'
        }
        $finalVerified = $true
    }
    catch {
        if ($finalCreatedByThisRun -and [IO.File]::Exists($finalPath)) {
            Remove-Item -LiteralPath $finalPath -Force
        }
        throw
    }

    try {
        Start-Process -FilePath explorer.exe -ArgumentList "/select,`"$finalPath`""
    }
    catch {
        throw '白名单已经校验并保存，但无法启动资源管理器选中文件。'
    }

    Write-Host ''
    Write-Host '实时白名单导出成功。' -ForegroundColor Green
    Write-Host "文件路径：$finalPath"
    Write-Host "群成员人数：$($finalVerification.memberCount) 人"
    Write-Host "实时拉取时间：$($finalVerification.fetchedAt)"
    Write-Host "SHA-256：$($finalVerification.sha256)"
    exit 0
}
catch {
    $message = Get-SafeDiagnostic -Text $_.Exception.Message
    Write-Host ''
    Write-Host "实时白名单导出失败：$message" -ForegroundColor Red
    if ($finalCreatedByThisRun -and $finalPath -and -not $finalVerified -and [IO.File]::Exists($finalPath)) {
        Remove-Item -LiteralPath $finalPath -Force
    }
    exit 1
}
finally {
    if ($tempFile -and [IO.File]::Exists($tempFile)) {
        Remove-Item -LiteralPath $tempFile -Force
    }
}
