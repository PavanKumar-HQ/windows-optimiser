using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Squirrel.Core.Audit
{
    public class LocalJsonAuditLedger : IAuditLedger
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public LocalJsonAuditLedger(string? customFilePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customFilePath))
            {
                _filePath = customFilePath;
            }
            else
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var squirrelDir = Path.Combine(localAppData, "Squirrel");
                _filePath = Path.Combine(squirrelDir, "audit_ledger.json");
            }

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch { }
            }
        }

        public async Task RecordEntryAsync(AuditEntry entry)
        {
            await _semaphore.WaitAsync();
            try
            {
                var entries = await LoadEntriesInternalAsync();
                entries.Add(entry);
                await SaveEntriesInternalAsync(entries);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<AuditEntry>> GetEntriesAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                return await LoadEntriesInternalAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> MarkRevertedAsync(string entryId)
        {
            await _semaphore.WaitAsync();
            try
            {
                var entries = await LoadEntriesInternalAsync();
                var target = entries.Find(e => string.Equals(e.Id, entryId, StringComparison.OrdinalIgnoreCase));
                if (target == null) return false;

                target.Status = "Reverted";
                await SaveEntriesInternalAsync(entries);
                return true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<List<AuditEntry>> LoadEntriesInternalAsync()
        {
            if (!File.Exists(_filePath))
            {
                return new List<AuditEntry>();
            }

            try
            {
                using var stream = File.OpenRead(_filePath);
                var entries = await JsonSerializer.DeserializeAsync<List<AuditEntry>>(stream, JsonOptions);
                return entries ?? new List<AuditEntry>();
            }
            catch
            {
                return new List<AuditEntry>();
            }
        }

        private async Task SaveEntriesInternalAsync(List<AuditEntry> entries)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions);
        }
    }
}
