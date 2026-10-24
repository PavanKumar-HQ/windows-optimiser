using System;
using System.Collections.Generic;

namespace Squirrel.Core.Diagnostics
{
    public class ProcessSample
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string MainWindowTitle { get; set; } = string.Empty;
        public double WorkingSetMb { get; set; }

        /// <summary>
        /// Total normalized system CPU percentage (0-100%).
        /// On a 2-core machine, 50% system CPU = 1 full saturated core.
        /// </summary>
        public double AverageCpuPercent { get; set; }
        public double PeakCpuPercent { get; set; }

        /// <summary>
        /// Single-core equivalent consumption on a 2-core AMD APU (e.g. 25% system CPU = 50% of 1 core).
        /// </summary>
        public double SingleCoreEquivalentPercent => Math.Min(100.0, AverageCpuPercent * 2.0);

        public int HighCpuSampleCount { get; set; }
        public int TotalSamplesObserved { get; set; }
        public bool IsProtected { get; set; }
        public string ProtectionReason { get; set; } = string.Empty;

        /// <summary>
        /// A process is classified as a sustained offender only if observed across at least 3 intervals
        /// and exceeding threshold in at least 60% of observed samples.
        /// </summary>
        public bool IsSustainedOffender => TotalSamplesObserved >= 3 && ((double)HighCpuSampleCount / TotalSamplesObserved) >= 0.6;
    }

    public class BottleneckReport
    {
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public int SampleCount { get; set; }
        public TimeSpan TotalSamplingDuration { get; set; }

        public double AverageSystemCpuPercent { get; set; }
        public double PeakSystemCpuPercent { get; set; }
        public double MemoryUsagePercent { get; set; }
        public double AvailableMemoryMb { get; set; }

        public List<ProcessSample> SustainedCpuOffenders { get; set; } = new List<ProcessSample>();
        public List<ProcessSample> TransientSpikeProcesses { get; set; } = new List<ProcessSample>();
        public List<ProcessSample> TopMemoryConsumers { get; set; } = new List<ProcessSample>();

        public List<string> Findings { get; set; } = new List<string>();
        public List<string> EvidenceBasedSuggestions { get; set; } = new List<string>();

        public bool HasCriticalCpuBottleneck => SustainedCpuOffenders.Count > 0 || AverageSystemCpuPercent > 70.0;
        public bool HasMemoryPressure => MemoryUsagePercent > 85.0 || AvailableMemoryMb < 1024.0;
    }
}
