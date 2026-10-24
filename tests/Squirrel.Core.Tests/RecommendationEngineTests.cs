using System.Collections.Generic;
using System.Linq;
using Squirrel.Core.Diagnostics;
using Squirrel.Core.Processes;
using Squirrel.Core.Recommendations;
using Squirrel.Core.Storage;
using Xunit;

namespace Squirrel.Core.Tests
{
    public class RecommendationEngineTests
    {
        [Fact]
        public void Evaluate_SustainedCpuBottleneck_GeneratesEvidenceBasedRecommendation()
        {
            var engine = new WindowsRecommendationEngine();
            var metrics = new SystemMetrics { TotalMemoryMb = 8192, UsedMemoryMb = 4000 };
            var report = new BottleneckReport
            {
                SampleCount = 5,
                AverageSystemCpuPercent = 45.0,
                SustainedCpuOffenders = new List<ProcessSample>
                {
                    new ProcessSample
                    {
                        ProcessName = "BackgroundWorker",
                        AverageCpuPercent = 38.5,
                        PeakCpuPercent = 44.0,
                        HighCpuSampleCount = 5,
                        TotalSamplesObserved = 5,
                        IsProtected = false
                    }
                }
            };

            var recommendations = engine.Evaluate(metrics, report, new List<StartupItem>(), null);

            var cpuRec = recommendations.FirstOrDefault(r => r.Category == RecommendationCategory.CpuBottleneck);
            Assert.NotNull(cpuRec);
            Assert.Contains("BackgroundWorker", cpuRec.Title);
            Assert.Contains("38.5%", cpuRec.Evidence);
            Assert.True(cpuRec.RequiresUserApproval);
        }

        [Fact]
        public void Evaluate_StartupItems_ProtectsOfficeAndHardwareDrivers()
        {
            var engine = new WindowsRecommendationEngine();
            var metrics = new SystemMetrics { TotalMemoryMb = 8192, UsedMemoryMb = 4000 };
            var startupItems = new List<StartupItem>
            {
                new StartupItem
                {
                    Name = "OfficeClickToRun",
                    Category = "Microsoft Office",
                    CanSafelyDisable = false,
                    IsEnabled = true
                },
                new StartupItem
                {
                    Name = "RadeonSoftware",
                    Category = "Hardware Driver",
                    CanSafelyDisable = false,
                    IsEnabled = true
                },
                new StartupItem
                {
                    Name = "Discord",
                    Category = "Messaging / Media",
                    Impact = StartupImpact.High,
                    CanSafelyDisable = true,
                    IsEnabled = true,
                    CommandLine = @"C:\Users\LapMac\AppData\Local\Discord\app-1.0.9\Discord.exe"
                }
            };

            var recommendations = engine.Evaluate(metrics, null, startupItems, null);

            // Discord should be recommended to disable
            var discordRec = recommendations.FirstOrDefault(r => r.Title.Contains("Discord"));
            Assert.NotNull(discordRec);
            Assert.NotNull(discordRec.Action);

            // Office and Radeon driver must NEVER have disable recommendations
            Assert.DoesNotContain(recommendations, r => r.Title.Contains("OfficeClickToRun"));
            Assert.DoesNotContain(recommendations, r => r.Title.Contains("RadeonSoftware"));
        }

        [Fact]
        public void Evaluate_HighMemoryPressure_RecommendsRAMMitigation()
        {
            var engine = new WindowsRecommendationEngine();
            var metrics = new SystemMetrics
            {
                TotalMemoryMb = 7650,
                UsedMemoryMb = 6500, // > 80% RAM
                AvailableMemoryMb = 1150
            };

            var recommendations = engine.Evaluate(metrics, null, new List<StartupItem>(), null);

            var memRec = recommendations.FirstOrDefault(r => r.Category == RecommendationCategory.MemoryOptimization);
            Assert.NotNull(memRec);
            Assert.Contains("Memory", memRec.Title);
        }
    }
}
