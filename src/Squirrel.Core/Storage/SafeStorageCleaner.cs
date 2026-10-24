using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// Executes safe storage cleanup strictly upon explicit user approval.
    /// Resists path traversal, avoids symlink/junction recursion, tolerates in-use files,
    /// verifies browser process state, and maintains accurate local audit records.
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

            if (!target.IsSafeForCleanup)
            {
                result.Succeeded = false;
                result.Warnings.Add($"Category disabled: {target.SafetyNotes}");
                result.Summary = "Cleanup refused: Target category is disabled for direct deletion.";
                return result;
            }

            if (!userConfirmed && !dryRun)
            {
                result.Succeeded = false;
                result.Warnings.Add("Action aborted: Explicit user approval was not provided.");
                result.Summary = "Aborted: User confirmation required.";
                return result;
            }

            if (!SafetyGuard.IsPathSafeForCleanup(target.DirectoryPath, out var reason))
            {
                result.Succeeded = false;
                result.Warnings.Add($"Safety Guard Block: {reason}");
                result.Summary = $"Safety violation: {reason}";
                return result;
            }

            // Check if active browser is running for browser caches
            if (target.Name.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0 && IsProcessActive("msedge"))
            {
                result.Succeeded = false;
                result.Warnings.Add("Microsoft Edge is currently running. Please close Edge to prevent cache file lock errors.");
                result.Summary = "Aborted: Close Microsoft Edge first.";
                return result;
            }

            if (target.Name.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0 && IsProcessActive("chrome"))
            {
                result.Succeeded = false;
                result.Warnings.Add("Google Chrome is currently running. Please close Chrome to prevent cache file lock errors.");
                result.Summary = "Aborted: Close Google Chrome first.";
                return result;
            }

            if (!Directory.Exists(target.DirectoryPath))
            {
                result.Succeeded = true;
                result.Warnings.Add("Directory does not exist or was already cleaned.");
                result.Summary = "Directory empty or not found.";
                return result;
            }

            progress?.Report($"{(dryRun ? "[DRY RUN] Simulating" : "Cleaning")} {target.Name}...");

            await Task.Run(() =>
            {
                var dirInfo = new DirectoryInfo(target.DirectoryPath);
                DeleteDirectoryContentsSafely(dirInfo, result, dryRun, cancellationToken);
            }, cancellationToken);

            result.Succeeded = true;
            result.Summary = dryRun
                ? $"[DRY RUN] Evaluated {result.DeletedFilesCount} candidate files ({result.DeletedMb:F1} MB). Zero files were deleted."
                : $"Cleaned {result.DeletedFilesCount} files ({result.DeletedMb:F1} MB reclaimed). Skipped {result.SkippedFilesCount} in-use/protected files.";

            // Log action in audit ledger only if not dry-run and files were processed
            if (!dryRun && _auditLedger != null && result.DeletedFilesCount > 0)
            {
                await _auditLedger.RecordEntryAsync(new AuditEntry
                {
                    ActionType = "StorageCleanup",
                    TargetName = target.Name,
                    TargetIdentifier = target.DirectoryPath,
                    Description = result.Summary,
                    PreviousState = $"{target.FileCount} files ({target.SizeMb:F1} MB)",
                    NewState = $"{result.SkippedFilesCount} files remaining",
                    IsReversible = false,
                    Status = "Completed"
                });
            }

            return result;
        }

        private static bool IsProcessActive(string processName)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                var active = procs.Length > 0;
                foreach (var p in procs) { try { p.Dispose(); } catch { } }
                return active;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteDirectoryContentsSafely(
            DirectoryInfo rootDir,
            CleanupResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            try
            {
                // Files directly inside root directory
                foreach (var file in rootDir.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        // Do not delete or follow symbolic links
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            result.SkippedFilesCount++;
                            continue;
                        }

                        var length = file.Length;

                        // Check if file is locked or in use by another process
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
                        // In-use locked file — safe to skip
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

                // Subdirectories in root — NEVER recurse into reparse points / directory junctions
                foreach (var subDir in rootDir.EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Directory junction or symlink — do NOT traverse into it
                        result.SkippedFilesCount++;
                        continue;
                    }

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
                // Never recurse into directory junctions or symbolic links
                if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    result.SkippedFilesCount++;
                    return;
                }

                foreach (var file in subDir.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            result.SkippedFilesCount++;
                            continue;
                        }

                        var length = file.Length;

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
                    if ((nested.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        result.SkippedFilesCount++;
                        continue;
                    }

                    DeleteSubdirectorySafely(nested, result, dryRun, cancellationToken);
                }

                // Delete directory if empty (never deletes root)
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
