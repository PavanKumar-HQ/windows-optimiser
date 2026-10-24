<#
.SYNOPSIS
    Build and publish Squirrel on Windows 10 for LapMac.
.DESCRIPTION
    Verifies .NET SDK compatibility, builds the solution, executes automated tests,
    and publishes Squirrel for immediate local execution.
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  SQUIRREL — PERSONAL WINDOWS BUILD SCRIPT (LapMac)        " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

# 1. Verify Operating System and Architecture
$os = Get-CimInstance Win32_OperatingSystem
Write-Host "`n[1/4] Inspecting Local System Environment..." -ForegroundColor Cyan
Write-Host "  OS Caption:      $($os.Caption)"
Write-Host "  OS Version:      $($os.Version) (Build $($os.BuildNumber))"
Write-Host "  Architecture:    $($os.OSArchitecture)"
Write-Host "  User Account:    $env:USERNAME"

# 2. Check .NET SDK
Write-Host "`n[2/4] Checking .NET SDK..." -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "  [!] .NET SDK not detected in PATH." -ForegroundColor Yellow
    Write-Host "  Please install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

$dotnetVersion = dotnet --version
Write-Host "  Installed .NET SDK: $dotnetVersion"

# 3. Build Solution and Run Tests
$rootPath = Split-Path -Parent $PSScriptRoot
Set-Location $rootPath

Write-Host "`n[3/4] Building Solution ($Configuration)..." -ForegroundColor Cyan
dotnet build "$rootPath\Squirrel.sln" -c $Configuration

if (-not $SkipTests) {
    Write-Host "`nExecuting Automated Safety & Regression Test Suite..." -ForegroundColor Cyan
    dotnet test "$rootPath\tests\Squirrel.Core.Tests\Squirrel.Core.Tests.csproj" -c $Configuration --no-build
}

# 4. Publish Native Windows Binaries
Write-Host "`n[4/4] Publishing Squirrel Application..." -ForegroundColor Cyan
$publishCliDir = "$rootPath\publish\Squirrel.Cli"
$publishWpfDir = "$rootPath\publish\Squirrel.Wpf"

dotnet publish "$rootPath\src\Squirrel.Cli\Squirrel.Cli.csproj" -c $Configuration -o $publishCliDir --no-build
dotnet publish "$rootPath\src\Squirrel.Wpf\Squirrel.Wpf.csproj" -c $Configuration -o $publishWpfDir --no-build

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  BUILD SUCCESSFUL!                                       " -ForegroundColor Green
Write-Host "  Squirrel WPF: $publishWpfDir\Squirrel.Wpf.exe" -ForegroundColor White
Write-Host "  Squirrel CLI: $publishCliDir\Squirrel.Cli.exe" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
