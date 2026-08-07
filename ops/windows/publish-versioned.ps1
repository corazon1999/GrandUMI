param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$ReleasesRoot = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repo "服务端WebSocket\GrandUMIServer.csproj"
if (-not $ReleasesRoot) {
    $ReleasesRoot = Join-Path $repo "服务端WebSocket\releases"
}
$ReleasesRoot = [IO.Path]::GetFullPath($ReleasesRoot)

$commit = (& git -C $repo rev-parse HEAD).Trim()
$workingTreeDirty = [bool](& git -C $repo status --porcelain)
if (-not $Version) {
    $Version = "{0}-{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $commit.Substring(0, 12)
}
if ($Version -notmatch '^[0-9A-Za-z._-]+$') {
    throw "版本号只能包含字母、数字、点、下划线和连字符。"
}

New-Item -ItemType Directory -Path $ReleasesRoot -Force | Out-Null
$target = Join-Path $ReleasesRoot $Version
if (Test-Path -LiteralPath $target) {
    throw "版本目录已存在：$target"
}

$staging = Join-Path $ReleasesRoot (".staging-{0}-{1}" -f $Version, $PID)
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

try {
    & dotnet publish $project -c $Configuration -o $staging --nologo `
        "-p:InformationalVersion=1.0.0+$commit" `
        "-p:IncludeSourceRevisionInInformationalVersion=false"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败。" }

    $executable = Join-Path $staging "GrandUMIServer.exe"
    $assembly = Join-Path $staging "GrandUMIServer.dll"
    if (-not (Test-Path -LiteralPath $executable) -or -not (Test-Path -LiteralPath $assembly)) {
        throw "发布产物不完整。"
    }

    [ordered]@{
        version = $Version
        commit = $commit
        workingTreeDirty = $workingTreeDirty
        builtAtUtc = [DateTime]::UtcNow.ToString("O")
        configuration = $Configuration
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging "release.json") -Encoding utf8

    Move-Item -LiteralPath $staging -Destination $target
    $latestNext = Join-Path $ReleasesRoot "latest-built.next"
    Set-Content -LiteralPath $latestNext -Value $Version -Encoding ascii
    Move-Item -LiteralPath $latestNext -Destination (Join-Path $ReleasesRoot "latest-built.txt") -Force
    Write-Host "版本化发布包已生成：$target" -ForegroundColor Green
    Write-Output $target
}
catch {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    throw
}
