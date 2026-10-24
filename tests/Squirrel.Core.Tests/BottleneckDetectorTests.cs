using System;
using System.Threading.Tasks;
using Squirrel.Core.Diagnostics;
using Xunit;

namespace Squirrel.Core.Tests
{
    public class MockSystemMonitor : ISystemMonitor
    {
        public double CpuToReturn { get; set; } = 40.0;
        public double MemoryUsagePercentToReturn { get; set; } = 60.0;

        public SystemMetrics GetSnapshot()
        {
            return new SystemMetrics
            {
                TotalCpuPercent = CpuToReturn,
                TotalMemoryMb = 8192,
                UsedMemoryMb = 8192 * (MemoryUsagePercentToReturn / 100.0),
                AvailableMemoryMb = 8192 * (1.0 - (MemoryUsagePercentToReturn / 100.0)),
                LogicalCoreCount = 2,
                SystemDriveTotalGb = 238,
                SystemDriveFreeGb = 144,
                SquirrelCpuPercent = 0.2,
                SquirrelWorkingSetMb = 35.0
            };
        }

        public Task<SystemMetrics> GetSnapshotAsync() => Task.FromResult(GetSnapshot());

        public void Dispose() { }
    }

    public class BottleneckDetectorTests
    {
        [Fact]
        public async Task RunDiagnosticAsync_CompletesAndCalculatesSystemAverages()
        {
            var mockMonitor = new MockSystemMonitor
            {
                CpuToReturn = 55.0,
                MemoryUsagePercentToReturn = 62.0
            };

            var detector = new BottleneckDetector(mockMonitor);
            var report = await detector.RunDiagnosticAsync(sampleCount: 2, intervalMs: 500);

            Assert.NotNull(report);
            Assert.Equal(2, report.SampleCount);
            Assert.True(report.AverageSystemCpuPercent >= 0.0);
            Assert.NotEmpty(report.Findings);
        }

        [Fact]
        public async Task RunDiagnosticAsync_DetectsElevatedMemoryPressure()
        {
            var mockMonitor = new MockSystemMonitor
            {
                CpuToReturn = 10.0,
                MemoryUsagePercentToReturn = 92.0 // > 85% threshold
            };

            var detector = new BottleneckDetector(mockMonitor);
            var report = await detector.RunDiagnosticAsync(sampleCount: 2, intervalMs: 500);

            Assert.True(report.HasMemoryPressure);
            Assert.Contains(report.Findings, f => f.Contains("Memory pressure", StringComparison.OrdinalIgnoreCase));
        }
    }
}
