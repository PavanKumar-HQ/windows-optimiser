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
                            Title = $"Investigate Background CPU Draw from '{offender.ProcessName}'",
                            Category = RecommendationCategory.CpuBottleneck,
                            Explanation = $"The process '{offender.ProcessName}' demonstrated persistent background CPU activity across repeated measurements.",
                            Evidence = $"Averaged {offender.AverageCpuPercent:F1}% system CPU (~{offender.SingleCoreEquivalentPercent:F0}% of a single core) across {offender.HighCpuSampleCount} of {offender.TotalSamplesObserved} samples on your 2-core AMD APU.",
                            ExpectedBenefit = $"Closing or configuring '{offender.ProcessName}' may reduce background CPU draw, helping active foreground applications respond faster.",
                            RiskLevel = ActionRiskLevel.Low,
                            RequiresUserApproval = true
                        });
                    }
                    else if (offender.ProcessName.Equals("SearchIndexer", StringComparison.OrdinalIgnoreCase))
                    {
                        recommendations.Add(new Recommendation
                        {
                            Title = "Limit Windows Search Indexer Scope via Windows Settings",
                            Category = RecommendationCategory.CpuBottleneck,
                            Explanation = "Windows Search is indexing files in the background, drawing CPU time on this dual-core processor.",
                            Evidence = $"SearchIndexer averaged {offender.AverageCpuPercent:F1}% system CPU across {offender.HighCpuSampleCount} samples.",
                            ExpectedBenefit = "Limiting indexing to essential user folders in Windows Settings > Search > Searching Windows reduces background disk and CPU activity without disabling search functionality.",
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
                        Evidence = $"Configured to launch automatically on logon ({item.LocationType}: {item.CommandLine}). Marked as {item.Impact} impact.",
                        ExpectedBenefit = "Stops the program from loading into RAM and consuming background CPU on boot. The program remains fully functional when opened manually.",
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
                    Title = "Review Elevated Memory Utilization",
                    Category = RecommendationCategory.MemoryOptimization,
                    Explanation = $"System is utilizing {currentMetrics.UsedMemoryMb:F0} MB of {currentMetrics.TotalMemoryMb:F0} MB RAM ({currentMetrics.MemoryUsagePercent:F1}%).",
                    Evidence = $"Only {currentMetrics.AvailableMemoryMb:F0} MB RAM available. On an 8 GB system with shared AMD GPU graphics, memory pressure increases SSD paging.",
                    ExpectedBenefit = "Closing unneeded background applications frees RAM and reduces SSD paging activity, which may help responsiveness.",
                    RiskLevel = ActionRiskLevel.Safe,
                    RequiresUserApproval = true
                });
            }

            // 4. Safe Storage Cleanup (Review-First)
            if (storageReport != null && storageReport.TotalReclaimableMb > 200.0)
            {
                recommendations.Add(new Recommendation
                {
                    Title = "Review Genuinely Safe Temporary Files",
                    Category = RecommendationCategory.SafeStorageCleanup,
                    Explanation = "Safe temporary application workspace files and crash dumps were identified. Personal files and Microsoft Office are strictly protected.",
                    Evidence = $"Found {storageReport.TotalReclaimableMb:F1} MB across verified safe cleanup categories (user temp, crash dumps).",
                    ExpectedBenefit = $"Reclaims {storageReport.TotalReclaimableMb:F1} MB of SSD space. Note: Storage cleaning frees disk space but does not solve CPU bottlenecks.",
                    RiskLevel = ActionRiskLevel.Safe,
                    RequiresUserApproval = true
                });
            }

            // 5. Windows Responsiveness (Power Plan on dual-core APU)
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
