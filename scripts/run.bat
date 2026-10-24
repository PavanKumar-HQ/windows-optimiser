@echo off
setlocal
cd /d "%~dp0\.."

echo ==========================================================
echo   SQUIRREL — PERSONAL WINDOWS LAUNCHER (LapMac)
echo ==========================================================
echo.
echo Select interface:
echo 1. Launch Squirrel Desktop App (WPF GUI)
echo 2. Launch Squirrel Interactive Console (CLI)
echo 3. Run Standalone Diagnostic Script (PowerShell)
echo 0. Exit
echo.
set /p choice="Enter choice [1-3]: "

if "%choice%"=="1" (
    if exist "publish\Squirrel.Wpf\Squirrel.Wpf.exe" (
        start "" "publish\Squirrel.Wpf\Squirrel.Wpf.exe"
    ) else (
        echo Building and launching Squirrel WPF...
        dotnet run --project src\Squirrel.Wpf\Squirrel.Wpf.csproj
    )
) else if "%choice%"=="2" (
    if exist "publish\Squirrel.Cli\Squirrel.Cli.exe" (
        "publish\Squirrel.Cli\Squirrel.Cli.exe"
    ) else (
        echo Building and launching Squirrel CLI...
        dotnet run --project src\Squirrel.Cli\Squirrel.Cli.csproj
    )
) else if "%choice%"=="3" (
    powershell.exe -ExecutionPolicy Bypass -File "%~dp0QuickDiagnose.ps1"
    pause
) else (
    echo Exiting.
)
