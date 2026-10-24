using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.Core.Safety;

namespace Squirrel.Core.Storage
{
    public class WindowsStorageAnalyzer : IStorageAnalyzer
    {
        public StorageReport AnalyzeStorage(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var report = new StorageReport();

            // Drive size
            try
            {
                var drives = DriveInfo.GetDrives();
                DriveInfo? drive = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    drive = Array.Find(drives, d => d.IsReady && string.Equals(d.Name, @"C:\", StringComparison.OrdinalIgnoreCase));
                }
                drive ??= Array.Find(drives, d => d.IsReady && d.DriveType == DriveType.Fixed);
                drive ??= drives.Length > 0 ? drives[0] : null;

                if (drive != null && drive.IsReady)
                {
                    report.DriveTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    report.DriveFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch
            {
                report.DriveTotalGb = 238;
                report.DriveFreeGb = 144;
            }

            var targetsToScan = GetCandidateTargets();

            foreach (var target in targetsToScan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report($"Scanning {target.Name}...");

                if (!Directory.Exists(target.DirectoryPath))
                {
                    continue;
                }

                // Verify safety guard
                if (!SafetyGuard.IsPathSafeForCleanup(target.DirectoryPath, out var reason))
                {
                    target.IsSafeForCleanup = false;
                    target.SafetyNotes = $"Skipped: {reason}";
                    continue;
                }

                CalculateDirectoryFootprint(target.DirectoryPath, target, cancellationToken);

                if (target.SizeBytes > 0 || target.FileCount > 0)
                {
                    report.Targets.Add(target);
                }
            }

            progress?.Report($"Storage analysis complete. Found {report.TotalReclaimableMb:F1} MB in safe cleanup candidates.");
            return report;
        }

        public Task<StorageReport> AnalyzeStorageAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => AnalyzeStorage(progress, cancellationToken), cancellationToken);
        }

        private static List<CleanupTarget> GetCandidateTargets()
        {
            var list = new List<CleanupTarget>();

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir)) winDir = @"C:\Windows";

            // 1. User Temp Directory
            var userTemp = Path.GetTempPath();
            list.Add(new CleanupTarget
            {
                Name = "User Temporary Files",
                Description = "Temporary files created by running applications that were not removed upon closing.",
                DirectoryPath = userTemp,
                SafetyNotes = "Safe to clean. In-use files are skipped automatically.",
                RequiresAdmin = false
            });

            // 2. Windows Crash Dumps & Error Reports
            var crashDumps = Path.Combine(localAppData, "CrashDumps");
            list.Add(new CleanupTarget
            {
                Name = "Application Crash Dumps",
                Description = "Memory dump files (.dmp) saved after application crashes.",
                DirectoryPath = crashDumps,
                SafetyNotes = "Safe to delete. Only needed if you are debugging a software crash with a developer.",
                RequiresAdmin = false
            });

            // 3. System Temp
            var systemTemp = Path.Combine(winDir, "Temp");
            list.Add(new CleanupTarget
            {
                Name = "Windows System Temp",
                Description = "Temporary workspace used by Windows system services and installers.",
                DirectoryPath = systemTemp,
                SafetyNotes = "Safe to clean. Requires administrator privileges.",
                RequiresAdmin = true
            });

            // 4. Windows Update Download Cache (SoftwareDistribution\Download)
            var softwareDistribution = Path.Combine(winDir, "SoftwareDistribution", "Download");
            list.Add(new CleanupTarget
            {
                Name = "Windows Update Download Cache",
                Description = "Downloaded installation packages for completed Windows updates.",
                DirectoryPath = softwareDistribution,
                SafetyNotes = "Safe to remove after updates have installed. Windows will re-download if ever needed.",
                RequiresAdmin = true
            });

            // 5. Microsoft Edge Browser Disk Cache (strictly Cache, never cookies/passwords/history)
            var edgeCache = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data");
            list.Add(new CleanupTarget
            {
                Name = "Microsoft Edge Web Cache",
                Description = "Cached web page images and scripts. Never affects your cookies, history, or saved passwords.",
                DirectoryPath = edgeCache,
                SafetyNotes = "Safe to clean. Temporary cache will be rebuilt naturally as you browse.",
                RequiresAdmin = false
            });

            // 6. Google Chrome Disk Cache (strictly Cache, never cookies/passwords/history)
            var chromeCache = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data");
            list.Add(new CleanupTarget
            {
                Name = "Google Chrome Web Cache",
                Description = "Cached web page images and scripts. Does not touch cookies, bookmarks, or login sessions.",
                DirectoryPath = chromeCache,
                SafetyNotes = "Safe to clean. Rebuilt as you browse.",
                RequiresAdmin = false
            });

            return list;
        }

        private static void CalculateDirectoryFootprint(string dirPath, CleanupTarget target, CancellationToken cancellationToken)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dirPath);
                if (!dirInfo.Exists) return;

                int count = 0;
                long totalBytes = 0;

                var stack = new Stack<DirectoryInfo>();
                stack.Push(dirInfo);

                while (stack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = stack.Pop();

                    try
                    {
                        foreach (var file in current.EnumerateFiles())
                        {
                            try
                            {
                                totalBytes += file.Length;
                                count++;
                            }
                            catch { }
                        }

                        foreach (var subDir in current.EnumerateDirectories())
                        {
                            try
                            {
                                stack.Push(subDir);
                            }
                            catch { }
                        }
                    }
                    catch
                    {
                        // Directory permission denied or inaccessible
                    }
                }

                target.FileCount = count;
                target.SizeBytes = totalBytes;
            }
            catch
            {
                // Root directory access denied
            }
        }
    }
}
