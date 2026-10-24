using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.Core.Safety;

namespace Squirrel.Core.Diagnostics
{
    /// <summary>
    /// Analyzes real CPU and memory usage using repeated samples to distinguish sustained bottlenecks from transient spikes.
    /// Tailored for dual-core systems (AMD A4-4350B) where a single sustained background task causes severe UI lag.
    /// </summary>
    public class BottleneckDetector
    {
        private readonly ISystemMonitor _systemMonitor;

        // On a 2-core machine, anything above 20% total CPU is consuming ~40% of an entire CPU core.
        private const double SustainedProcessCpuThresholdPercent = 15.0;

        public BottleneckDetector(ISystemMonitor systemMonitor)
        {
            _systemMonitor = systemMonitor ?? throw new ArgumentNullException(nameof(systemMonitor));
        }

        private class ProcessTracker
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public string MainWindowTitle { get; set; } = string.Empty;
            public TimeSpan LastTotalProcessorTime { get; set; }
            public double LastWorkingSetMb { get; set; }
            public List<double> CpuSamples { get; } = new List<double>();
            public int HighCpuHits { get; set; }
        }

        public async Task<BottleneckReport> RunDiagnosticAsync(
            int sampleCount = 5,
            int intervalMs = 1500,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (sampleCount < 2) sampleCount = 2;
            if (intervalMs < 500) intervalMs = 500;

            var report = new BottleneckReport
            {
                SampleCount = sampleCount,
                TotalSamplingDuration = TimeSpan.FromMilliseconds(sampleCount * intervalMs)
            };

            var systemCpuSamples = new List<double>();
            var trackers = new Dictionary<int, ProcessTracker>();
            var logicalCores = Math.Max(1, Environment.ProcessorCount);

            progress?.Report($"Starting diagnostic baseline ({sampleCount} samples, {intervalMs / 1000.0:F1}s intervals)...");

            DateTime previousWallTime = DateTime.UtcNow;

            for (int i = 0; i < sampleCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentWallTime = DateTime.UtcNow;
                var wallDeltaMs = (currentWallTime - previousWallTime).TotalMilliseconds;
                previousWallTime = currentWallTime;

                var snapshot = _systemMonitor.GetSnapshot();
                systemCpuSamples.Add(snapshot.TotalCpuPercent);
                report.MemoryUsagePercent = snapshot.MemoryUsagePercent;
                report.AvailableMemoryMb = snapshot.AvailableMemoryMb;

                // Capture current processes
                var currentProcesses = Process.GetProcesses();
                try
                {
                    foreach (var proc in currentProcesses)
                    {
                        try
                        {
                            var pid = proc.Id;
                            if (pid == 0) continue; // Idle process

                            var currentProcTime = proc.TotalProcessorTime;
                            var currentWorkingSetMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                            var windowTitle = string.Empty;
                            try { windowTitle = proc.MainWindowTitle; } catch { }

                            if (!trackers.TryGetValue(pid, out var tracker))
                            {
                                tracker = new ProcessTracker
                                {
                                    ProcessId = pid,
                                    ProcessName = proc.ProcessName,
                                    MainWindowTitle = windowTitle,
                                    LastTotalProcessorTime = currentProcTime,
                                    LastWorkingSetMb = currentWorkingSetMb
                                };
                                trackers[pid] = tracker;
                            }
                            else
                            {
                                if (wallDeltaMs > 100)
                                {
                                    var procDeltaMs = (currentProcTime - tracker.LastTotalProcessorTime).TotalMilliseconds;
                                    var cpuPercent = (procDeltaMs / (wallDeltaMs * logicalCores)) * 100.0;
                                    cpuPercent = Math.Clamp(cpuPercent, 0.0, 100.0);

                                    tracker.CpuSamples.Add(cpuPercent);
                                    if (cpuPercent >= SustainedProcessCpuThresholdPercent)
                                    {
                                        tracker.HighCpuHits++;
                                    }
                                }

                                tracker.LastTotalProcessorTime = currentProcTime;
                                tracker.LastWorkingSetMb = currentWorkingSetMb;
                                if (!string.IsNullOrEmpty(windowTitle))
                                {
                                    tracker.MainWindowTitle = windowTitle;
                                }
                            }
                        }
                        catch
                        {
                            // Process may have exited or access denied
                        }
                    }
                }
                finally
                {
                    foreach (var p in currentProcesses)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }

                progress?.Report($"Sample {i + 1}/{sampleCount} recorded: System CPU {snapshot.TotalCpuPercent:F1}%, RAM {snapshot.MemoryUsagePercent:F1}%");

                if (i < sampleCount - 1)
                {
                    await Task.Delay(intervalMs, cancellationToken);
                }
            }

            // Calculate aggregate statistics
            report.AverageSystemCpuPercent = systemCpuSamples.Count > 0 ? systemCpuSamples.Average() : 0.0;
            report.PeakSystemCpuPercent = systemCpuSamples.Count > 0 ? systemCpuSamples.Max() : 0.0;

            foreach (var kvp in trackers)
            {
                var tracker = kvp.Value;
                if (tracker.CpuSamples.Count == 0) continue;

                var avgCpu = tracker.CpuSamples.Average();
                var peakCpu = tracker.CpuSamples.Max();
                var isProtected = SafetyGuard.IsProtectedEntity(tracker.ProcessName, out var reason);

                var sample = new ProcessSample
                {
                    ProcessId = tracker.ProcessId,
                    ProcessName = tracker.ProcessName,
                    MainWindowTitle = tracker.MainWindowTitle,
                    WorkingSetMb = tracker.LastWorkingSetMb,
                    AverageCpuPercent = avgCpu,
                    PeakCpuPercent = peakCpu,
                    HighCpuSampleCount = tracker.HighCpuHits,
                    TotalSamplesObserved = tracker.CpuSamples.Count,
                    IsProtected = isProtected,
                    ProtectionReason = reason
                };

                // Classify: Sustained vs Transient
                if (sample.IsSustainedOffender)
                {
                    report.SustainedCpuOffenders.Add(sample);
                }
                else if (peakCpu >= SustainedProcessCpuThresholdPercent && avgCpu < SustainedProcessCpuThresholdPercent)
                {
                    report.TransientSpikeProcesses.Add(sample);
                }

                if (sample.WorkingSetMb > 250.0)
                {
                    report.TopMemoryConsumers.Add(sample);
                }
            }

            // Order lists
            report.SustainedCpuOffenders = report.SustainedCpuOffenders.OrderByDescending(p => p.AverageCpuPercent).ToList();
            report.TransientSpikeProcesses = report.TransientSpikeProcesses.OrderByDescending(p => p.PeakCpuPercent).Take(8).ToList();
            report.TopMemoryConsumers = report.TopMemoryConsumers.OrderByDescending(p => p.WorkingSetMb).Take(8).ToList();

            // Generate plain-English findings
            GenerateFindings(report);

            return report;
        }

        private static void GenerateFindings(BottleneckReport report)
        {
            if (report.AverageSystemCpuPercent > 65.0)
            {
                report.Findings.Add($"High sustained total CPU load: averaged {report.AverageSystemCpuPercent:F1}% over {report.SampleCount} samples (peak {report.PeakSystemCpuPercent:F1}%).");
            }
            else
            {
                report.Findings.Add($"Average system CPU load was moderate ({report.AverageSystemCpuPercent:F1}%, peak {report.PeakSystemCpuPercent:F1}%).");
            }

            if (report.SustainedCpuOffenders.Count > 0)
            {
                foreach (var offender in report.SustainedCpuOffenders)
                {
                    var protText = offender.IsProtected ? $" [Protected: {offender.ProtectionReason}]" : " [User / Third-party App]";
                    report.Findings.Add($"Sustained CPU Bottleneck: '{offender.ProcessName}' averaged {offender.AverageCpuPercent:F1}% CPU (peak {offender.PeakCpuPercent:F1}% across {offender.HighCpuSampleCount} of {offender.TotalSamplesObserved} samples){protText}.");

                    if (!offender.IsProtected)
                    {
                        report.EvidenceBasedSuggestions.Add($"Investigate or close '{offender.ProcessName}' — it is using {offender.AverageCpuPercent:F1}% of your dual-core CPU continuously.");
                    }
                    else if (offender.ProcessName.Equals("SearchIndexer", StringComparison.OrdinalIgnoreCase))
                    {
                        report.EvidenceBasedSuggestions.Add("Windows Search Indexer is actively indexing files. Consider pausing or reducing indexed locations in Windows Settings.");
                    }
                    else if (offender.ProcessName.Equals("MsMpEng", StringComparison.OrdinalIgnoreCase))
                    {
                        report.EvidenceBasedSuggestions.Add("Windows Defender antimalware service is currently running a real-time scan. This will normalize once current file activity subsides.");
                    }
                }
            }
            else
            {
                report.Findings.Add("No single process sustained abnormally high CPU across the sampling window.");
            }

            if (report.HasMemoryPressure)
            {
                report.Findings.Add($"Memory pressure detected: {report.MemoryUsagePercent:F1}% RAM in use ({report.AvailableMemoryMb:F0} MB free out of 8 GB).");
                var topMem = report.TopMemoryConsumers.FirstOrDefault();
                if (topMem != null)
                {
                    report.EvidenceBasedSuggestions.Add($"Top memory consumer is '{topMem.ProcessName}' using {topMem.WorkingSetMb:F0} MB RAM.");
                }
            }
            else
            {
                report.Findings.Add($"Memory usage is healthy: {report.MemoryUsagePercent:F1}% in use ({report.AvailableMemoryMb:F0} MB free).");
            }
        }
    }
}
