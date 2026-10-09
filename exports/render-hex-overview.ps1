param(
    [string]$CatalogPath = (Join-Path $PSScriptRoot "..\服务端WebSocket\Game\Hex\HexCatalog.cs"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "hex-overview-current.png")
)

$ErrorActionPreference = 'Stop'

function Escape-Xml([string]$Value) {
    [System.Security.SecurityElement]::Escape($Value)
}

function Wrap-Text([string]$Value, [int]$Width = 38) {
    for ($index = 0; $index -lt $Value.Length; $index += $Width) {
        $Value.Substring($index, [Math]::Min($Width, $Value.Length - $index))
    }
}

$source = Get-Content -LiteralPath $CatalogPath -Encoding utf8
$entries = foreach ($line in $source) {
    if ($line -match '^\s*H\((\d+), "([^"]+)", HexTier\.(Silver|Gold|Rainbow), "([^"]+)"\),$') {
        [pscustomobject]@{
            Id = [int]$Matches[1]
            Name = $Matches[2]
            Tier = $Matches[3]
            Description = $Matches[4]
        }
    }
}

$catalogIds = @($entries.Id | Sort-Object)
$expectedIds = @(1..82)
if ($entries.Count -ne 82 -or ($catalogIds -join ',') -ne ($expectedIds -join ',')) {
    throw '海克斯目录不是连续的 1 至 82，停止生成图片。'
}

$layout = @(
    [pscustomobject]@{ Tier = 'Silver'; Label = '银色'; Accent = '#B9C5D3'; Fill = '#344252'; Glow = '#9FB2C7' }
    [pscustomobject]@{ Tier = 'Gold'; Label = '金色'; Accent = '#FFD267'; Fill = '#5B4721'; Glow = '#F8B83E' }
    [pscustomobject]@{ Tier = 'Rainbow'; Label = '棱彩'; Accent = '#E7B8FF'; Fill = '#553B69'; Glow = '#D788FF' }
)
$totalCountByTier = @{}
$regularCountByTier = @{}
foreach ($group in $layout) {
    $totalCountByTier[$group.Tier] = @($entries | Where-Object Tier -eq $group.Tier).Count
    $regularCountByTier[$group.Tier] = @($entries | Where-Object { $_.Tier -eq $group.Tier -and $_.Id -notin 27, 48 }).Count
}

$width = 3600
$margin = 105
$gap = 55
$columnWidth = [int](($width - ($margin * 2) - ($gap * 2)) / 3)
$headerHeight = 530
$cardHeight = 130
$cardGap = 16
$maxItemCount = ($layout | ForEach-Object { @($entries | Where-Object Tier -eq $_.Tier).Count } | Measure-Object -Maximum).Maximum
$lastCardBottom = $headerHeight + 116 + (($maxItemCount - 1) * ($cardHeight + $cardGap)) + $cardHeight
$height = $lastCardBottom + 150
$font = 'Microsoft YaHei, Microsoft JhengHei, Noto Sans CJK SC, sans-serif'
$svg = [System.Collections.Generic.List[string]]::new()
$svg.Add("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$width`" height=`"$height`" viewBox=`"0 0 $width $height`">")
$svg.Add('<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#0A101A"/><stop offset="0.48" stop-color="#121B2A"/><stop offset="1" stop-color="#110D22"/></linearGradient><filter id="shadow" x="-10%" y="-10%" width="120%" height="120%"><feDropShadow dx="0" dy="10" stdDeviation="12" flood-color="#000000" flood-opacity="0.38"/></filter></defs>')
$svg.Add("<rect width=`"$width`" height=`"$height`" fill=`"url(#bg)`"/>")
$svg.Add("<path d=`"M0 420 C720 280 1040 610 1800 430 S2910 255 3600 445 L3600 0 L0 0Z`" fill=`"#17243A`" opacity=`"0.78`"/>")
$svg.Add("<text x=`"$margin`" y=`"158`" fill=`"#FFFFFF`" font-family=`"$font`" font-size=`"76`" font-weight=`"700`">GrandUMI 当前海克斯目录</text>")
$svg.Add("<text x=`"$margin`" y=`"240`" fill=`"#BFCDE1`" font-family=`"$font`" font-size=`"34`">完整目录 $($entries.Count) 项 · 新房间常规池 $(@($entries | Where-Object Id -notin 27, 48).Count) 项：银色 $($regularCountByTier['Silver']) / 金色 $($regularCountByTier['Gold']) / 棱彩 $($regularCountByTier['Rainbow'])</text>")
$svg.Add("<text x=`"$margin`" y=`"306`" fill=`"#9EADC3`" font-family=`"$font`" font-size=`"28`">#27 已退役（仅旧房间与录像兼容）；#48 为备选（不进入普通选秀或随机质变）；#30 已回归常规池</text>")
$svg.Add("<line x1=`"$margin`" y1=`"370`" x2=`"$($width-$margin)`" y2=`"370`" stroke=`"#52647C`" stroke-width=`"2`"/>")

for ($column = 0; $column -lt $layout.Count; $column++) {
    $group = $layout[$column]
    $x = $margin + $column * ($columnWidth + $gap)
    $items = @($entries | Where-Object Tier -eq $group.Tier | Sort-Object Id)
    $regularCount = $regularCountByTier[$group.Tier]
    $svg.Add("<rect x=`"$x`" y=`"$headerHeight`" width=`"$columnWidth`" height=`"88`" rx=`"18`" fill=`"$($group.Fill)`" stroke=`"$($group.Accent)`" stroke-width=`"2`"/>")
    $svg.Add("<circle cx=`"$($x+43)`" cy=`"$($headerHeight+44)`" r=`"15`" fill=`"$($group.Accent)`"/>")
    $svg.Add("<text x=`"$($x+78)`" y=`"$($headerHeight+56)`" fill=`"$($group.Accent)`" font-family=`"$font`" font-size=`"40`" font-weight=`"700`">$($group.Label) · 常规 $regularCount 项</text>")
    for ($index = 0; $index -lt $items.Count; $index++) {
        $item = $items[$index]
        $y = $headerHeight + 116 + $index * ($cardHeight + $cardGap)
        $status = if ($item.Id -eq 27) { '退役' } elseif ($item.Id -eq 48) { '备选' } else { '' }
        $cardFill = if ($status) { '#20293A' } else { '#172131' }
        $svg.Add("<g filter=`"url(#shadow)`"><rect x=`"$x`" y=`"$y`" width=`"$columnWidth`" height=`"$cardHeight`" rx=`"16`" fill=`"$cardFill`" stroke=`"$($group.Accent)`" stroke-opacity=`"0.46`" stroke-width=`"2`"/>")
        $svg.Add("<rect x=`"$($x+20)`" y=`"$($y+21)`" width=`"82`" height=`"44`" rx=`"12`" fill=`"$($group.Fill)`"/><text x=`"$($x+61)`" y=`"$($y+53)`" text-anchor=`"middle`" fill=`"$($group.Accent)`" font-family=`"$font`" font-size=`"27`" font-weight=`"700`">#$($item.Id)</text>")
        $nameX = $x + 120
        $svg.Add("<text x=`"$nameX`" y=`"$($y+53)`" fill=`"#FFFFFF`" font-family=`"$font`" font-size=`"34`" font-weight=`"700`">$(Escape-Xml $item.Name)</text>")
        if ($status) {
            $badgeWidth = 70
            $badgeX = $x + $columnWidth - $badgeWidth - 22
            $badgeColor = if ($status -eq '退役') { '#FC8C8C' } else { '#8ED6FF' }
            $svg.Add("<rect x=`"$badgeX`" y=`"$($y+20)`" width=`"$badgeWidth`" height=`"42`" rx=`"12`" fill=`"$badgeColor`" fill-opacity=`"0.19`" stroke=`"$badgeColor`"/><text x=`"$($badgeX+$badgeWidth/2)`" y=`"$($y+50)`" text-anchor=`"middle`" fill=`"$badgeColor`" font-family=`"$font`" font-size=`"24`" font-weight=`"700`">$status</text>")
        }
        $lines = @(Wrap-Text $item.Description)
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            $svg.Add("<text x=`"$($x+22)`" y=`"$($y+94+$lineIndex*28)`" fill=`"#C6D0DE`" font-family=`"$font`" font-size=`"25`">$(Escape-Xml $lines[$lineIndex])</text>")
        }
        $svg.Add('</g>')
    }
}
$svg.Add("<text x=`"$margin`" y=`"$($height-48)`" fill=`"#718198`" font-family=`"$font`" font-size=`"22`">来源：服务端WebSocket/Game/Hex/HexCatalog.cs · 品质显示名：银色 / 金色 / 棱彩</text>")
$svg.Add('</svg>')

$svgPath = [System.IO.Path]::ChangeExtension($OutputPath, '.svg')
[System.IO.File]::WriteAllText($svgPath, ($svg -join "`n"), [System.Text.UTF8Encoding]::new($false))

$chrome = @('C:\Program Files\Google\Chrome\Application\chrome.exe', 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw '未找到可用于 SVG 渲染的 Chrome 或 Edge。' }
. (Join-Path $PSScriptRoot '..\ops\windows\GrandUmiTemp.ps1')
$renderRoot = Get-GrandUmiTempDirectory -Category 'HexOverview'
$tempRoot = [IO.Path]::GetFullPath((Join-Path $renderRoot ('chrome-' + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $uri = [System.Uri]::new((Resolve-Path -LiteralPath $svgPath)).AbsoluteUri
    $arguments = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', "--user-data-dir=$tempRoot", "--window-size=$width,$height", '--force-device-scale-factor=1', "--screenshot=$OutputPath", $uri)
    $process = Start-Process -FilePath $chrome -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $OutputPath)) { throw '浏览器未能成功生成 PNG。' }
}
finally {
    $allowedPrefix = [IO.Path]::GetFullPath($renderRoot).TrimEnd('\') + '\'
    if (-not $tempRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw '浏览器临时目录超出当前任务范围，拒绝清理。'
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
