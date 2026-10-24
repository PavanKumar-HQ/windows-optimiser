using System;
using System.IO;
using System.Threading.Tasks;
using Squirrel.Core.Actions;
using Squirrel.Core.Audit;
using Squirrel.Core.Processes;
using Xunit;

namespace Squirrel.Core.Tests
{
    public class StartupActionTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _auditLogPath;
        private readonly LocalJsonAuditLedger _ledger;

        public StartupActionTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "SquirrelStartupTests_" + Guid.NewGuid().ToString("N"));
            _auditLogPath = Path.Combine(_testRoot, "audit.json");
            _ledger = new LocalJsonAuditLedger(_auditLogPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutConfirmation_RejectsExecution()
        {
            var item = new StartupItem
            {
                Name = "HarmlessUtility",
                CommandLine = @"C:\Tools\Harmless.exe",
                LocationType = StartupLocationType.RegistryCurrentUser,
                LocationPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                CanSafelyDisable = true
            };

            var action = new DisableStartupItemAction(item, _ledger);
            var result = await action.ExecuteAsync(userConfirmed: false);

            Assert.False(result.Succeeded);
            Assert.Contains("confirmation", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_RunOnceItem_RejectsExecution()
        {
            var item = new StartupItem
            {
                Name = "PendingSetupUpdate",
                CommandLine = @"C:\Setup\update.exe",
                LocationType = StartupLocationType.RegistryCurrentUser,
                LocationPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                CanSafelyDisable = false
            };

            var action = new DisableStartupItemAction(item, _ledger);
            var result = await action.ExecuteAsync(userConfirmed: true);

            Assert.False(result.Succeeded);
            Assert.Contains("RunOnce", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_ProtectedOfficeItem_RejectsExecution()
        {
            var item = new StartupItem
            {
                Name = "OfficeSyncProcess",
                CommandLine = @"C:\Program Files\Microsoft Office\root\Office16\OfficeClickToRun.exe",
                ExecutablePath = @"C:\Program Files\Microsoft Office\root\Office16\OfficeClickToRun.exe",
                LocationType = StartupLocationType.RegistryCurrentUser,
                LocationPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                CanSafelyDisable = true // Even if marked true by error, SafetyGuard must intercept
            };

            var action = new DisableStartupItemAction(item, _ledger);
            var result = await action.ExecuteAsync(userConfirmed: true);

            Assert.False(result.Succeeded);
            Assert.Contains("Office", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_ProtectedSecurityItem_RejectsExecution()
        {
            var item = new StartupItem
            {
                Name = "SecurityHealth",
                CommandLine = @"C:\Windows\System32\SecurityHealthSystray.exe",
                ExecutablePath = @"C:\Windows\System32\SecurityHealthSystray.exe",
                LocationType = StartupLocationType.RegistryCurrentUser,
                LocationPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                CanSafelyDisable = true
            };

            var action = new DisableStartupItemAction(item, _ledger);
            var result = await action.ExecuteAsync(userConfirmed: true);

            Assert.False(result.Succeeded);
            Assert.Contains("Security", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_ProtectedAccessibilityItem_RejectsExecution()
        {
            var item = new StartupItem
            {
                Name = "osk",
                CommandLine = @"C:\Windows\System32\osk.exe",
                ExecutablePath = @"C:\Windows\System32\osk.exe",
                LocationType = StartupLocationType.RegistryCurrentUser,
                LocationPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                CanSafelyDisable = true
            };

            var action = new DisableStartupItemAction(item, _ledger);
            var result = await action.ExecuteAsync(userConfirmed: true);

            Assert.False(result.Succeeded);
            Assert.Contains("accessibility", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
