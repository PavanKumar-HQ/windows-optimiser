using System;
using System.IO;
using System.Threading.Tasks;
using Squirrel.Core.Audit;
using Xunit;

namespace Squirrel.Core.Tests
{
    public class AuditLedgerTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _logPath;

        public AuditLedgerTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "SquirrelAuditTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _logPath = Path.Combine(_testDir, "audit.json");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task RecordAndRetrieve_PersistsCorrectly()
        {
            var ledger1 = new LocalJsonAuditLedger(_logPath);
            var entry = new AuditEntry
            {
                Id = "test-123",
                ActionType = "DisableStartupItem",
                TargetName = "Spotify",
                TargetIdentifier = "HKCU\\Run\\Spotify",
                Description = "Disabled Spotify from auto-starting",
                PreviousState = "Enabled",
                NewState = "Disabled",
                IsReversible = true,
                Status = "Completed"
            };

            await ledger1.RecordEntryAsync(entry);

            // Create new instance pointing to same file
            var ledger2 = new LocalJsonAuditLedger(_logPath);
            var entries = await ledger2.GetEntriesAsync();

            Assert.Single(entries);
            Assert.Equal("test-123", entries[0].Id);
            Assert.Equal("Spotify", entries[0].TargetName);
            Assert.Equal("Completed", entries[0].Status);

            // Test mark reverted
            var reverted = await ledger2.MarkRevertedAsync("test-123");
            Assert.True(reverted);

            var updatedEntries = await ledger2.GetEntriesAsync();
            Assert.Equal("Reverted", updatedEntries[0].Status);
        }
    }
}
