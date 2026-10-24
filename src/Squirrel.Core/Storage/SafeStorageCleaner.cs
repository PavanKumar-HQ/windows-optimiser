using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.Core.Audit;
using Squirrel.Core.Safety;

namespace Squirrel.Core.Storage
{
    public class CleanupResult
    {
        public string TargetName { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public int DeletedFilesCount { get; set; }
        public long DeletedBytes { get; set; }
        public double DeletedMb => DeletedBytes / (1024.0 * 1024.0);
        public int SkippedFilesCount { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public bool IsDryRun { get; set; }
        public bool Succeeded { get; set; }
    }

    /// <summary>
    /// Executes safe storage cleanup strictly upon explicit user approval.
    /// Handles locked files gracefully and preserves personal data.
    /// </summary>
    public class SafeStorageCleaner
    {
        private readonly IAuditLedger? _auditLedger;

        public SafeStorageCleaner(IAuditLedger? auditLedger = null)
        {
            _auditLedger = auditLedger;
        }

        public async Task<CleanupResult> CleanTargetAsync(
            CleanupTarget target,
            bool userConfirmed,
            bool dryRun = false,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new CleanupResult
            {
                TargetName = target.Name,
                TargetPath = target.DirectoryPath,
                IsDryRun = dryRun
            };

            if (!userConfirmed && !dryRun)
            {
                result.Succeeded = false;
                result.Warnings.Add("Action aborted: Explicit user approval was not provided.");
                return result;
            }

            if (!SafetyGuard.IsPathSafeForCleanup(target.DirectoryPath, out var reason))
            {
                result.Succeeded = false;
                result.Warnings.Add($"Safety Guard Block: {reason}");
                return result;
            }

            if (!Directory.Exists(target.DirectoryPath))
            {
                result.Succeeded = true;
                result.Warnings.Add("Directory does not exist or was already cleaned.");
                return result;
            }

            progress?.Report($"{(dryRun ? "[DRY RUN] Simulating" : "Cleaning")} {target.Name}...");

            await Task.Run(() =>
            {
                var dirInfo = new DirectoryInfo(target.DirectoryPath);
                DeleteDirectoryContentsSafely(dirInfo, result, dryRun, cancellationToken);
            }, cancellationToken);

            result.Succeeded = true;

            // Log action in audit ledger if not a dry-run
            if (!dryRun && _auditLedger != null && result.DeletedFilesCount > 0)
            {
                await _auditLedger.RecordEntryAsync(new AuditEntry
                {
                    ActionType = "StorageCleanup",
                    TargetName = target.Name,
                    TargetIdentifier = target.DirectoryPath,
                    Description = $"Cleaned {result.DeletedFilesCount} files ({result.DeletedMb:F1} MB reclaimed). Skipped {result.SkippedFilesCount} in-use files.",
                    PreviousState = $"{target.FileCount} files ({target.SizeMb:F1} MB)",
                    NewState = $"{result.SkippedFilesCount} files remaining",
                    IsReversible = false,
                    Status = "Completed"
                });
            }

            return result;
        }

        private static void DeleteDirectoryContentsSafely(
            DirectoryInfo rootDir,
            CleanupResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            try
            {
                // Files in root
                foreach (var file in rootDir.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var length = file.Length;

                        // Check if file is locked / in use by another process
                        bool isLocked = false;
                        try
                        {
                            using (file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                            }
                        }
                        catch (IOException)
                        {
                            isLocked = true;
                        }

                        if (isLocked)
                        {
                            result.SkippedFilesCount++;
                            continue;
                        }

                        if (!dryRun)
                        {
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                        }
                        result.DeletedFilesCount++;
                        result.DeletedBytes += length;
                    }
                    catch (IOException)
                    {
                        // File is currently locked / open by an active process — perfectly normal and safe to skip
                        result.SkippedFilesCount++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        result.SkippedFilesCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Could not delete {file.Name}: {ex.Message}");
                        result.SkippedFilesCount++;
                    }
                }

                // Subdirectories in root
                foreach (var subDir in rootDir.EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DeleteSubdirectorySafely(subDir, result, dryRun, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Directory access error: {ex.Message}");
            }
        }

        private static void DeleteSubdirectorySafely(
            DirectoryInfo subDir,
            CleanupResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (var file in subDir.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var length = file.Length;

                        // Check if file is locked / in use by another process
                        bool isLocked = false;
                        try
                        {
                            using (file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                            }
                        }
                        catch (IOException)
                        {
                            isLocked = true;
                        }

                        if (isLocked)
                        {
                            result.SkippedFilesCount++;
                            continue;
                        }

                        if (!dryRun)
                        {
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                        }
                        result.DeletedFilesCount++;
                        result.DeletedBytes += length;
                    }
                    catch (IOException)
                    {
                        result.SkippedFilesCount++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        result.SkippedFilesCount++;
                    }
                }

                foreach (var nested in subDir.EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DeleteSubdirectorySafely(nested, result, dryRun, cancellationToken);
                }

                // Delete directory if empty
                if (!dryRun)
                {
                    try
                    {
                        subDir.Delete(false);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
