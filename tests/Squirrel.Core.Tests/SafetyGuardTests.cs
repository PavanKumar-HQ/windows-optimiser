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

        [Fact]
        public void ProtectedOfficePath_MustBeProtectedEvenWithUnusualName()
        {
            var path = @"C:\Program Files\Microsoft Office\Root\Office16\custom_tool.exe";
            var isProtected = SafetyGuard.IsProtectedEntity("custom_tool.exe", path, out var reason);
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
        [InlineData("osk.exe")]
        [InlineData("narrator.exe")]
        [InlineData("magnify.exe")]
        public void ProtectedAccessibilityEntities_MustBeProtected(string entityName)
        {
            var isProtected = SafetyGuard.IsProtectedEntity(entityName, out var reason);
            Assert.True(isProtected);
            Assert.Contains("accessibility", reason, StringComparison.OrdinalIgnoreCase);
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
        public void DriveRoots_MustBeRejectedForCleanup()
        {
            Assert.False(SafetyGuard.IsPathSafeForCleanup(@"C:\", out var reasonC));
            Assert.Contains("drive root", reasonC, StringComparison.OrdinalIgnoreCase);

            Assert.False(SafetyGuard.IsPathSafeForCleanup(@"/", out var reasonSlash));
            Assert.Contains("drive root", reasonSlash, StringComparison.OrdinalIgnoreCase);
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
        public void ProgramFiles_MustBeRejectedForCleanup()
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf))
            {
                Assert.False(SafetyGuard.IsPathSafeForCleanup(pf, out var reasonPf));
                Assert.Contains("Program Files", reasonPf, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void TempDirectories_AreAllowedForCleanup()
        {
            var tempDir = Path.GetTempPath();
            var isSafe = SafetyGuard.IsPathSafeForCleanup(tempDir, out var reason);
            Assert.True(isSafe, $"Expected temp dir to be safe, but was rejected: {reason}");
        }

        [Fact]
        public void ArbitraryNonWhitelistedPaths_AreRejected()
        {
            var fakePath = Path.Combine(Path.GetTempPath(), "..", "RandomDirectory", "stuff");
            // If normalized path does not match any approved category whitelist
            var isSafe = SafetyGuard.IsPathSafeForCleanup(fakePath, out var reason);
            Assert.False(isSafe);
            Assert.Contains("whitelist", reason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
