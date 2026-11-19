# Windows Batch-Mode Build & Test Automation Script
param (
    [string]$UnityExe = "C:\Program Files\Unity\Hub\6000.0.30f1\Editor\Unity.exe",
    [string]$ProjectPath = $PSScriptRoot,
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  NETWORKED VR SHOOTING RANGE — AUTOMATED BUILD PIPELINE  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Unity Executable: $UnityExe"
Write-Host "Project Path:     $ProjectPath"

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity executable not found at '$UnityExe'. Please verify installation."
    exit 1
}

$logsDir = Join-Path $ProjectPath "Logs"
if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir -Force | Out-Null }

$buildsDir = Join-Path $ProjectPath "Builds\WindowsPCVR"
if (-not (Test-Path $buildsDir)) { New-Item -ItemType Directory -Path $buildsDir -Force | Out-Null }

# 1. Run Automated Tests
if (-not $SkipTests) {
    Write-Host "`n[1/2] Running EditMode Unit Tests..." -ForegroundColor Yellow
    $testResultsXml = Join-Path $logsDir "TestResults_EditMode.xml"
    $testLog = Join-Path $logsDir "TestExecution.log"

    $testArgs = @(
        "-batchmode",
        "-nographics",
        "-projectPath", "`"$ProjectPath`"",
        "-runTests",
        "-testPlatform", "EditMode",
        "-testResults", "`"$testResultsXml`"",
        "-logFile", "`"$testLog`""
    )

    $testProcess = Start-Process -FilePath $UnityExe -ArgumentList $testArgs -PassThru -Wait -NoNewWindow
    if ($testProcess.ExitCode -ne 0) {
        Write-Warning "Tests completed with code $($testProcess.ExitCode). Inspect $testLog for details."
    } else {
        Write-Host "All tests passed successfully!" -ForegroundColor Green
    }
}

# 2. Build Windows x64 PCVR Standalone
Write-Host "`n[2/2] Building Windows x64 PCVR Player..." -ForegroundColor Yellow
$buildLog = Join-Path $logsDir "Build_WindowsPCVR.log"

$buildArgs = @(
    "-batchmode",
    "-nographics",
    "-projectPath", "`"$ProjectPath`"",
    "-executeMethod", "ShootingRange.Editor.BuildPipelineScript.BuildWindowsPCVR",
    "-logFile", "`"$buildLog`""
)

$buildProcess = Start-Process -FilePath $UnityExe -ArgumentList $buildArgs -PassThru -Wait -NoNewWindow
if ($buildProcess.ExitCode -ne 0) {
    Write-Error "Build failed with exit code $($buildProcess.ExitCode). Check log at: $buildLog"
    exit $buildProcess.ExitCode
}

$outputExe = Join-Path $buildsDir "ShootingRange.exe"
if (Test-Path $outputExe) {
    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " BUILD SUCCEEDED!" -ForegroundColor Green
    Write-Host " Binary Path: $outputExe" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    exit 0
} else {
    Write-Error "Build executable not found at expected location '$outputExe'."
    exit 1
}

