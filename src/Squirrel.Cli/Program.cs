using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.Core.Actions;
using Squirrel.Core.Audit;
using Squirrel.Core.Diagnostics;
using Squirrel.Core.Processes;
using Squirrel.Core.Recommendations;
using Squirrel.Core.Safety;
using Squirrel.Core.Storage;

namespace Squirrel.Cli
{
    internal class Program
    {
        private static readonly ISystemMonitor Monitor = new WindowsSystemMonitor();
        private static readonly IAuditLedger Ledger = new LocalJsonAuditLedger();
        private static readonly IStartupAnalyzer StartupAnalyzer = new WindowsStartupAnalyzer();
        private static readonly IStorageAnalyzer StorageAnalyzer = new WindowsStorageAnalyzer();
        private static readonly IRecommendationEngine RecommendationEngine = new WindowsRecommendationEngine(Ledger);
        private static readonly BottleneckDetector BottleneckDetector = new BottleneckDetector(Monitor);
        private static readonly SafeStorageCleaner StorageCleaner = new SafeStorageCleaner(Ledger);

        static async Task Main(string[] args)
        {
            Console.Title = "Squirrel — Personal Laptop Diagnostic & Optimizer (LapMac)";
            PrintBanner();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n=== SQUIRREL MAIN MENU ===");
                Console.ResetColor();
                Console.WriteLine("1. [Live Monitor] Real-time CPU, RAM, Disk, & Squirrel Footprint");
                Console.WriteLine("2. [Bottleneck Scan] 5-Sample Sustained CPU & Memory Diagnosis");
                Console.WriteLine("3. [Startup Apps] Inspect & Manage Startup Programs (Safe & Reversible)");
                Console.WriteLine("4. [Storage Analysis] Review-First Safe Storage Cleanup");
                Console.WriteLine("5. [Recommendations] Evidence-Based Performance Proposals");
                Console.WriteLine("6. [Audit History] View Past System Actions & Revert Changes");
                Console.WriteLine("0. Exit");
                Console.Write("\nSelect an option [0-6]: ");

                var choice = Console.ReadLine()?.Trim();
                switch (choice)
                {
                    case "1":
                        await RunLiveMonitorAsync();
                        break;
                    case "2":
                        await RunBottleneckDiagnosticAsync();
                        break;
                    case "3":
                        await ManageStartupAppsAsync();
                        break;
                    case "4":
                        await ReviewAndCleanStorageAsync();
                        break;
                    case "5":
                        await ViewRecommendationsAsync();
                        break;
                    case "6":
                        await ViewAuditHistoryAsync();
                        break;
                    case "0":
                        Console.WriteLine("Exiting Squirrel. Goodbye!");
                        return;
                    default:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Invalid option. Please enter a number between 0 and 6.");
                        Console.ResetColor();
                        break;
                }
            }
        }

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(@"
   ___ ____  _   _ ___ ____  ____  _____ _     
  / __/ __ \| | | |_ _|  _ \|  _ \| ____| |    
  \__ \ | | | | | || || |_) | |_) |  _| | |    
  ___) | |_| | |_| || ||  _ <|  _ <| |___| |___ 
 |____/\___\_\\___/|___|_| \_\_| \_\_____|_____|
");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" Squirrel Personal Diagnostic & Responsiveness Utility");
            Console.WriteLine(" Tailored for Windows 10 Pro (AMD PRO A4-4350B, 8 GB RAM, LapMac)");
            Console.WriteLine(" [Strict Safety Active: Microsoft Office & Windows Core Protected]");
            Console.ResetColor();
        }

        private static async Task RunLiveMonitorAsync()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== LIVE SYSTEM MONITOR (Press [Q] or [Enter] to return) ===");
            Console.ResetColor();

            var cts = new CancellationTokenSource();
            var monitorTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var snapshot = Monitor.GetSnapshot();
                    Console.Write($"\rCPU: {snapshot.TotalCpuPercent,5:F1}% | RAM: {snapshot.UsedMemoryMb,5:F0}/{snapshot.TotalMemoryMb,5:F0} MB ({snapshot.MemoryUsagePercent,4:F1}%) | Free Disk: {snapshot.SystemDriveFreeGb,5:F1} GB | Squirrel: {snapshot.SquirrelWorkingSetMb,4:F1} MB RAM (CPU {snapshot.SquirrelCpuPercent,4:F1}%)   ");
                    await Task.Delay(1000);
                }
            });

            Console.ReadLine();
            cts.Cancel();
            await monitorTask;
            Console.WriteLine();
        }

        private static async Task RunBottleneckDiagnosticAsync()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== RUNNING MULTI-SAMPLE BOTTLENECK DIAGNOSIS ===");
            Console.ResetColor();
            Console.WriteLine("Taking 5 repeated samples over 7.5 seconds to distinguish sustained bottlenecks from transient spikes...\n");

            var progress = new Progress<string>(msg =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" -> {msg}");
                Console.ResetColor();
            });

            var report = await BottleneckDetector.RunDiagnosticAsync(sampleCount: 5, intervalMs: 1500, progress: progress);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n--- DIAGNOSTIC RESULTS ---");
            Console.ResetColor();
            Console.WriteLine($"Average Total CPU Load: {report.AverageSystemCpuPercent:F1}% (Peak: {report.PeakSystemCpuPercent:F1}%)");
            Console.WriteLine($"Current Memory Usage:   {report.MemoryUsagePercent:F1}% ({report.AvailableMemoryMb:F0} MB Available)");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[Findings]");
            Console.ResetColor();
            foreach (var finding in report.Findings)
            {
                Console.WriteLine($" • {finding}");
            }

            if (report.SustainedCpuOffenders.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[Sustained High CPU Offenders]");
                Console.ResetColor();
                foreach (var offender in report.SustainedCpuOffenders)
                {
                    var protNote = offender.IsProtected ? $" (PROTECTED: {offender.ProtectionReason})" : " (Third-party process)";
                    Console.WriteLine($" • {offender.ProcessName,-25} Avg CPU: {offender.AverageCpuPercent,5:F1}% (Peak: {offender.PeakCpuPercent,5:F1}% in {offender.HighCpuSampleCount}/{offender.TotalSamplesObserved} samples) RAM: {offender.WorkingSetMb,5:F0} MB{protNote}");
                }
            }

            if (report.EvidenceBasedSuggestions.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[Evidence-Based Suggestions]");
                Console.ResetColor();
                foreach (var suggestion in report.EvidenceBasedSuggestions)
                {
                    Console.WriteLine($" -> {suggestion}");
                }
            }

            Console.WriteLine("\nPress Enter to return to menu...");
            Console.ReadLine();
        }

        private static async Task ManageStartupAppsAsync()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== STARTUP PROGRAMS INVESTIGATION ===");
            Console.ResetColor();

            var items = await StartupAnalyzer.GetStartupItemsAsync();
            if (items.Count == 0)
            {
                Console.WriteLine("No startup items found in standard registry or startup folder locations.");
                Console.WriteLine("Press Enter to return to menu...");
                Console.ReadLine();
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var status = item.IsEnabled ? "ENABLED " : "DISABLED";
                var safety = item.CanSafelyDisable ? "[Safe to Disable]" : "[PROTECTED: DO NOT DISABLE]";

                if (!item.CanSafelyDisable)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                }
                else if (item.Impact == StartupImpact.High)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.White;
                }

                Console.WriteLine($"{i + 1,2}. [{status}] {item.Name,-22} Impact: {item.Impact,-6} {safety}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Command: {item.CommandLine}");
                Console.WriteLine($"    Notes:   {item.SafetyExplanation}");
                Console.ResetColor();
            }

            Console.WriteLine("\nOptions: Enter item number to toggle/disable, or '0' to return: ");
            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int selectedIndex) && selectedIndex > 0 && selectedIndex <= items.Count)
            {
                var selected = items[selectedIndex - 1];
                if (!selected.CanSafelyDisable)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\nSafety Block: '{selected.Name}' is protected ({selected.SafetyExplanation}). Cannot disable.");
                    Console.ResetColor();
                }
                else if (selected.IsEnabled)
                {
                    Console.Write($"\nAre you sure you want to disable startup for '{selected.Name}'? (y/N): ");
                    var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (confirm == "y" || confirm == "yes")
                    {
                        var action = new DisableStartupItemAction(selected, Ledger);
                        var result = await action.ExecuteAsync(userConfirmed: true);
                        if (result.Succeeded)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"Success: {result.Message}");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Failed: {result.Message}");
                        }
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Action cancelled.");
                    }
                }
                else
                {
                    Console.Write($"\nRe-enable startup for '{selected.Name}'? (y/N): ");
                    var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (confirm == "y" || confirm == "yes")
                    {
                        var action = new DisableStartupItemAction(selected, Ledger);
                        var result = await action.RollbackAsync();
                        if (result.Succeeded)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"Success: {result.Message}");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Failed: {result.Message}");
                        }
                        Console.ResetColor();
                    }
                }

                Console.WriteLine("Press Enter to continue...");
                Console.ReadLine();
            }
        }

        private static async Task ReviewAndCleanStorageAsync()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== REVIEW-FIRST SAFE STORAGE CLEANUP ===");
            Console.ResetColor();
            Console.WriteLine("Scanning genuinely safe cleanup targets (personal files and Office are strictly protected)...\n");

            var progress = new Progress<string>(msg =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" -> {msg}");
                Console.ResetColor();
            });

            var report = await StorageAnalyzer.AnalyzeStorageAsync(progress);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nPrimary SSD Free Space: {report.DriveFreeGb:F1} GB of {report.DriveTotalGb:F1} GB ({report.DriveFreePercent:F1}% Free)");
            Console.WriteLine($"Total Safe Reclaimable Space: {report.TotalReclaimableMb:F1} MB\n");
            Console.ResetColor();

            if (report.Targets.Count == 0)
            {
                Console.WriteLine("No temporary cache files found to clean.");
                Console.WriteLine("Press Enter to return to menu...");
                Console.ReadLine();
                return;
            }

            for (int i = 0; i < report.Targets.Count; i++)
            {
                var target = report.Targets[i];
                Console.WriteLine($"{i + 1,2}. {target.Name,-32} {target.FileCount,5} files ({target.SizeMb,7:F1} MB)");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Path:  {target.DirectoryPath}");
                Console.WriteLine($"    Notes: {target.SafetyNotes}");
                Console.ResetColor();
            }

            Console.WriteLine("\nReview Options:");
            Console.WriteLine(" D. Dry-Run Simulation (calculates exact items, deletes nothing)");
            Console.WriteLine(" A. Clean All Safe Targets (requires explicit approval)");
            Console.WriteLine(" 1-{0}. Clean specific target number", report.Targets.Count);
            Console.WriteLine(" 0. Return to menu");
            Console.Write("\nSelect action: ");

            var input = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (input == "D")
            {
                Console.WriteLine("\n--- SIMULATING CLEANUP (DRY RUN) ---");
                foreach (var t in report.Targets.Where(target => target.IsSafeForCleanup))
                {
                    var res = await StorageCleaner.CleanTargetAsync(t, userConfirmed: true, dryRun: true);
                    Console.WriteLine($"[Dry Run] {t.Name}: Evaluated {res.DeletedFilesCount} candidate files ({res.DeletedMb:F1} MB). Skipped: {res.SkippedFilesCount}");
                }
                Console.WriteLine("\nDry run complete. No files were removed from disk.");
            }
            else if (input == "A")
            {
                var safeTargets = report.Targets.Where(t => t.IsSafeForCleanup).ToList();
                if (safeTargets.Count == 0)
                {
                    Console.WriteLine("No targets are currently eligible for direct cleanup.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"\nType 'CONFIRM' to execute cleanup on {safeTargets.Count} approved safe targets: ");
                    Console.ResetColor();
                    if (Console.ReadLine()?.Trim() == "CONFIRM")
                    {
                        long totalReclaimed = 0;
                        int totalFiles = 0;
                        foreach (var t in safeTargets)
                        {
                            var res = await StorageCleaner.CleanTargetAsync(t, userConfirmed: true, dryRun: false);
                            totalReclaimed += res.DeletedBytes;
                            totalFiles += res.DeletedFilesCount;
                            Console.WriteLine($"Cleaned {t.Name}: {res.DeletedFilesCount} files ({res.DeletedMb:F1} MB reclaimed). In-use/protected skipped: {res.SkippedFilesCount}");
                        }
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\nCleanup Completed: Reclaimed {totalReclaimed / (1024.0 * 1024.0):F1} MB across {totalFiles} files.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Cleanup cancelled.");
                    }
                }
            }
            else if (int.TryParse(input, out int idx) && idx > 0 && idx <= report.Targets.Count)
            {
                var t = report.Targets[idx - 1];
                if (!t.IsSafeForCleanup)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\nNotice: '{t.Name}' is disabled for direct cleanup: {t.SafetyNotes}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"\nClean '{t.Name}' ({t.SizeMb:F1} MB)? (y/N): ");
                    Console.ResetColor();
                    if (Console.ReadLine()?.Trim().ToLowerInvariant() == "y")
                    {
                        var res = await StorageCleaner.CleanTargetAsync(t, userConfirmed: true, dryRun: false);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Cleaned {t.Name}: {res.DeletedFilesCount} files ({res.DeletedMb:F1} MB reclaimed). In-use skipped: {res.SkippedFilesCount}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Cancelled.");
                    }
                }
            }

            Console.WriteLine("\nPress Enter to return to menu...");
            Console.ReadLine();
        }

        private static async Task ViewRecommendationsAsync()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== EVIDENCE-BASED RECOMMENDATIONS ===");
            Console.ResetColor();
            Console.WriteLine("Gathering system baseline and analyzing responsiveness factors...\n");

            var metrics = Monitor.GetSnapshot();
            var startupItems = await StartupAnalyzer.GetStartupItemsAsync();
            var storageReport = await StorageAnalyzer.AnalyzeStorageAsync();

            var recommendations = RecommendationEngine.Evaluate(metrics, null, startupItems, storageReport);

            if (recommendations.Count == 0)
            {
                Console.WriteLine("No current performance recommendations. System is operating within normal parameters.");
            }
            else
            {
                for (int i = 0; i < recommendations.Count; i++)
                {
                    var r = recommendations[i];
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{i + 1}. [{r.Category}] {r.Title}");
                    Console.ResetColor();
                    Console.WriteLine($"   Explanation:      {r.Explanation}");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"   Evidence:         {r.Evidence}");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"   Expected Benefit: {r.ExpectedBenefit}");
                    Console.ResetColor();
                    Console.WriteLine($"   Risk Level:       {r.RiskLevel} (Requires User Approval: {r.RequiresUserApproval})");
                    Console.WriteLine();
                }
            }

            Console.WriteLine("Press Enter to return to menu...");
            Console.ReadLine();
        }

        private static async Task ViewAuditHistoryAsync()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== LOCAL AUDIT HISTORY LEDGER ===");
            Console.ResetColor();

            var entries = await Ledger.GetEntriesAsync();
            if (entries.Count == 0)
            {
                Console.WriteLine("No recorded system changes in audit ledger.");
                Console.WriteLine("Press Enter to return to menu...");
                Console.ReadLine();
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rev = entry.IsReversible ? "[Reversible]" : "[Irreversible]";
                Console.WriteLine($"{i + 1,2}. [{entry.TimestampUtc:yyyy-MM-dd HH:mm}] {entry.ActionType,-20} {rev} Status: {entry.Status}");
                Console.WriteLine($"    Target:  {entry.TargetName} ({entry.TargetIdentifier})");
                Console.WriteLine($"    Details: {entry.Description}");
                Console.ResetColor();
            }

            Console.WriteLine("\nPress Enter to return to menu...");
            Console.ReadLine();
        }
    }
}
