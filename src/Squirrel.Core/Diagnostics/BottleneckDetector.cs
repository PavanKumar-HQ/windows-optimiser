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
    /// Resilient against PID reuse, process restarts, clock drift, and brief spikes.
    /// </summary>
    public class BottleneckDetector
    {
        private readonly ISystemMonitor _systemMonitor;

        // On a 2-core machine, 15% total system CPU represents 30% of an entire CPU core.
        private const double SustainedProcessCpuThresholdPercent = 15.0;

        public BottleneckDetector(ISystemMonitor systemMonitor)
        {
            _systemMonitor = systemMonitor ?? throw new ArgumentNullException(nameof(systemMonitor));
        }

        private class ProcessTracker
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public DateTime? StartTime { get; set; }
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

            var intervalStopwatch = new Stopwatch();

            for (int i = 0; i < sampleCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double elapsedMs = 0;
                if (i > 0)
                {
                    intervalStopwatch.Stop();
                    elapsedMs = intervalStopwatch.Elapsed.TotalMilliseconds;
                }

                var snapshot = _systemMonitor.GetSnapshot();
                systemCpuSamples.Add(snapshot.TotalCpuPercent);
                report.MemoryUsagePercent = snapshot.MemoryUsagePercent;
                report.AvailableMemoryMb = snapshot.AvailableMemoryMb;

                // Capture current running processes
                var currentProcesses = Process.GetProcesses();
                try
                {
                    foreach (var proc in currentProcesses)
                    {
                        try
                        {
                            var pid = proc.Id;
                            if (pid == 0) continue; // Skip System Idle process

                            TimeSpan currentProcTime;
                            try { currentProcTime = proc.TotalProcessorTime; } catch { continue; }

                            double currentWorkingSetMb = 0;
                            try { currentWorkingSetMb = proc.WorkingSet64 / (1024.0 * 1024.0); } catch { }

                            DateTime? startTime = null;
                            try { startTime = proc.StartTime; } catch { }

                            string windowTitle = string.Empty;
                            try { windowTitle = proc.MainWindowTitle; } catch { }

                            if (!trackers.TryGetValue(pid, out var tracker))
                            {
                                tracker = new ProcessTracker
                                {
                                    ProcessId = pid,
                                    ProcessName = proc.ProcessName,
                                    StartTime = startTime,
                                    MainWindowTitle = windowTitle,
                                    LastTotalProcessorTime = currentProcTime,
                                    LastWorkingSetMb = currentWorkingSetMb
                                };
                                trackers[pid] = tracker;
                            }
                            else
                            {
                                // Detect PID Reuse or Process Restart:
                                // If process name or start time differs, or processor time went backwards,
                                // this PID was reassigned to a new process. Reset tracker!
                                bool isReassigned = !string.Equals(tracker.ProcessName, proc.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                                                    (startTime.HasValue && tracker.StartTime.HasValue && startTime.Value != tracker.StartTime.Value) ||
                                                    currentProcTime < tracker.LastTotalProcessorTime;

                                if (isReassigned)
                                {
                                    tracker.ProcessName = proc.ProcessName;
                                    tracker.StartTime = startTime;
                                    tracker.MainWindowTitle = windowTitle;
                                    tracker.LastTotalProcessorTime = currentProcTime;
                                    tracker.LastWorkingSetMb = currentWorkingSetMb;
                                    tracker.CpuSamples.Clear();
                                    tracker.HighCpuHits = 0;
                                }
                                else if (elapsedMs > 100)
                                {
                                    var procDeltaMs = (currentProcTime - tracker.LastTotalProcessorTime).TotalMilliseconds;
                                    if (procDeltaMs >= 0)
                                    {
                                        // Normalized to total system CPU (0-100%)
                                        var cpuPercent = (procDeltaMs / (elapsedMs * logicalCores)) * 100.0;
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
                        }
                        catch
                        {
                            // Inaccessible or exited process
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

                progress?.Report($"Sample {i + 1}/{sampleCount} recorded: Total System CPU {snapshot.TotalCpuPercent:F1}%, RAM {snapshot.MemoryUsagePercent:F0}%");

                if (i < sampleCount - 1)
                {
                    intervalStopwatch.Restart();
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

                // Classify: Sustained vs Transient vs Normal
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
                    report.Findings.Add($"Sustained CPU Bottleneck: '{offender.ProcessName}' averaged {offender.AverageCpuPercent:F1}% system CPU (~{offender.SingleCoreEquivalentPercent:F0}% of a single core) across {offender.HighCpuSampleCount} of {offender.TotalSamplesObserved} samples{protText}.");

                    if (!offender.IsProtected)
                    {
                        report.EvidenceBasedSuggestions.Add($"Investigate or close '{offender.ProcessName}' — it is persistently drawing ~{offender.SingleCoreEquivalentPercent:F0}% of a core on your dual-core APU.");
                    }
                    else if (offender.ProcessName.Equals("SearchIndexer", StringComparison.OrdinalIgnoreCase))
                    {
                        report.EvidenceBasedSuggestions.Add("Windows Search Indexer is actively indexing files. Consider limiting indexed locations in Windows Settings > Search > Searching Windows instead of disabling the service.");
                    }
                    else if (offender.ProcessName.Equals("MsMpEng", StringComparison.OrdinalIgnoreCase))
                    {
                        report.EvidenceBasedSuggestions.Add("Windows Defender antimalware service is currently running a real-time scan. This will normalize once current file activity subsides.");
                    }
                }
            }
            else
            {
                report.Findings.Add("No single process sustained abnormally high background CPU load across the sampling window.");
            }

            if (report.TransientSpikeProcesses.Count > 0)
            {
                var topTransient = report.TransientSpikeProcesses.First();
                report.Findings.Add($"Brief CPU burst observed in '{topTransient.ProcessName}' (peak {topTransient.PeakCpuPercent:F1}%), but average was low ({topTransient.AverageCpuPercent:F1}%). This was a normal momentary spike, not a sustained bottleneck.");
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
