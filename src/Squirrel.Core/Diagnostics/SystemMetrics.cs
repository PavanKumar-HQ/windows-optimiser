using System;

namespace Squirrel.Core.Diagnostics
{
    /// <summary>
    /// Represents a point-in-time snapshot of system hardware metrics and Squirrel's own footprint.
    /// </summary>
    public class SystemMetrics
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        // CPU Metrics
        public double TotalCpuPercent { get; set; }
        public int LogicalCoreCount { get; set; }

        // Memory Metrics (RAM)
        public double TotalMemoryMb { get; set; }
        public double UsedMemoryMb { get; set; }
        public double AvailableMemoryMb { get; set; }
        public double MemoryUsagePercent => TotalMemoryMb > 0 ? (UsedMemoryMb / TotalMemoryMb) * 100.0 : 0.0;

        // Primary Storage (C:) Metrics
        public double SystemDriveTotalGb { get; set; }
        public double SystemDriveFreeGb { get; set; }
        public double SystemDriveUsedGb => Math.Max(0, SystemDriveTotalGb - SystemDriveFreeGb);
        public double SystemDriveUsedPercent => SystemDriveTotalGb > 0 ? (SystemDriveUsedGb / SystemDriveTotalGb) * 100.0 : 0.0;
        public double DiskActiveTimePercent { get; set; }
        public double DiskReadBytesPerSec { get; set; }
        public double DiskWriteBytesPerSec { get; set; }

        // Squirrel Self-Monitoring Footprint
        // Explicitly fulfills: "Minimize CPU, RAM, disk usage, dependencies, and background activity.
        // The application must also be lightweight enough that running Squirrel does not itself make my laptop slower."
        public double SquirrelCpuPercent { get; set; }
        public double SquirrelWorkingSetMb { get; set; }
        public double SquirrelPrivateBytesMb { get; set; }

        public override string ToString()
        {
            return $"[{TimestampUtc:HH:mm:ss}] CPU: {TotalCpuPercent:F1}% | RAM: {UsedMemoryMb:F0}/{TotalMemoryMb:F0} MB ({MemoryUsagePercent:F1}%) | Disk: {SystemDriveFreeGb:F1} GB free | Squirrel RAM: {SquirrelWorkingSetMb:F1} MB (CPU {SquirrelCpuPercent:F1}%)";
        }
    }
}
