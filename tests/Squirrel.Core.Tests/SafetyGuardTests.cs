using System;
using System.IO;
using Squirrel.Core.Safety;
using Xunit;

namespace Squirrel.Core.Tests
{
    public class SafetyGuardTests
    {
        [Theory]
        [InlineData("WINWORD.EXE")]
        [InlineData("EXCEL.EXE")]
        [InlineData("POWERPNT.EXE")]
        [InlineData("OUTLOOK.EXE")]
        [InlineData("OfficeClickToRun.exe")]
        [InlineData("ClickToRunSvc")]
        public void ProtectedOfficeEntities_MustBeProtected(string entityName)
        {
            var isProtected = SafetyGuard.IsProtectedEntity(entityName, out var reason);
            Assert.True(isProtected);
            Assert.Contains("Office", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("MsMpEng.exe")]
        [InlineData("WinDefend")]
        [InlineData("SecurityHealthService.exe")]
        [InlineData("smartscreen.exe")]
        public void ProtectedSecurityEntities_MustBeProtected(string entityName)
        {
            var isProtected = SafetyGuard.IsProtectedEntity(entityName, out var reason);
            Assert.True(isProtected);
            Assert.Contains("Security", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("csrss.exe")]
        [InlineData("smss.exe")]
        [InlineData("services.exe")]
        [InlineData("explorer.exe")]
        [InlineData("dwm.exe")]
        public void ProtectedSystemEntities_MustBeProtected(string entityName)
        {
            var isProtected = SafetyGuard.IsProtectedEntity(entityName, out var reason);
            Assert.True(isProtected);
            Assert.Contains("Windows", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("RadeonSoftware.exe")]
        [InlineData("AMDRSServ.exe")]
        [InlineData("RtkNGUI64.exe")]
        [InlineData("SynTPEnh.exe")]
        public void ProtectedHardwareEntities_MustBeProtected(string entityName)
        {
            var isProtected = SafetyGuard.IsProtectedEntity(entityName, out var reason);
            Assert.True(isProtected);
            Assert.Contains("driver", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void UnprotectedApplication_IsAllowed()
        {
            var isProtected = SafetyGuard.IsProtectedEntity("SomeRandomApp.exe", out var reason);
            Assert.False(isProtected);
            Assert.Empty(reason);
        }

        [Fact]
        public void PersonalFolders_MustBeRejectedForCleanup()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            Assert.False(SafetyGuard.IsPathSafeForCleanup(userProfile, out var reasonProfile));
            Assert.Contains("user profile", reasonProfile, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(docs))
            {
                Assert.False(SafetyGuard.IsPathSafeForCleanup(docs, out var reasonDocs));
                Assert.Contains("personal", reasonDocs, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(desktop))
            {
                Assert.False(SafetyGuard.IsPathSafeForCleanup(desktop, out var reasonDesktop));
                Assert.Contains("personal", reasonDesktop, StringComparison.OrdinalIgnoreCase);
            }

            var downloadDir = Path.Combine(userProfile, "Downloads");
            Assert.False(SafetyGuard.IsPathSafeForCleanup(downloadDir, out var reasonDownloads));
            Assert.Contains("personal", reasonDownloads, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TempDirectories_AreAllowedForCleanup()
        {
            var tempDir = Path.GetTempPath();
            var isSafe = SafetyGuard.IsPathSafeForCleanup(tempDir, out var reason);
            Assert.True(isSafe, $"Expected temp dir to be safe, but was rejected: {reason}");
        }
    }
}
