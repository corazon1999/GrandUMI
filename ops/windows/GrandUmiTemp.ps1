Set-StrictMode -Version Latest

function Get-GrandUmiTempDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[A-Za-z0-9._-]+$')]
        [string]$Category
    )

    $driveRoot = 'E:\'
    $tempRoot = 'E:\GrandUMI-Temp'

    if (-not [IO.Directory]::Exists($driveRoot)) {
        throw 'E 盘不可用，拒绝在 C 盘或系统临时目录创建 GrandUMI 临时文件。'
    }

    $directory = [IO.Path]::GetFullPath([IO.Path]::Combine($tempRoot, $Category))
    if (-not $directory.StartsWith($driveRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "GrandUMI 临时目录必须位于 E 盘，实际解析为：$directory"
    }

    [IO.Directory]::CreateDirectory($directory) | Out-Null
    return $directory
}
