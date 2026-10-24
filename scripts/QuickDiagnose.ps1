<#
.SYNOPSIS
    Squirrel Zero-Dependency Quick Diagnostic for Windows 10 (LapMac)
.DESCRIPTION
    Runs natively on Windows 10 PowerShell 5.1 without compiling.
    Takes 5 repeated hardware samples to diagnose sustained CPU and memory bottlenecks.
#>

[CmdletBinding()]
param ()

$ErrorActionPreference = "SilentlyContinue"

Clear-Host
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  SQUIRREL — LAPTOP PERFORMANCE DIAGNOSTIC (LapMac)       " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Target Machine: AMD PRO A4-4350B · 8 GB RAM · Windows 10 Pro 22H2" -ForegroundColor DarkGray
Write-Host "Strict Safety: Read-Only Diagnostic Mode Active`n" -ForegroundColor DarkGray

# 1. Hardware Baseline
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor
$cs = Get-CimInstance Win32_ComputerSystem
$drive = Get-PSDrive C

$totalRamGb = [Math]::Round($cs.TotalPhysicalMemory / 1GB, 2)
$freeRamGb = [Math]::Round($os.FreePhysicalMemory / 1MB / 1024, 2)
$usedRamGb = [Math]::Round($totalRamGb - $freeRamGb, 2)
$driveFreeGb = [Math]::Round($drive.Free / 1GB, 2)
$driveUsedGb = [Math]::Round($drive.Used / 1GB, 2)
$driveTotalGb = [Math]::Round(($drive.Used + $drive.Free) / 1GB, 2)

Write-Host "--- [1/4] HARDWARE STATUS ---" -ForegroundColor Cyan
Write-Host "  CPU:          $($cpu.Name) ($($cpu.NumberOfCores) Cores / $($cpu.NumberOfLogicalProcessors) Threads)"
Write-Host "  Memory:       $usedRamGb GB used of $totalRamGb GB ($([Math]::Round(($usedRamGb/$totalRamGb)*100, 1))%)"
Write-Host "  SSD Drive C:  $driveFreeGb GB free of $driveTotalGb GB ($([Math]::Round(($driveFreeGb/$driveTotalGb)*100, 1))% free)"
Write-Host "  User:         $env:USERNAME on $env:COMPUTERNAME"

# 2. Microsoft Office Integrity Check
Write-Host "`n--- [2/4] MICROSOFT OFFICE INTEGRITY CHECK ---" -ForegroundColor Cyan
$officeSvc = Get-Service -Name "ClickToRunSvc" -ErrorAction SilentlyContinue
if ($officeSvc) {
    Write-Host "  Microsoft Office Service (ClickToRunSvc): $($officeSvc.Status) (StartType: $($officeSvc.StartType))" -ForegroundColor Green
    Write-Host "  [Safety Verified] Office services and Click-to-Run processes are fully preserved." -ForegroundColor DarkGray
} else {
    Write-Host "  ClickToRunSvc service not detected in standard location." -ForegroundColor Yellow
}

# 3. Multi-Sample Bottleneck Diagnosis
Write-Host "`n--- [3/4] 5-SAMPLE BOTTLENECK DIAGNOSIS ---" -ForegroundColor Cyan
Write-Host "Taking 5 repeated samples over 7.5 seconds to detect sustained CPU hogs..." -ForegroundColor DarkGray

$samples = 5
$cpuLoadArray = @()
$procStats = @{}

for ($i = 1; $i -le $samples; $i++) {
    $cpuLoad = (Get-CimInstance Win32_Processor).LoadPercentage
    $cpuLoadArray += $cpuLoad
    Write-Host "  Sample $i/$samples: Total CPU Load = $cpuLoad%" -ForegroundColor DarkGray

    # Sample top processes by CPU time
    Get-Process | Where-Object { $_.Id -ne 0 -and $_.ProcessName -ne "Idle" } | ForEach-Object {
        $name = $_.ProcessName
        $cpuTime = $_.CPU
        $memMb = [Math]::Round($_.WorkingSet64 / 1MB, 1)

        if (-not $procStats.ContainsKey($name)) {
            $procStats[$name] = @{
                Name = $name
                LastCpu = $cpuTime
                WorkingSetMb = $memMb
                CpuHits = 0
            }
        } else {
            $delta = $cpuTime - $procStats[$name].LastCpu
            $procStats[$name].LastCpu = $cpuTime
            $procStats[$name].WorkingSetMb = $memMb
            if ($delta -gt 0.8) {
                $procStats[$name].CpuHits++
            }
        }
    }

    if ($i -lt $samples) {
        Start-Sleep -Milliseconds 1500
    }
}

$avgCpu = [Math]::Round(($cpuLoadArray | Measure-Object -Average).Average, 1)
$peakCpu = ($cpuLoadArray | Measure-Object -Maximum).Maximum

Write-Host "`n  Diagnostic Results:" -ForegroundColor White
Write-Host "  Average CPU Load: $avgCpu% (Peak: $peakCpu%)"

$sustained = $procStats.Values | Where-Object { $_.CpuHits -ge 2 } | Sort-Object CpuHits -Descending
if ($sustained.Count -gt 0) {
    Write-Host "`n  Persistent Resource Consumers Detected:" -ForegroundColor Yellow
    foreach ($p in $sustained) {
        Write-Host "   • $($p.Name,-25) RAM: $($p.WorkingSetMb,5) MB | Sustained Activity: $($p.CpuHits)/$($samples-1) intervals"
    }
} else {
    Write-Host "  No single process sustained abnormal background CPU load during the sampling window." -ForegroundColor Green
}

# 4. Safe Storage Analysis (Review-First)
Write-Host "`n--- [4/4] REVIEW-FIRST SAFE STORAGE ESTIMATE ---" -ForegroundColor Cyan
$safePaths = @(
    @{ Name = "User Temp"; Path = $env:TEMP },
    @{ Name = "Windows Crash Dumps"; Path = "$env:LOCALAPPDATA\CrashDumps" },
    @{ Name = "Windows System Temp"; Path = "$env:WINDIR\Temp" },
    @{ Name = "Windows Update Cache"; Path = "$env:WINDIR\SoftwareDistribution\Download" }
)

$totalReclaimableMb = 0.0
foreach ($target in $safePaths) {
    if (Test-Path $target.Path) {
        $files = Get-ChildItem -Path $target.Path -File -Recurse -Force -ErrorAction SilentlyContinue
        $sizeBytes = ($files | Measure-Object -Property Length -Sum).Sum
        $sizeMb = [Math]::Round($sizeBytes / 1MB, 1)
        $totalReclaimableMb += $sizeMb
        Write-Host "  $($target.Name,-26) $($files.Count,5) files ($sizeMb MB)"
    }
}

Write-Host "`n  Total Safe Reclaimable Space: $([Math]::Round($totalReclaimableMb, 1)) MB" -ForegroundColor Green
Write-Host "  Personal files (Documents, Desktop, Downloads, OneDrive) were NOT included or modified." -ForegroundColor DarkGray

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  DIAGNOSTIC COMPLETE                                     " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
