using System;

namespace Squirrel.Core.Processes
{
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string MainWindowTitle { get; set; } = string.Empty;

        public double WorkingSetMb { get; set; }
        public double PrivateMemoryMb { get; set; }
        public int ThreadCount { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }
        public DateTime? StartTime { get; set; }

        public bool IsProtected { get; set; }
        public string ProtectionReason { get; set; } = string.Empty;
        public bool IsMicrosoftOffice { get; set; }
    }
}
