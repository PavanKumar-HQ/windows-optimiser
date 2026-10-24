# Squirrel — Personal Laptop Performance & Responsiveness Utility

Squirrel is a lightweight, local Windows utility built specifically for personal use on **LapMac** (Windows 10 Pro 22H2, AMD PRO A4-4350B dual-core APU, 8 GB DDR3 RAM, 238 GB SSD).

Squirrel is designed strictly for personal laptop maintenance:
- **No telemetry, cloud dependencies, accounts, or background services.**
- **Strict safety guards:** Read-only diagnostics by default; Microsoft Office and Windows security components are protected; personal files are never touched.
- **Evidence-based diagnosis first:** Diagnoses sustained CPU and memory bottlenecks using repeated samples rather than assuming storage cleaning will fix sluggishness.
- **Self-monitoring:** Displays Squirrel's own CPU and RAM usage to guarantee it does not slow down the dual-core laptop.

---

## Architecture

| Component | Description |
| :--- | :--- |
| **`Squirrel.Core`** | Core engine containing `BottleneckDetector`, `WindowsSystemMonitor`, `WindowsStartupAnalyzer`, `SafeStorageCleaner`, `SafetyGuard`, `LocalJsonAuditLedger`, and `WindowsRecommendationEngine`. |
| **`Squirrel.Wpf`** | Native Windows desktop WPF application with real-time hardware gauges, bottleneck inspection, startup manager, review-first cleaner, and audit history. |
| **`Squirrel.Cli`** | Interactive console interface for quick terminal-based diagnostics and management. |
| **`QuickDiagnose.ps1`** | Zero-dependency PowerShell 5.1 diagnostic script that runs immediately on Windows 10 without compiling. |
| **`Squirrel.Core.Tests`** | Automated unit test suite verifying safety guards, multi-sample bottleneck classification, and audit ledger persistence. |

---

## Strict Safety Guardrails

Squirrel enforces strict boundaries defined in [`SafetyGuard.cs`](file:///Users/pavankumars/Downloads/windows%20optimiser/src/Squirrel.Core/Safety/SafetyGuard.cs):
1. **Microsoft Office Protection:** Protects `OfficeClickToRun.exe`, `WINWORD.EXE`, `EXCEL.EXE`, `POWERPNT.EXE`, `OUTLOOK.EXE`, `ONENOTE.EXE`, and the `ClickToRunSvc` service.
2. **Windows Security & Core OS:** Never modifies Windows Defender (`MsMpEng.exe`), Security Health, or essential OS services.
3. **Personal Files:** `SafetyGuard.IsPathSafeForCleanup()` strictly rejects personal user folders (`Documents`, `Desktop`, `Pictures`, `Videos`, `Downloads`, `OneDrive`).
4. **Review-First & Confirmation Required:** Every proposed change requires explicit user approval.
5. **Reversibility & Audit Ledger:** Reversible actions (like disabling startup items) backup registry values to `HKCU\Software\Squirrel\DisabledStartup` and can be restored with a single click. Every operation is recorded in `%LOCALAPPDATA%\Squirrel\audit_ledger.json`.

---

## How to Run on Your Windows Laptop

### Option 1: One-Click Launcher (`run.bat`)
Double-click `scripts\run.bat` on your laptop to launch either:
1. **Squirrel Desktop App (WPF GUI)**
2. **Squirrel Interactive Console (CLI)**
3. **Zero-Dependency PowerShell Diagnostic (`QuickDiagnose.ps1`)**

### Option 2: Build and Test from PowerShell
```powershell
# Open PowerShell in the project directory
.\scripts\build.ps1
```
This script will:
- Check your Windows 10 environment and .NET SDK.
- Build the solution.
- Run all 32 automated unit tests.
- Publish ready-to-run executables into `publish\`.

### Option 3: Immediate Zero-Dependency Quick Scan
If you want to diagnose your laptop immediately before installing the .NET SDK:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\QuickDiagnose.ps1
```

---

## Automated Test Verification

All 32 automated unit and integration tests can be verified using:
```bash
dotnet test
```
Verified passing:
- `SafetyGuardTests`: Protection of Microsoft Office, Windows OS, security services, hardware drivers, and user personal files.
- `BottleneckDetectorTests`: Multi-sample time-series analysis and sustained vs transient process classification.
- `StorageCleanerTests`: Review-first confirmation guard, dry-run simulation, in-use locked file tolerance, and safety refusal on protected directories.
- `AuditLedgerTests`: JSON persistence, concurrency safety, and rollback status updates.
- `RecommendationEngineTests`: Concrete evidence generation and startup protection filtering.
