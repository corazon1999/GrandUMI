param(
    [string]$Version = "",
    [switch]$Rollback,
    [string]$ServiceName = "GrandUMI-Backend",
    [int]$Port = 8080,
    [string]$ReleasesRoot = "",
    [string]$HealthUrl = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $ReleasesRoot) {
    $ReleasesRoot = Join-Path $repo "服务端WebSocket\releases"
}
$ReleasesRoot = [IO.Path]::GetFullPath($ReleasesRoot)
$statePath = Join-Path $ReleasesRoot "service-release-state.json"
if (-not $HealthUrl) { $HealthUrl = "http://127.0.0.1:$Port/ready" }

$nssm = (Get-Command nssm.exe -ErrorAction Stop).Source
$service = Get-Service -Name $ServiceName -ErrorAction Stop
$old = [ordered]@{
    version = "unknown"
    application = (& $nssm get $ServiceName Application).Trim()
    directory = (& $nssm get $ServiceName AppDirectory).Trim()
    parameters = (& $nssm get $ServiceName AppParameters).Trim()
}

if ($Rollback) {
    if (-not (Test-Path -LiteralPath $statePath)) { throw "没有可回滚的服务版本记录。" }
    $saved = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    $targetApplication = [string]$saved.previous.application
    $targetDirectory = [string]$saved.previous.directory
    $targetParameters = [string]$saved.previous.parameters
    $targetVersion = [string]$saved.previous.version
}
else {
    if (-not $Version) { throw "切换发布时必须提供 -Version。" }
    if ($Version -notmatch '^[0-9A-Za-z._-]+$') { throw "版本号格式无效。" }
    $targetDirectory = Join-Path $ReleasesRoot $Version
    $targetApplication = Join-Path $targetDirectory "GrandUMIServer.exe"
    $targetParameters = [string]$Port
    $targetVersion = $Version
    if (-not (Test-Path -LiteralPath $targetApplication)) {
        throw "目标发布包不存在：$targetApplication"
    }
}

function Set-ServiceTarget([string]$Application, [string]$Directory, [string]$Parameters) {
    & $nssm set $ServiceName Application $Application | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "设置 NSSM Application 失败。" }
    & $nssm set $ServiceName AppDirectory $Directory | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "设置 NSSM AppDirectory 失败。" }
    & $nssm set $ServiceName AppParameters $Parameters | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "设置 NSSM AppParameters 失败。" }
}

function Restart-And-WaitReady {
    & $nssm stop $ServiceName confirm | Out-Null
    & $nssm start $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    return $false
}

try {
    Set-ServiceTarget $targetApplication $targetDirectory $targetParameters
    if (-not (Restart-And-WaitReady)) { throw "新版本未通过就绪检查。" }

    [ordered]@{
        current = [ordered]@{
            version = $targetVersion
            application = $targetApplication
            directory = $targetDirectory
            parameters = $targetParameters
        }
        previous = $old
        switchedAtUtc = [DateTime]::UtcNow.ToString("O")
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
    Write-Host "后端已切换至版本 $targetVersion，并通过就绪检查。" -ForegroundColor Green
}
catch {
    Write-Warning "切换失败，正在恢复原服务配置。"
    Set-ServiceTarget $old.application $old.directory $old.parameters
    [void](Restart-And-WaitReady)
    throw
}
