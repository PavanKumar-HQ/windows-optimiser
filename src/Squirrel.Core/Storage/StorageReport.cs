using System;
using System.Collections.Generic;
using System.Linq;

namespace Squirrel.Core.Storage
{
    public class StorageReport
    {
        public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;
        public double DriveTotalGb { get; set; }
        public double DriveFreeGb { get; set; }
        public double DriveUsedGb => Math.Max(0, DriveTotalGb - DriveFreeGb);
        public double DriveFreePercent => DriveTotalGb > 0 ? (DriveFreeGb / DriveTotalGb) * 100.0 : 0.0;

        public List<CleanupTarget> Targets { get; set; } = new List<CleanupTarget>();

        public long TotalReclaimableBytes => Targets.Where(t => t.IsSafeForCleanup).Sum(t => t.SizeBytes);
        public double TotalReclaimableMb => TotalReclaimableBytes / (1024.0 * 1024.0);
        public double TotalReclaimableGb => TotalReclaimableBytes / (1024.0 * 1024.0 * 1024.0);
    }
}
