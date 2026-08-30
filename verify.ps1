param(
  [string]$ExpectedCommit = "",
  [string]$ProofPath = "",
  [switch]$InfrastructureOnly
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
Set-Location $repo

if ($ProofPath -and $InfrastructureOnly) {
  throw "基础设施快速检查不能生成部署验证证明。"
}

$runningOnWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($runningOnWindows) {
  . (Join-Path $repo "ops\windows\GrandUmiTemp.ps1")
  $verificationTemp = Get-GrandUmiTempDirectory -Category "Verify"
} else {
  $ciTempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { "/tmp" }
  $verificationTemp = Join-Path $ciTempRoot "grandumi-verify"
  New-Item -ItemType Directory -Force -Path $verificationTemp | Out-Null
}
$runTemp = Join-Path $verificationTemp ("run-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $runTemp | Out-Null
$env:GRANDUMI_TEST_TEMP_ROOT = $runTemp

$suiteResults = [Collections.Generic.List[object]]::new()
$failed = $false

function Invoke-VerificationSuite {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Command,
    [Parameter(Mandatory = $true)][scriptblock]$Action
  )

  Write-Host "`n[验证] $Name" -ForegroundColor Cyan
  $watch = [Diagnostics.Stopwatch]::StartNew()
  $status = "passed"
  try {
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "命令退出码为 $LASTEXITCODE" }
  } catch {
    $status = "failed"
    $script:failed = $true
    Write-Host "[失败] $Name：$($_.Exception.Message)" -ForegroundColor Red
  } finally {
    $watch.Stop()
    $suiteResults.Add([ordered]@{
      name = $Name
      command = $Command
      status = $status
      durationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds)
    })
  }
}

try {
  Invoke-VerificationSuite "WebSocket 协议契约" "node tools/verify-protocol-contract.mjs" {
    & node "tools/verify-protocol-contract.mjs"
  }
  Invoke-VerificationSuite "部署证明门禁" "node --test tools/verification-proof.test.mjs" {
    & node --test "tools/verification-proof.test.mjs" "tools/deploy-verification-gate.test.mjs"
  }

  if (-not $InfrastructureOnly) {
    Invoke-VerificationSuite "卡牌内容结构与清单" "node tools/verify-card-content.mjs" {
      & node "tools/verify-card-content.mjs"
    }
    Invoke-VerificationSuite "卡效严格审计" "node tools/audit-card-effects.mjs --strict" {
      & node "tools/audit-card-effects.mjs" --strict
    }
    Invoke-VerificationSuite "服务端完整测试" "dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj" {
      & dotnet test "服务端WebSocket.Tests/GrandUMIServer.Tests.csproj" --logger "console;verbosity=minimal"
    }
    Invoke-VerificationSuite "前端完整单元测试" "node --test opcgpro-web/tests/*.test.mjs" {
      $testFiles = Get-ChildItem (Join-Path $repo "opcgpro-web\tests") -Filter "*.test.mjs" |
        Sort-Object FullName | ForEach-Object FullName
      Push-Location (Join-Path $repo "opcgpro-web")
      try {
        & node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test $testFiles
      } finally {
        Pop-Location
      }
    }
    Invoke-VerificationSuite "QQ Bot 完整测试" "python -m unittest discover -s qq-bug-bot/tests" {
      $py = Get-Command py -ErrorAction SilentlyContinue
      if ($py) { & $py.Source -3 -m unittest discover -s "qq-bug-bot/tests" -p "test_*.py" }
      else { & python -m unittest discover -s "qq-bug-bot/tests" -p "test_*.py" }
    }
    Invoke-VerificationSuite "前端生产构建" "npm run build --prefix opcgpro-web" {
      & npm run build --prefix "opcgpro-web"
    }
    Invoke-VerificationSuite "移动端真实浏览器回归" "node tools/verify-mobile-browser.mjs" {
      & node "tools/verify-mobile-browser.mjs"
    }
  }

  if ($failed) {
    Write-Host "`n统一验证失败，不会生成部署证明。" -ForegroundColor Red
    exit 1
  }

  if ($ProofPath) {
    if (-not $ExpectedCommit) { throw "生成部署证明必须提供 -ExpectedCommit。" }
    $head = (& git rev-parse HEAD).Trim()
    $tree = (& git rev-parse 'HEAD^{tree}').Trim()
    if ($head -ne $ExpectedCommit) { throw "验证提交 $head 与待部署提交 $ExpectedCommit 不一致。" }
    $dirty = & git status --porcelain
    if ($dirty) { throw "工作区在验证后不是干净状态，拒绝为提交生成证明。" }
    if ($runningOnWindows -and
        -not ([IO.Path]::GetFullPath($ProofPath)).StartsWith("E:\GrandUMI-Temp\", [StringComparison]::OrdinalIgnoreCase)) {
      throw "Windows 上的验证证明必须写入 E:\GrandUMI-Temp\。"
    }
    if (Test-Path -LiteralPath $ProofPath) { Remove-Item -LiteralPath $ProofPath -Force }
    $inputObject = [ordered]@{
      commit = $head
      tree = $tree
      platform = [Environment]::OSVersion.Platform.ToString()
      suites = $suiteResults
    }
    $inputObject | ConvertTo-Json -Depth 8 | & node "tools/verification-proof.mjs" create --output $ProofPath
    if ($LASTEXITCODE -ne 0) { throw "生成部署验证证明失败。" }
  }

  Write-Host "`n统一验证通过：$($suiteResults.Count) 个套件。" -ForegroundColor Green
} finally {
  if (Test-Path -LiteralPath $runTemp) {
    $resolvedRunTemp = [IO.Path]::GetFullPath($runTemp)
    $resolvedRoot = [IO.Path]::GetFullPath($verificationTemp)
    if ($resolvedRunTemp.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
      Remove-Item -LiteralPath $resolvedRunTemp -Recurse -Force
    }
  }
}
