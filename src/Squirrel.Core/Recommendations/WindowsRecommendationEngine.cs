using System;
using System.Collections.Generic;
using System.Linq;
using Squirrel.Core.Actions;
using Squirrel.Core.Audit;
using Squirrel.Core.Diagnostics;
using Squirrel.Core.Processes;
using Squirrel.Core.Storage;

namespace Squirrel.Core.Recommendations
{
    public class WindowsRecommendationEngine : IRecommendationEngine
    {
        private readonly IAuditLedger? _auditLedger;

        public WindowsRecommendationEngine(IAuditLedger? auditLedger = null)
        {
            _auditLedger = auditLedger;
        }

        public IReadOnlyList<Recommendation> Evaluate(
            SystemMetrics currentMetrics,
            BottleneckReport? bottleneckReport,
            IReadOnlyList<StartupItem> startupItems,
            StorageReport? storageReport)
        {
            var recommendations = new List<Recommendation>();

            // 1. Evaluate Sustained CPU Bottlenecks
            if (bottleneckReport != null && bottleneckReport.SustainedCpuOffenders.Count > 0)
            {
                foreach (var offender in bottleneckReport.SustainedCpuOffenders)
                {
                    if (!offender.IsProtected)
                    {
                        recommendations.Add(new Recommendation
                        {
                            Title = $"Mitigate High CPU Usage from '{offender.ProcessName}'",
                            Category = RecommendationCategory.CpuBottleneck,
                            Explanation = $"The process '{offender.ProcessName}' is persistently consuming CPU resources across repeated measurements.",
                            Evidence = $"Averaged {offender.AverageCpuPercent:F1}% CPU (peak {offender.PeakCpuPercent:F1}%) across {offender.HighCpuSampleCount} of {offender.TotalSamplesObserved} samples on your 2-core AMD APU.",
                            ExpectedBenefit = $"Reclaiming ~{offender.AverageCpuPercent:F0}% CPU will directly alleviate system lag and responsiveness bottlenecks.",
                            RiskLevel = ActionRiskLevel.Low,
                            RequiresUserApproval = true
                        });
                    }
                    else if (offender.ProcessName.Equals("SearchIndexer", StringComparison.OrdinalIgnoreCase))
                    {
                        recommendations.Add(new Recommendation
                        {
                            Title = "Limit Windows Search Indexer Scope",
                            Category = RecommendationCategory.CpuBottleneck,
                            Explanation = "Windows Search is indexing files in the background, consuming substantial CPU on dual-core processors.",
                            Evidence = $"SearchIndexer averaged {offender.AverageCpuPercent:F1}% CPU across {offender.HighCpuSampleCount} samples.",
                            ExpectedBenefit = "Limiting indexing to user profile folders only (via Indexing Options in Control Panel) stops excessive background drive crawls without breaking Start Menu search.",
                            RiskLevel = ActionRiskLevel.Safe,
                            RequiresUserApproval = true
                        });
                    }
                }
            }

            // 2. Evaluate Startup Item Bloat
            var safeStartupCandidates = startupItems.Where(s => s.IsEnabled && s.CanSafelyDisable).ToList();
            if (safeStartupCandidates.Count > 0)
            {
                foreach (var item in safeStartupCandidates.Where(s => s.Impact == StartupImpact.High || s.Impact == StartupImpact.Medium))
                {
                    recommendations.Add(new Recommendation
                    {
                        Title = $"Disable Startup for '{item.Name}'",
                        Category = RecommendationCategory.StartupOptimization,
                        Explanation = item.SafetyExplanation,
                        Evidence = $"Configured to start at boot ({item.LocationType}: {item.CommandLine}). Marked as {item.Impact} startup impact.",
                        ExpectedBenefit = "Reduces boot time and eliminates background memory footprint on startup. The app remains fully functional when opened manually.",
                        RiskLevel = ActionRiskLevel.Safe,
                        RequiresUserApproval = true,
                        Action = new DisableStartupItemAction(item, _auditLedger)
                    });
                }
            }

            // 3. Evaluate RAM Usage
            if (currentMetrics.MemoryUsagePercent > 75.0)
            {
                recommendations.Add(new Recommendation
                {
                    Title = "Address Elevated Memory Utilization",
                    Category = RecommendationCategory.MemoryOptimization,
                    Explanation = $"System is utilizing {currentMetrics.UsedMemoryMb:F0} MB of {currentMetrics.TotalMemoryMb:F0} MB RAM ({currentMetrics.MemoryUsagePercent:F1}%).",
                    Evidence = $"Only {currentMetrics.AvailableMemoryMb:F0} MB RAM available. On an 8 GB system with shared AMD GPU graphics, memory pressure triggers disk paging.",
                    ExpectedBenefit = "Closing unneeded background applications reduces SSD paging and improves active window responsiveness.",
                    RiskLevel = ActionRiskLevel.Safe,
                    RequiresUserApproval = true
                });
            }

            // 4. Safe Storage Cleanup (Review-First)
            if (storageReport != null && storageReport.TotalReclaimableMb > 200.0)
            {
                recommendations.Add(new Recommendation
                {
                    Title = "Review and Clean Temporary Cache Files",
                    Category = RecommendationCategory.SafeStorageCleanup,
                    Explanation = "Genuinely safe temporary files and update caches were detected. Personal files and Microsoft Office are strictly protected.",
                    Evidence = $"Found {storageReport.TotalReclaimableMb:F1} MB in {storageReport.Targets.Count} safe targets (e.g. user temp, crash dumps).",
                    ExpectedBenefit = $"Reclaims {storageReport.TotalReclaimableMb:F1} MB of storage space on SSD. Note: Storage cleaning frees disk space but does not resolve CPU bottlenecks.",
                    RiskLevel = ActionRiskLevel.Safe,
                    RequiresUserApproval = true
                });
            }

            // 5. Windows Responsiveness (Power Plan & Visual Effects on dual-core APU)
            recommendations.Add(new Recommendation
            {
                Title = "Maintain Balanced Power Plan for Dual-Core AMD APU",
                Category = RecommendationCategory.Responsiveness,
                Explanation = "The AMD PRO A4-4350B operates best under the standard Windows Balanced or AMD Balanced power profile, avoiding thermal throttling.",
                Evidence = "Hardware is 2-core / 2-logical-processor APU with 15W cTDP.",
                ExpectedBenefit = "Prevents premature thermal throttling under sustained loads.",
                RiskLevel = ActionRiskLevel.Safe,
                RequiresUserApproval = false
            });

            return recommendations;
        }
    }
}
