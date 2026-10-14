#!/usr/bin/env pwsh
<#
.SYNOPSIS
Xapper CLI를 사용한 통합테스트 러너 예시

.DESCRIPTION
E2E 테스트를 순서대로 실행하고 결과를 저장합니다.

.EXAMPLE
.\runner.ps1 -AppExe "C:\MyApp\MyApp.exe" -ScriptsDir "./scripts"
#>

param(
  [Parameter(Mandatory=$true)]
  [string]$AppExe,

  [Parameter(Mandatory=$false)]
  [string]$ScriptsDir = "./scripts",

  [Parameter(Mandatory=$false)]
  [string]$ResultsDir = "./results"
)

# 결과 디렉토리 생성
if (-not (Test-Path $ResultsDir)) {
  New-Item -ItemType Directory -Path $ResultsDir | Out-Null
}

# xapper-cli 경로
$cliPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$xapperCli = Join-Path $cliPath "bin/xapper.js"

Write-Host "🚀 Xapper 통합테스트 시작"
Write-Host "앱: $AppExe"
Write-Host "스크립트: $ScriptsDir"
Write-Host ""

# 1. 앱 시작
Write-Host "📱 앱 시작 중..."
try {
  $launchJson = node $xapperCli launch --exe $AppExe
  $launchResult = $launchJson | ConvertFrom-Json
  $appPid = $launchResult.pid
  Write-Host "✅ 앱 시작됨 (PID: $appPid)"
} catch {
  Write-Error "앱 시작 실패: $_"
  exit 1
}

$testResults = @()
$scriptFiles = Get-ChildItem -Path $ScriptsDir -Filter "*.js" | Sort-Object Name

foreach ($scriptFile in $scriptFiles) {
  $testName = $scriptFile.BaseName
  $scriptPath = $scriptFile.FullName

  Write-Host ""
  Write-Host "🧪 테스트 실행: $testName"

  # 각 테스트마다 결과 파일 생성
  $resultFile = Join-Path $ResultsDir "$testName.json"

  try {
    # 테스트 스크립트 실행
    $output = node $xapperCli run $scriptPath

    if ($LASTEXITCODE -eq 0) {
      Write-Host "✅ $testName 통과"
      $testResults += @{
        name = $testName
        status = "PASS"
        timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
      }

      # 결과 저장
      $output | Out-File -Encoding UTF8 $resultFile
    } else {
      Write-Host "❌ $testName 실패"
      $testResults += @{
        name = $testName
        status = "FAIL"
        timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
      }

      # 실패 결과도 저장
      $output | Out-File -Encoding UTF8 $resultFile
    }
  } catch {
    Write-Error "테스트 실행 오류: $_"
    $testResults += @{
      name = $testName
      status = "ERROR"
      error = $_.Exception.Message
      timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
  }

  # 짧은 대기
  Start-Sleep -Milliseconds 500
}

# 최종 보고서
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host "📋 테스트 결과 요약"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

$passCount = ($testResults | Where-Object { $_.status -eq "PASS" }).Count
$failCount = ($testResults | Where-Object { $_.status -eq "FAIL" }).Count
$errorCount = ($testResults | Where-Object { $_.status -eq "ERROR" }).Count
$totalCount = $testResults.Count

Write-Host "총 테스트: $totalCount"
Write-Host "✅ 통과: $passCount"
Write-Host "❌ 실패: $failCount"
Write-Host "⚠️  오류: $errorCount"
Write-Host ""

# 실패 목록
if ($failCount -gt 0 -or $errorCount -gt 0) {
  Write-Host "실패한 테스트:"
  $testResults | Where-Object { $_.status -ne "PASS" } | ForEach-Object {
    Write-Host "  - $($_.name) [$($_.status)]"
  }
}

# 최종 결과 파일 저장
$summaryFile = Join-Path $ResultsDir "summary.json"
@{
  timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
  appPid = $appPid
  total = $totalCount
  passed = $passCount
  failed = $failCount
  errors = $errorCount
  tests = $testResults
} | ConvertTo-Json | Out-File -Encoding UTF8 $summaryFile

Write-Host ""
Write-Host "📊 결과 저장됨: $summaryFile"

# 종료 코드
$exitCode = if ($failCount -gt 0 -or $errorCount -gt 0) { 1 } else { 0 }
exit $exitCode
