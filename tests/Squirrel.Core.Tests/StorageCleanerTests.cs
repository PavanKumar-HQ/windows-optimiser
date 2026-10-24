using System;
using System.IO;
using System.Threading.Tasks;
using Squirrel.Core.Audit;
using Squirrel.Core.Storage;
using Xunit;

namespace Squirrel.Core.Tests
{
    public class StorageCleanerTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _targetDir;
        private readonly string _auditLogPath;
        private readonly LocalJsonAuditLedger _ledger;

        public StorageCleanerTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "SquirrelTests_" + Guid.NewGuid().ToString("N"));
            _targetDir = Path.Combine(_testRoot, "target_files");
            Directory.CreateDirectory(_targetDir);

            _auditLogPath = Path.Combine(_testRoot, "audit_store", "test_audit.json");
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
        public async Task CleanTargetAsync_WithoutUserConfirmation_AbortsExecution()
        {
            var cleaner = new SafeStorageCleaner(_ledger);
            var target = new CleanupTarget
            {
                Name = "Test Temp",
                DirectoryPath = _targetDir
            };

            var result = await cleaner.CleanTargetAsync(target, userConfirmed: false, dryRun: false);

            Assert.False(result.Succeeded);
            Assert.Contains("approval", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CleanTargetAsync_DryRun_DoesNotDeleteFiles()
        {
            var filePath = Path.Combine(_targetDir, "sample.tmp");
            File.WriteAllText(filePath, "Squirrel diagnostic test data");

            var cleaner = new SafeStorageCleaner(_ledger);
            var target = new CleanupTarget
            {
                Name = "Test Temp",
                DirectoryPath = _targetDir
            };

            var result = await cleaner.CleanTargetAsync(target, userConfirmed: true, dryRun: true);

            Assert.True(result.Succeeded);
            Assert.True(result.IsDryRun);
            Assert.Equal(1, result.DeletedFilesCount);
            Assert.True(File.Exists(filePath), "Dry run must NOT delete files from disk.");
        }

        [Fact]
        public async Task CleanTargetAsync_Confirmed_DeletesFilesAndLogsAudit()
        {
            var filePath = Path.Combine(_targetDir, "sample.tmp");
            File.WriteAllText(filePath, "Hello world data");

            var cleaner = new SafeStorageCleaner(_ledger);
            var target = new CleanupTarget
            {
                Name = "Test Temp",
                DirectoryPath = _targetDir,
                FileCount = 1,
                SizeBytes = 16
            };

            var result = await cleaner.CleanTargetAsync(target, userConfirmed: true, dryRun: false);

            Assert.True(result.Succeeded);
            Assert.False(File.Exists(filePath));
            Assert.Equal(1, result.DeletedFilesCount);

            var auditEntries = await _ledger.GetEntriesAsync();
            Assert.Single(auditEntries);
            Assert.Equal("StorageCleanup", auditEntries[0].ActionType);
            Assert.Equal("Completed", auditEntries[0].Status);
        }

        [Fact]
        public async Task CleanTargetAsync_HandlesInUseFilesGracefully()
        {
            var lockedFilePath = Path.Combine(_targetDir, "locked.tmp");
            var freeFilePath = Path.Combine(_targetDir, "free.tmp");

            File.WriteAllText(freeFilePath, "Can be deleted");

            // Open locked file exclusively
            using (var stream = new FileStream(lockedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Write(new byte[] { 1, 2, 3 });
                stream.Flush();

                var cleaner = new SafeStorageCleaner(_ledger);
                var target = new CleanupTarget
                {
                    Name = "Test Temp",
                    DirectoryPath = _targetDir
                };

                var result = await cleaner.CleanTargetAsync(target, userConfirmed: true, dryRun: false);

                Assert.True(result.Succeeded);
                Assert.Equal(1, result.DeletedFilesCount);
                Assert.Equal(1, result.SkippedFilesCount);
                Assert.False(File.Exists(freeFilePath));
                Assert.True(File.Exists(lockedFilePath));
            }
        }
    }
}
